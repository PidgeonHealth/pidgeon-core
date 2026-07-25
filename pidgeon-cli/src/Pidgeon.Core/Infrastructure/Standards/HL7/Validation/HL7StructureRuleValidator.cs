// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Domain.Validation;
using Pidgeon.Core.Infrastructure.Standards.Common.HL7;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.Validation;

/// <summary>
/// Owns the trigger-event structure rules for HL7 validation: required-segment
/// presence (HL7-ORDER-001), segment order (HL7-ORDER-002), and structure
/// membership (HL7-STRUCT-003), graded against the resolved trigger event's
/// segment structure. The plugin (<see cref="HL7ValidationPlugin"/>) handles
/// message parsing, per-segment field iteration, and issue aggregation; this
/// collaborator runs the message-level structure checks.
/// </summary>
internal sealed class HL7StructureRuleValidator
{
    private readonly IHL7TriggerEventProvider _triggerEventProvider;
    private readonly ILogger _logger;

    public HL7StructureRuleValidator(IHL7TriggerEventProvider triggerEventProvider, ILogger logger)
    {
        _triggerEventProvider = triggerEventProvider ?? throw new ArgumentNullException(nameof(triggerEventProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<(List<ValidationIssue> issues, int rulesChecked, int rulesPassed)> ValidateStructureAsync(
        IReadOnlyList<RawHl7Segment> actualSegments,
        string triggerEventCode,
        IReadOnlySet<string> knownSegmentCodes,
        ValidationMode mode)
    {
        var issues = new List<ValidationIssue>();
        int rulesChecked = 0;
        int rulesPassed = 0;

        var eventResult = await _triggerEventProvider.GetTriggerEventAsync(triggerEventCode).ConfigureAwait(false);
        if (!eventResult.IsSuccess)
        {
            // HL7-STRUCT-002 (audit D-15c): an unknown trigger used to disable ALL
            // required-segment/order/membership checks silently — an ADT^A99 with no EVN
            // or PV1 validated clean. The skip is unavoidable (no structure to grade
            // against) but must be visible: a warning in both modes, because it is a
            // coverage signal, not a defect verdict.
            _logger.LogDebug("Trigger event {Code} not found for structure validation", triggerEventCode);
            issues.Add(new ValidationIssue
            {
                Location = "MSH.9",
                Severity = ValidationSeverity.Warning,
                Message = $"Trigger event {triggerEventCode} is not recognized; structural validation (required segments, order, membership, cardinality) was skipped",
                RuleId = "HL7-STRUCT-002",
                ExpectedValue = "A trigger event with an embedded structure definition",
                ActualValue = triggerEventCode,
                Suggestion = "Verify MSH-9 names a real trigger event for this HL7 version"
            });
            // Zero rules checked: an uncheckable structure is a coverage gap (disclosed by
            // the warning above), not a failed rule — it must not dent the conformance score.
            return (issues, 0, 0);
        }

        var triggerEvent = eventResult.Value!;
        var actualCodes = actualSegments.Select(s => s.Code).ToList();

        var orderIssues = ValidateOrderRules(triggerEvent, actualCodes, triggerEventCode, mode);
        issues.AddRange(orderIssues);
        rulesChecked += orderIssues.Count > 0 ? orderIssues.Count : 1;
        rulesPassed += orderIssues.Count == 0 ? 1 : 0;

        var (membershipIssues, membershipChecked, membershipPassed) =
            ValidateMembershipRules(triggerEvent, actualCodes, knownSegmentCodes, triggerEventCode, mode);
        issues.AddRange(membershipIssues);
        rulesChecked += membershipChecked;
        rulesPassed += membershipPassed;

        var (cardinalityIssues, cardinalityChecked, cardinalityPassed) =
            ValidateCardinalityRules(triggerEvent, actualCodes, triggerEventCode, mode);
        issues.AddRange(cardinalityIssues);
        rulesChecked += cardinalityChecked;
        rulesPassed += cardinalityPassed;

        return (issues, rulesChecked, rulesPassed);
    }

    /// <summary>
    /// HL7-CARD-001 (audit D-15b): a segment repeated beyond what the structure allows —
    /// two PID segments (two different patients!) in one ADT^A01 used to pass strict
    /// validation untouched. Deliberately conservative to avoid false positives: a code
    /// is only flagged when it appears MORE times than it has structure positions AND no
    /// position (or its enclosing repeating-group context) permits repetition. Codes the
    /// structure doesn't contain are the membership rule's finding, not this one's.
    /// </summary>
    private static (List<ValidationIssue> issues, int rulesChecked, int rulesPassed) ValidateCardinalityRules(
        TriggerEvent triggerEvent,
        List<string> actualCodes,
        string triggerEventCode,
        ValidationMode mode)
    {
        var issues = new List<ValidationIssue>();
        int rulesChecked = 0;
        int rulesPassed = 0;

        // Group headers that repeat make every member effectively repeatable.
        var repeatingGroups = new HashSet<string>(
            triggerEvent.Segments.Where(s => s.IsGroup && s.Repeatability == "∞").Select(s => s.SegmentCode),
            StringComparer.Ordinal);

        var entriesByCode = triggerEvent.Segments
            .Where(s => !s.IsGroup)
            .GroupBy(s => s.SegmentCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var group in actualCodes.GroupBy(c => c, StringComparer.Ordinal))
        {
            if (group.Count() <= 1)
                continue;
            if (!entriesByCode.TryGetValue(group.Key, out var entries))
                continue; // foreign or unknown segment — the membership/unknown rules own it

            rulesChecked++;
            var mayRepeat = entries.Any(e =>
                e.Repeatability == "∞" || e.GroupPath.Any(repeatingGroups.Contains));

            if (mayRepeat || group.Count() <= entries.Count)
            {
                rulesPassed++;
                continue;
            }

            issues.Add(new ValidationIssue
            {
                Location = group.Key,
                Severity = mode == ValidationMode.Compatibility
                    ? ValidationSeverity.Warning
                    : ValidationSeverity.Error,
                Message = $"Segment {group.Key} appears {group.Count()} times but the {triggerEventCode} structure allows at most {entries.Count}",
                RuleId = "HL7-CARD-001",
                ExpectedValue = $"At most {entries.Count} occurrence(s) of {group.Key}",
                ActualValue = $"{group.Count()} occurrences",
                Suggestion = $"Remove the extra {group.Key} segment(s) or verify the message was not concatenated"
            });
        }

        return (issues, rulesChecked, rulesPassed);
    }

    /// <summary>
    /// HL7-STRUCT-003 (audit D-15a): a KNOWN segment (a schema exists for it) that has
    /// no position in the trigger event's structure is foreign to the message type —
    /// an RXE pharmacy order inside an ADT admit is a structural defect even though
    /// the segment itself is well-formed. Unknown segments are excluded (they already
    /// carry HL7-SEG-UNKNOWN; double-flagging one root cause muddies the diagnosis),
    /// and Z-segments are always legal — they are the standard's own extension point
    /// for locally-defined content.
    /// </summary>
    private static (List<ValidationIssue> issues, int rulesChecked, int rulesPassed) ValidateMembershipRules(
        TriggerEvent triggerEvent,
        List<string> actualCodes,
        IReadOnlySet<string> knownSegmentCodes,
        string triggerEventCode,
        ValidationMode mode)
    {
        var issues = new List<ValidationIssue>();
        int rulesChecked = 0;
        int rulesPassed = 0;

        var structureCodes = new HashSet<string>(
            triggerEvent.Segments.Where(s => !s.IsGroup).Select(s => s.SegmentCode),
            StringComparer.Ordinal);

        foreach (var code in actualCodes.Distinct(StringComparer.Ordinal))
        {
            if (!knownSegmentCodes.Contains(code))
                continue;
            if (code.StartsWith('Z'))
                continue;

            rulesChecked++;
            if (structureCodes.Contains(code))
            {
                rulesPassed++;
                continue;
            }

            issues.Add(new ValidationIssue
            {
                Location = code,
                Severity = mode == ValidationMode.Compatibility
                    ? ValidationSeverity.Warning
                    : ValidationSeverity.Error,
                Message = $"Segment {code} is not part of the {triggerEventCode} message structure",
                RuleId = "HL7-STRUCT-003",
                ExpectedValue = $"Only segments defined in the {triggerEventCode} structure (or Z-segments)",
                ActualValue = code,
                Suggestion = $"Remove the {code} segment or verify MSH-9 names the intended trigger event"
            });
        }

        return (issues, rulesChecked, rulesPassed);
    }

    private static List<ValidationIssue> ValidateOrderRules(
        TriggerEvent triggerEvent, List<string> actualCodes, string triggerEventCode, ValidationMode mode)
    {
        var issues = new List<ValidationIssue>();

        // A segment marked R inside an OPTIONAL group is only required when that group is present.
        // Map each group name to its optionality (from the group-header segments) and collect the
        // groups actually instantiated in the message (those whose members appear). A required
        // segment is demanded unless some OPTIONAL group on its path is absent — required groups and
        // top-level segments stay always-demanded, so a genuinely dropped required segment (a
        // top-level PID, or a required content group) still surfaces rather than being masked.
        var groupOptionality = triggerEvent.Segments
            .Where(s => s.IsGroup)
            .GroupBy(s => s.SegmentCode)
            .ToDictionary(g => g.Key, g => g.First().Optionality);

        // Group presence is inferred only from segment codes that are UNAMBIGUOUS members of a group.
        // A code that also appears top-level, or in disjoint groups, tells us nothing about which
        // optional group was instantiated: e.g. ROL is defined top-level AND inside PROCEDURE AND
        // INSURANCE, so a lone ROL must not flag PROCEDURE/INSURANCE present and demand PR1/IN1.
        // A code signals presence only for the group(s) common to every one of its definitions,
        // and only when none of those definitions is top-level.
        var presentGroups = new HashSet<string>();
        foreach (var byCode in triggerEvent.Segments.Where(s => !s.IsGroup).GroupBy(s => s.SegmentCode))
        {
            if (!actualCodes.Contains(byCode.Key))
                continue;

            var defs = byCode.ToList();
            if (defs.Any(d => d.GroupPath.Count == 0))
                continue; // ambiguous: this code also occurs top-level

            IEnumerable<string> common = defs[0].GroupPath;
            foreach (var d in defs.Skip(1))
                common = common.Intersect(d.GroupPath);

            foreach (var g in common)
                presentGroups.Add(g);
        }

        bool IsRequiredHere(TriggerEventSegment seg) =>
            seg.GroupPath.All(g =>
                (groupOptionality.TryGetValue(g, out var opt) && opt == "R") || presentGroups.Contains(g));

        var expectedRequired = triggerEvent.Segments
            .Where(s => s.Optionality == "R" && !s.IsGroup)
            .OrderBy(s => s.OrderIndex)
            .ToList();

        foreach (var requiredSeg in expectedRequired)
        {
            if (!IsRequiredHere(requiredSeg))
            {
                continue;
            }

            if (!actualCodes.Contains(requiredSeg.SegmentCode))
            {
                var severity = mode == ValidationMode.Compatibility
                    ? ValidationSeverity.Warning
                    : ValidationSeverity.Error;

                issues.Add(new ValidationIssue
                {
                    Location = requiredSeg.SegmentCode,
                    Severity = severity,
                    Message = $"Required segment {requiredSeg.SegmentCode} ({requiredSeg.SegmentDesc}) is missing for trigger event {triggerEventCode}",
                    RuleId = "HL7-ORDER-001",
                    ExpectedValue = $"{requiredSeg.SegmentCode} present at order position {requiredSeg.OrderIndex}",
                    ActualValue = "Segment not found",
                    Suggestion = $"Add the {requiredSeg.SegmentCode} segment to the message"
                });
            }
        }

        if (mode == ValidationMode.Strict)
        {
            // A segment code can occupy multiple structure positions (TQ1 in both the
            // TIMING and TIMING ENCODED groups of RDE_O11; RXR in ORDER DETAIL and again
            // after RXE). IndexOf below always resolves a code's FIRST message occurrence,
            // so keep only the EARLIEST spec entry per code — pairing a later spec entry
            // with the first occurrence flags false order violations on conformant
            // messages. Order validation of multi-position segments via first occurrence
            // is inherently approximate; the exact structure walk belongs to the ADR-0001
            // conformance work.
            var presentRequired = expectedRequired
                .GroupBy(s => s.SegmentCode)
                .Select(g => g.First())
                .Where(s => actualCodes.Contains(s.SegmentCode) && IsRequiredHere(s))
                .ToList();

            for (int i = 1; i < presentRequired.Count; i++)
            {
                var prev = presentRequired[i - 1];
                var curr = presentRequired[i];

                int prevIdx = actualCodes.IndexOf(prev.SegmentCode);
                int currIdx = actualCodes.IndexOf(curr.SegmentCode);

                if (prevIdx > currIdx)
                {
                    issues.Add(new ValidationIssue
                    {
                        Location = curr.SegmentCode,
                        Severity = ValidationSeverity.Error,
                        Message = $"Segment {curr.SegmentCode} appears before {prev.SegmentCode}, violating required segment order for {triggerEventCode}",
                        RuleId = "HL7-ORDER-002",
                        ExpectedValue = $"{prev.SegmentCode} before {curr.SegmentCode}",
                        ActualValue = $"{curr.SegmentCode} at position {currIdx}, {prev.SegmentCode} at position {prevIdx}",
                        Suggestion = $"Reorder segments so {prev.SegmentCode} precedes {curr.SegmentCode}"
                    });
                }
            }
        }

        return issues;
    }
}
