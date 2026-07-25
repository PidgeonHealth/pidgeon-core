// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Validation;
using Pidgeon.Core.Infrastructure.Standards.Common.HL7;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.Validation;

/// <summary>
/// Owns the cross-message rules that only exist when one file carries multiple
/// HL7 messages: duplicate Message Control ID detection (HL7-BATCH-001). The
/// plugin (<see cref="HL7ValidationPlugin"/>) partitions the file into
/// per-message segment lists and runs this collaborator once over all of them;
/// per-message rules never see across a partition boundary, and this validator
/// never grades anything inside a single message. Pure over its inputs: no
/// providers, no I/O.
/// </summary>
internal sealed class HL7BatchRuleValidator
{
    private const string MshSegmentCode = "MSH";

    // MSH-10 (Message Control ID) is Fields[9] in the pipe-split array:
    // Fields[0] is the segment code and MSH-1 is the field separator itself,
    // so MSH-N maps to Fields[N-1] for N >= 2.
    private const int MessageControlIdFieldIndex = 9;

    /// <summary>
    /// HL7-BATCH-001: MSH-10 must be unique per message within a file. Receivers
    /// deduplicate on the Message Control ID and correlate ACKs by it, so a
    /// repeated value silently drops or misroutes messages. Messages with a
    /// missing or empty MSH-10 are skipped here — the per-message required-field
    /// rules own that omission. Comparison is ordinal and case-sensitive after
    /// trimming (control ids are opaque tokens, not case-folded identifiers).
    /// </summary>
    public static (List<ValidationIssue> issues, int rulesChecked, int rulesPassed) ValidateBatchRules(
        IReadOnlyList<IReadOnlyList<RawHl7Segment>> partitions,
        ValidationMode mode)
    {
        var issues = new List<ValidationIssue>();

        // Control id -> 1-based message ordinals carrying it, in file order.
        var ordinalsByControlId = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (int k = 0; k < partitions.Count; k++)
        {
            var header = partitions[k].FirstOrDefault(s => s.Code == MshSegmentCode);
            if (header is null || header.Fields.Length <= MessageControlIdFieldIndex)
                continue;

            var controlId = header.Fields[MessageControlIdFieldIndex].Trim();
            if (string.IsNullOrEmpty(controlId))
                continue;

            if (!ordinalsByControlId.TryGetValue(controlId, out var ordinals))
            {
                ordinals = new List<int>();
                ordinalsByControlId[controlId] = ordinals;
            }

            ordinals.Add(k + 1);
        }

        int rulesChecked = ordinalsByControlId.Count;
        int rulesPassed = 0;

        foreach (var (controlId, ordinals) in ordinalsByControlId.OrderBy(kv => kv.Value[0]))
        {
            if (ordinals.Count == 1)
            {
                rulesPassed++;
                continue;
            }

            var ordinalList = FormatOrdinals(ordinals);
            issues.Add(new ValidationIssue
            {
                Location = "MSH.10",
                Severity = mode == ValidationMode.Compatibility
                    ? ValidationSeverity.Warning
                    : ValidationSeverity.Error,
                Message = $"Message Control ID '{controlId}' is shared by messages {ordinalList}; MSH-10 must be unique for each message in a file",
                RuleId = "HL7-BATCH-001",
                ExpectedValue = "A unique MSH-10 Message Control ID per message",
                ActualValue = $"'{controlId}' in messages {ordinalList}",
                Suggestion = "Assign each message its own MSH-10 value — receivers deduplicate messages and correlate ACKs by Message Control ID, so a repeated id can silently drop or mismatch messages"
            });
        }

        return (issues, rulesChecked, rulesPassed);
    }

    private static string FormatOrdinals(IReadOnlyList<int> ordinals)
    {
        if (ordinals.Count == 2)
            return $"{ordinals[0]} and {ordinals[1]}";

        return string.Join(", ", ordinals.Take(ordinals.Count - 1)) + $" and {ordinals[^1]}";
    }
}
