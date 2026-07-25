// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;
using Pidgeon.Core.Services.FieldValueResolvers;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.Resolvers;

/// <summary>
/// Resolves HL7-specific field values that have fixed semantic meanings.
/// Handles MSH fields, message control IDs, timestamps, and other HL7 standard values.
/// Priority: 90 (high - HL7 semantics should override random generation)
/// </summary>
public class HL7SpecificFieldResolver : IFieldValueResolver
{
    private readonly ILogger<HL7SpecificFieldResolver> _logger;
    private readonly MshHeaderDefaults _mshDefaults;

    public int Priority => 90;

    public HL7SpecificFieldResolver(
        ILogger<HL7SpecificFieldResolver> logger,
        MshHeaderDefaults mshDefaults)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mshDefaults = mshDefaults ?? throw new ArgumentNullException(nameof(mshDefaults));
    }

    public async Task<string?> ResolveAsync(FieldResolutionContext context)
    {
        await Task.Yield();

        // Handle MSH segment fields with fixed HL7 meanings
        if (context.SegmentCode == "MSH")
        {
            return ResolveMSHField(context);
        }

        // A field bound to a populated HL7 table is a constrained enumeration whose value must
        // come from that table. The heuristic name matches below ("sequence"/"set id" -> "1",
        // etc.) must not override it — e.g. TQ2.2 "Sequence/Results Flag" -> table 0503 (C/R/S),
        // TQ2.6 "Sequence Condition Code" -> table 0504 — so defer to HL7TableFieldResolver (85).
        // MSH is handled above and returns before this gate. Genuine Set ID / sequence-number
        // fields are unbound (no table), so they still resolve to "1" here. Only a coded field
        // carries a table id, so skip the async table lookup entirely for everything else.
        if (context.Field.TableId is > 0 &&
            await HL7TableBinding.GetPopulatedTableAsync(
                context.Field, context.GenerationContext?.TableProvider).ConfigureAwait(false) is not null)
        {
            return null;
        }

        // Handle other HL7-specific fields across segments
        return ResolveCommonHL7Field(context);
    }

    /// <summary>
    /// Resolve MSH segment fields with proper HL7 semantics.
    /// These fields have fixed meanings per HL7 v2.3 specification.
    /// Identity literals come from <see cref="MshHeaderDefaults"/>, the single config
    /// home shared with <see cref="MshSegmentComposer"/>. In the composition flow
    /// <see cref="MshSegmentComposer"/> owns MSH-1..12 (including the MSH-10 control id keyed off
    /// <c>context.Key</c>); this resolver is the schema-driven fallback for MSH fields resolved
    /// outside that composer, drawing the control id from the field coordinate via
    /// <c>context.FieldRng()</c>.
    /// </summary>
    private string? ResolveMSHField(FieldResolutionContext context)
    {
        return context.FieldPosition switch
        {
            1 => "|",                                           // Field Separator - always "|"
            2 => "^~\\&",                                       // Encoding Characters - always "^~\&"
            3 => _mshDefaults.SendingApplication,               // Sending Application
            4 => _mshDefaults.SendingFacility,                  // Sending Facility
            5 => _mshDefaults.ReceivingApplication,             // Receiving Application
            6 => _mshDefaults.ReceivingFacility,                // Receiving Facility
            7 => context.Clock().ToString("yyyyMMddHHmmss"),   // Date/Time of Message
            8 => "",                                           // Security (usually empty)
            9 => context.GenerationContext.MessageType,        // Message Type
            10 => GenerateRandomId(context.FieldRng()),        // Message Control ID
            11 => _mshDefaults.ProcessingId,                    // Processing ID (P=Production)
            12 => context.Options?.Hl7Version ?? "2.3",            // Version ID
            _ => null                                          // Let other resolvers handle
        };
    }

    /// <summary>
    /// Resolve common HL7 fields that appear across multiple segments.
    /// Uses field name patterns to identify HL7-specific semantics.
    /// </summary>
    private string? ResolveCommonHL7Field(FieldResolutionContext context)
    {
        var fieldName = context.Field.Name?.ToLowerInvariant() ?? "";
        var fieldDescription = context.Field.Description?.ToLowerInvariant() ?? "";

        // Timestamp fields
        if (fieldName.Contains("time") || fieldName.Contains("date") ||
            fieldDescription.Contains("time") || fieldDescription.Contains("date"))
        {
            if (context.Field.DataType == "TS" || context.Field.DataType == "DTM")
                return context.Clock().ToString("yyyyMMddHHmmss");
            if (context.Field.DataType == "DT")
                return context.Clock().ToString("yyyyMMdd");
            if (context.Field.DataType == "TM")
                return context.Clock().ToString("HHmmss");
        }

        // Message control and sequence IDs
        if (fieldName.Contains("control") && fieldName.Contains("id"))
            return GenerateRandomId(context.FieldRng());
        if (fieldName.Contains("sequence") || fieldName.Contains("set id"))
            return "1";

        // Version fields
        if (fieldName.Contains("version"))
            return context.Options?.Hl7Version ?? "2.3";

        // Processing ID fields
        if (fieldName.Contains("processing") && fieldName.Contains("id"))
            return "P";

        // No HL7-specific handling for this field
        return null;
    }

    /// <summary>
    /// Generate random message control ID in HL7-appropriate format.
    /// </summary>
    private static string GenerateRandomId(Random rng) => rng.Next(100000, 999999).ToString();
}