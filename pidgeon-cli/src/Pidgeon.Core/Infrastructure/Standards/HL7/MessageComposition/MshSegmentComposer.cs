// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using Pidgeon.Core.Common;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default <see cref="IMshSegmentComposer"/>: the MSH header composition. MSH-1..12 are
/// version-stable HL7 semantics built from <see cref="MshHeaderDefaults"/> plus the per-message
/// clock/RNG carried on the context; fields past MSH-12 defer to the caller's field engine.
/// A whole-field MSH pin overrides any default.
/// </summary>
public class MshSegmentComposer : IMshSegmentComposer
{
    private readonly IHL7FieldPinner _fieldPinner;
    private readonly MshHeaderDefaults _defaults;
    private readonly Pidgeon.Core.Application.Interfaces.Standards.HL7.IHL7DataProviderFactory _dataProviderFactory;

    public MshSegmentComposer(
        IHL7FieldPinner fieldPinner,
        MshHeaderDefaults defaults,
        Pidgeon.Core.Application.Interfaces.Standards.HL7.IHL7DataProviderFactory dataProviderFactory)
    {
        _fieldPinner = fieldPinner ?? throw new ArgumentNullException(nameof(fieldPinner));
        _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        _dataProviderFactory = dataProviderFactory ?? throw new ArgumentNullException(nameof(dataProviderFactory));
    }

    public async Task<Result<string>> ComposeAsync(
        SegmentSchema segmentSchema,
        SegmentGenerationContext context,
        GenerationOptions options,
        Func<SegmentField, Task<string>> generateTailField)
    {
        var mshBuilder = new StringBuilder("MSH");

        foreach (var field in segmentSchema.Fields)
        {
            string fieldValue;

            // PINS workbench: a whole-field MSH pin overrides the defaults below.
            // (User's responsibility if they pin MSH.1 / MSH.2 — those have HL7-significant
            // values; the override is verbatim.)
            var mshPins = _fieldPinner.GetPinsForField("MSH", field.Position, options);
            if (mshPins.TryGetValue(0, out var mshWholeFieldPin))
            {
                AppendField(mshBuilder, field.Position, mshWholeFieldPin);
                continue;
            }

            // Special handling for MSH fields based on position
            switch (field.Position)
            {
                case 1: // Field Separator - always "|"
                    fieldValue = "|";
                    break;
                case 2: // Encoding Characters - always "^~\&"
                    fieldValue = "^~\\&";
                    break;
                case 3: // Sending Application
                    fieldValue = _defaults.SendingApplication;
                    break;
                case 4: // Sending Facility
                    fieldValue = _defaults.SendingFacility;
                    break;
                case 5: // Receiving Application
                    fieldValue = _defaults.ReceivingApplication;
                    break;
                case 6: // Receiving Facility
                    fieldValue = _defaults.ReceivingFacility;
                    break;
                case 7: // Date/Time of Message - primary temporal anchor for the message
                    var msh7Time = context.Clock;
                    context.GeneratedTimestamps["MSH.7"] = msh7Time;
                    fieldValue = msh7Time.ToString("yyyyMMddHHmmss");
                    break;
                case 8: // Security
                    fieldValue = "";
                    break;
                case 9: // Message Type. HL7 v2.3's CM_MSG has only type + trigger components and
                        // a seven-character wire budget. MSG-3 (message structure) starts in v2.3.1
                        // and must carry the STRUCTURE ID from HL7's published event→structure table:
                        // aliased triggers reuse another event's structure (ADT^A04 → ADT_A01,
                        // MDM^T04 → MDM_T02, SIU^S13 → SIU_S12). Deriving it from type^trigger
                        // shipped a wrong MSH-9.3 for every aliased trigger; the trigger code remains
                        // the fallback for events whose structure is its own name. Only a
                        // two-component type^trigger in v2.3.1+ gets the structure appended; bare
                        // types and v2.3 remain two-component.
                    fieldValue = options.Hl7Version != "2.3"
                        && context.MessageType.Split('^').Length == 2
                        ? $"{context.MessageType}^{await ResolveMessageStructureAsync(context.MessageType, options)}"
                        : context.MessageType;
                    break;
                case 10: // Message Control ID
                    fieldValue = GenerateRandomId(context.Key.Derive(10).AsRandom());
                    break;
                case 11: // Processing ID
                    fieldValue = _defaults.ProcessingId;
                    break;
                case 12: // Version ID
                    fieldValue = options.Hl7Version;
                    break;
                default:
                    // For any other fields, use the composer's standard schema-driven path.
                    fieldValue = await generateTailField(field);
                    break;
            }

            AppendField(mshBuilder, field.Position, fieldValue);
        }

        return Result<string>.Success(mshBuilder.ToString());
    }

    /// <summary>
    /// Resolves the MSH-9.3 message-structure ID for a type^trigger via the version's
    /// trigger-event oracle (`message_structure` in the trigger JSON, HL7's published
    /// event→structure table). Falls back to the trigger code when the oracle carries no
    /// alias — correct for the majority of events whose structure is their own name.
    /// </summary>
    private async Task<string> ResolveMessageStructureAsync(string messageType, GenerationOptions options)
    {
        var triggerCode = messageType.Replace("^", "_");
        var result = await _dataProviderFactory
            .GetTriggerEventProvider(options.Hl7Version)
            .GetTriggerEventAsync(triggerCode)
            .ConfigureAwait(false);

        return result.IsSuccess && !string.IsNullOrEmpty(result.Value!.MessageStructure)
            ? result.Value.MessageStructure!
            : triggerCode;
    }

    // MSH-1/MSH-2 are the field separator and encoding characters: they are appended raw,
    // because the "|" delimiter is itself the value. Every other field is delimiter-prefixed.
    private static void AppendField(StringBuilder builder, int position, string value)
    {
        if (position == 1 || position == 2)
            builder.Append(value);
        else
            builder.Append("|").Append(value);
    }

    private static string GenerateRandomId(Random rng) => rng.Next(100000, 999999).ToString();
}
