// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;
using Pidgeon.Core.Services.FieldValueResolvers;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.Resolvers;

/// <summary>
/// Resolves HL7 field values using official HL7 table definitions.
/// Maps segment fields to their corresponding HL7 tables and returns valid coded values.
/// Priority: 85 (between HL7Specific at 90 and Demographic at 80)
/// </summary>
public class HL7TableFieldResolver : IFieldValueResolver
{
    private readonly ILogger<HL7TableFieldResolver> _logger;
    private readonly IHL7TableProvider _tableProvider;
    private readonly IHL7FallbackValueSource _fallbacks;

    /// <summary>
    /// Maps segment code and field position to HL7 table ID.
    /// Key format: "SEGMENT-POSITION" (e.g., "EVN-1", "PID-8", "ORC-1")
    /// </summary>
    private static readonly Dictionary<string, int> FieldToTableMap = new()
    {
        // MSH - Message Header
        { "MSH-15", 10 },      // Accept Acknowledgment Type
        { "MSH-16", 10 },      // Application Acknowledgment Type
        { "MSH-18", 211 },     // Character Set
        { "MSH-19", 296 },     // Principal Language Of Message

        // EVN - Event Type
        { "EVN-1", 3 },        // Event Type Code (A01, A04, R01, etc.)
        { "EVN-6", 62 },       // Event Occurred

        // PID - Patient Identification
        { "PID-8", 1 },        // Administrative Sex
        { "PID-10", 5 },       // Race
        { "PID-16", 2 },       // Marital Status
        { "PID-17", 6 },       // Religion
        { "PID-23", 136 },     // Citizenship
        { "PID-24", 212 },     // Nationality
        { "PID-26", 231 },     // Student Indicator
        { "PID-28", 189 },     // Ethnicity
        { "PID-30", 136 },     // Patient Death Indicator

        // PD1 - Patient Additional Demographic
        { "PD1-2", 223 },      // Living Dependency
        { "PD1-3", 220 },      // Living Arrangement
        { "PD1-6", 140 },      // Military Branch
        { "PD1-7", 141 },      // Military Rank/Grade
        { "PD1-8", 142 },      // Military Status
        { "PD1-10", 136 },     // Duplicate Patient

        // NK1 - Next of Kin / Associated Parties
        { "NK1-3", 63 },       // Relationship
        { "NK1-7", 1 },        // Contact Role
        { "NK1-15", 1 },       // Administrative Sex

        // PV1 - Patient Visit
        { "PV1-2", 4 },        // Patient Class (E, I, O, P, R)
        { "PV1-10", 69 },      // Hospital Service
        { "PV1-15", 23 },      // Admit Source
        { "PV1-16", 9 },       // VIP Indicator
        { "PV1-18", 32 },      // Patient Type
        { "PV1-20", 32 },      // Financial Class
        { "PV1-36", 112 },     // Discharge Disposition
        { "PV1-38", 21 },      // Diet Type
        { "PV1-40", 87 },      // Bed Status
        { "PV1-41", 110 },     // Account Status
        { "PV1-42", 129 },     // Pending Location
        { "PV1-43", 129 },     // Prior Patient Location
        { "PV1-45", 92 },      // Discharge Date/Time

        // PV2 - Patient Visit - Additional Info
        { "PV2-1", 129 },      // Prior Pending Location
        { "PV2-2", 129 },      // Accommodation Code
        { "PV2-3", 7 },        // Admit Reason
        { "PV2-4", 7 },        // Transfer Reason
        { "PV2-9", 63 },       // Expected Discharge Disposition
        { "PV2-16", 213 },     // Newborn Baby Indicator
        { "PV2-17", 136 },     // Baby Detained Indicator
        { "PV2-21", 214 },     // Visit Publicity Code
        { "PV2-22", 136 },     // Visit Protection Indicator
        { "PV2-33", 136 },     // Expected Surgery Indicator

        // ORC - Common Order
        { "ORC-1", 119 },      // Order Control (NW, CA, UA, etc.)
        { "ORC-5", 38 },       // Order Status
        { "ORC-9", 39 },       // Transaction Code
        { "ORC-15", 294 },     // Order Effective Date/Time
        { "ORC-16", 42 },      // Order Control Code Reason

        // OBR - Observation Request
        { "OBR-11", 65 },      // Specimen Action Code
        { "OBR-13", 74 },      // Danger Code
        { "OBR-15", 111 },     // Specimen Source
        { "OBR-18", 87 },      // Filler Field 1
        { "OBR-19", 87 },      // Filler Field 2
        { "OBR-24", 74 },      // Diagnostic Serv Sect ID
        { "OBR-25", 123 },     // Result Status (F, P, C, X, I, etc.)
        { "OBR-27", 102 },     // Quantity/Timing
        { "OBR-38", 124 },     // Transport Logistics Of Collected Sample
        { "OBR-39", 125 },     // Collector's Comment
        { "OBR-40", 88 },      // Transport Arrangement Responsibility
        { "OBR-43", 74 },      // Planned Patient Transport Comment

        // OBX - Observation/Result
        { "OBX-2", 125 },      // Value Type
        { "OBX-8", 78 },       // Abnormal Flags (N, L, H, etc.)
        { "OBX-10", 80 },      // Nature of Abnormal Test
        { "OBX-11", 85 },      // Observation Result Status (F, P, C, etc.)

        // DG1 - Diagnosis
        { "DG1-3", 51 },       // Diagnosis Coding Method
        { "DG1-6", 52 },       // Diagnosis Type
        { "DG1-15", 53 },      // Diagnosis Priority
        { "DG1-17", 54 },      // Diagnosing Clinician
        { "DG1-19", 55 },      // Attestation Date/Time

        // PR1 - Procedures
        { "PR1-5", 59 },       // Procedure Code
        { "PR1-6", 230 },      // Procedure Functional Type
        { "PR1-15", 59 },      // Associated Diagnosis Code

        // GT1 - Guarantor
        { "GT1-5", 1 },        // Guarantor Sex
        { "GT1-9", 63 },       // Guarantor Relationship
        { "GT1-12", 66 },      // Guarantor Employer Name
        { "GT1-26", 68 },      // Guarantor Household Annual Income
        { "GT1-27", 189 },     // Guarantor Household Size
        { "GT1-28", 296 },     // Guarantor Primary Language
        { "GT1-29", 220 },     // Living Arrangement
        { "GT1-44", 1 },       // Guarantor Contact Person's Name

        // IN1 - Insurance
        { "IN1-15", 86 },      // Plan Type
        { "IN1-19", 1 },       // Insured's Sex
        { "IN1-22", 98 },      // Coordination Of Benefits
        { "IN1-23", 136 },     // Coordination Of Benefits Priority
        { "IN1-43", 63 },      // Insured's Relationship To Patient
        { "IN1-45", 189 },     // Insured's Ethnicity
        { "IN1-48", 220 },     // Insured's Living Arrangement

        // AL1 - Patient Allergy Information
        { "AL1-2", 127 },      // Allergen Type Code (DA, FA, MA, EA)
        { "AL1-4", 128 },      // Allergy Severity Code (SV, MO, MI)
        { "AL1-5", 436 },      // Allergy Reaction Code
        { "AL1-6", 127 },      // Identification Date

        // IAM - Patient Adverse Reaction Information
        { "IAM-2", 127 },      // Allergen Type Code
        { "IAM-4", 128 },      // Allergy Severity Code
        { "IAM-5", 436 },      // Allergy Reaction Code

        // NTE - Notes and Comments
        { "NTE-2", 105 },      // Source of Comment (L, P, O, etc.)
        { "NTE-4", 364 },      // Comment Type
    };

    public int Priority => 85;

    public HL7TableFieldResolver(
        ILogger<HL7TableFieldResolver> logger,
        IHL7TableProvider tableProvider,
        IHL7FallbackValueSource fallbacks)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tableProvider = tableProvider ?? throw new ArgumentNullException(nameof(tableProvider));
        _fallbacks = fallbacks ?? throw new ArgumentNullException(nameof(fallbacks));
    }

    public async Task<string?> ResolveAsync(FieldResolutionContext context)
    {
        try
        {
            // Build lookup key: "SEGMENT-POSITION"
            var lookupKey = $"{context.SegmentCode}-{context.FieldPosition}";

            // Prefer the field's own schema table binding — it is authoritative and
            // version-correct. Fall back to the static segment-field map only for fields
            // whose schema omits a table id. The static map predates schema-driven bindings
            // and is stale in places (e.g. MSH-16 -> 10 "Physician ID" instead of 155),
            // which routed coded fields into GenerateUserDefinedValue's ID/PRV placeholders.
            int tableId;
            if (context.Field.TableId.HasValue && context.Field.TableId.Value > 0)
            {
                tableId = context.Field.TableId.Value;
            }
            else if (FieldToTableMap.TryGetValue(lookupKey, out var mappedTableId))
            {
                tableId = mappedTableId;
            }
            else
            {
                return null;
            }

            // Resolve against the version-aware table provider carried on the generation
            // context so a v2.7 message draws from v2.7's table, not the v2.3-pinned default
            // (the dominant cause of higher-version placeholder/junk codes).
            var tableProvider = context.GenerationContext?.TableProvider ?? _tableProvider;
            var tableResult = await tableProvider.GetTableAsync(tableId);
            if (tableResult.IsFailure)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Table {TableId} not available: {Error}",
                        tableId, tableResult.Error);
                // Generate smart random value for user-defined tables
                return GenerateUserDefinedValue(tableId, context.FieldRng());
            }

            var table = tableResult.Value;

            // Template-notation codes (Q<integer>J<day#>, TH<integer>, <null>) and ellipsis range
            // markers ("O1 ... O9", "10 …19") are spec notation, not sendable values; a table of
            // only those behaves like an empty one for selection.
            var selectableValues = HL7TableCodeTemplates.Selectable(table.Values);

            // Check if table has values
            if (selectableValues.Count == 0)
            {
                // A table that HAS rows but none are selectable is all notation/range (e.g. 0141
                // military rank, entirely "O1 ... O9" graded ranges). It publishes no sendable code.
                if (table.Values.Count > 0)
                {
                    // For an optional field the honest emission is nothing — not a fabricated value
                    // that would trade an HL7-LEN-001 warning for an HL7-TABLE-001 one.
                    if (context.Field.Optionality != "R")
                    {
                        if (_logger.IsEnabled(LogLevel.Debug))
                            _logger.LogDebug("Table {TableId} ({TableName}) has only range/notation rows; omitting optional field",
                                table.Id, table.Name);
                        return string.Empty;
                    }

                    // A required field (or required component of a composite) must carry a value: a
                    // real — if over-length — table member keeps the field table-valid, where a
                    // fabricated ID/PRV placeholder would fail the populated table (HL7-TABLE-001).
                    return table.Values[context.FieldRng().Next(table.Values.Count)].Code;
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Table {TableId} ({TableName}) is empty",
                        table.Id, table.Name);
                // Generate smart random value for user-defined tables
                return GenerateUserDefinedValue(tableId, context.FieldRng());
            }

            // For event type codes, try to match the message type if available
            if (lookupKey == "EVN-1" && context.GenerationContext?.MessageType != null)
            {
                var messageType = context.GenerationContext.MessageType;
                // Extract trigger event from message type (e.g., "ADT^A01" → "A01")
                var triggerEvent = messageType.Contains('^')
                    ? messageType.Split('^')[1]
                    : messageType;

                // Try to find matching event code
                var matchingValue = selectableValues
                    .FirstOrDefault(v => v.Code.Equals(triggerEvent, StringComparison.OrdinalIgnoreCase));

                if (matchingValue != null)
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("Using event code {Code} matching message type {MessageType}",
                            matchingValue.Code, messageType);
                    return matchingValue.Code;
                }
            }

            // Return random valid value from table
            var randomValue = selectableValues[context.FieldRng().Next(selectableValues.Count)];

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Selected random value {Code} from table {TableId} ({TableName})",
                    randomValue.Code, table.Id, table.Name);

            return randomValue.Code;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving field {Segment}-{Position} using HL7 tables",
                context.SegmentCode, context.FieldPosition);
            return null;
        }
    }

    /// <summary>
    /// Generates fallback values for user-defined or empty tables. Placeholder patterns
    /// (provider IDs, diagnosis codes) are generated in code; enumerable value lists come
    /// from <see cref="IHL7FallbackValueSource"/>. A single RNG draw per selection keeps
    /// the seeded draw sequence identical to the historical inline arrays.
    /// </summary>
    private string? GenerateUserDefinedValue(int tableId, Random rng)
    {
        // Physician/Provider placeholder IDs (Table 10)
        if (tableId == 10)
            return $"PRV{rng.Next(10000, 99999)}";

        // Diagnosis-code placeholder pattern (Tables 51, 53)
        if (tableId == 51 || tableId == 53)
            return $"{new[] { "A", "E", "I", "J", "K", "M", "N", "R", "Z" }[rng.Next(9)]}{rng.Next(10, 99)}.{rng.Next(0, 9)}";

        var values = _fallbacks.GetUserDefinedTableFallback(tableId);
        if (values is { Count: > 0 })
            return values[rng.Next(values.Count)];

        // No fallback defined — return null to let other resolvers handle
        return null;
    }
}
