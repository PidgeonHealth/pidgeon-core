// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Domain.Configuration.Entities;
using Pidgeon.Core.Domain.Validation;
using Pidgeon.Core.Infrastructure.Standards.Common.HL7;

namespace Pidgeon.Core.Application.Services.Configuration;

/// <summary>
/// Validates healthcare messages against vendor profile field and segment requirements.
/// </summary>
internal class ProfileValidationService : IProfileValidationService
{
    private readonly ILogger<ProfileValidationService> _logger;
    private readonly IHl7StructuralParser _structuralParser;

    public ProfileValidationService(
        ILogger<ProfileValidationService> logger,
        IHl7StructuralParser structuralParser)
    {
        _logger = logger;
        _structuralParser = structuralParser;
    }

    public Task<Result<ProfileValidationResult>> ValidateAsync(string message, VendorSpecification profile)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Task.FromResult(Result<ProfileValidationResult>.Failure(
                Error.Validation("Message cannot be empty", "message")));

        if (profile == null)
            return Task.FromResult(Result<ProfileValidationResult>.Failure(
                Error.Validation("Profile cannot be null", "profile")));

        // A file may carry multiple messages (one MSH each; HL7 batch protocol 2.10.3). A
        // VendorSpecification carries one spec PER message type (MessagesToReceiver /
        // MessagesFromReceiver), so each message must be routed to the spec matching its own
        // MSH-9 message type and graded ONLY against that spec — grading a message against
        // another type's expectations is the defect this path previously carried. Single-message
        // input is one partition (SplitMessages returns the whole string unchanged for <= 1 MSH),
        // so single-message callers keep their exact prior issue set and unstamped MessageIndex.
        var messages = Hl7MessagePartitioner.SplitMessages(message);
        bool stampIndex = messages.Count > 1;

        var issues = new List<ValidationIssue>();
        var rulesChecked = 0;

        for (int k = 0; k < messages.Count; k++)
        {
            var (msgIssues, msgChecked) = ValidateOneMessage(messages[k], profile);
            rulesChecked += msgChecked;

            if (stampIndex)
            {
                int messageIndex = k + 1;
                issues.AddRange(msgIssues.Select(i => i with { MessageIndex = messageIndex }));
            }
            else
            {
                issues.AddRange(msgIssues);
            }
        }

        var hasErrors = issues.Any(i => i.Severity == ValidationSeverity.Error);

        var result = new ProfileValidationResult
        {
            IsValid = !hasErrors,
            ProfileId = profile.Id,
            Issues = issues,
            RulesChecked = rulesChecked
        };

        _logger.LogDebug("Profile validation '{ProfileId}': {RulesChecked} rules checked, {Issues} issues found",
            profile.Id, rulesChecked, issues.Count);

        return Task.FromResult(Result<ProfileValidationResult>.Success(result));
    }

    /// <summary>
    /// Grades one HL7 message against the single message-type spec that matches its MSH-9. A
    /// message whose type is not described by the profile degrades to a named finding rather than
    /// being graded against the wrong spec (a false positive) or silently passing.
    /// </summary>
    private (List<ValidationIssue> issues, int rulesChecked) ValidateOneMessage(
        string singleMessage, VendorSpecification profile)
    {
        var issues = new List<ValidationIssue>();
        var rulesChecked = 0;

        // A profile with no message-type specs carries no rules — nothing to check, nothing to
        // route. (Preserves the "empty profile passes any message" contract.)
        if (profile.MessagesToReceiver.Count == 0 && profile.MessagesFromReceiver.Count == 0)
            return (issues, rulesChecked);

        var (spec, messageType, msh9Present) = ResolveMessageTypeSpec(singleMessage, profile);

        if (spec == null)
        {
            var (ruleId, detail) = msh9Present
                ? ("PROFILE_UNMATCHED_MESSAGE_TYPE",
                   $"Message type '{messageType}' has no matching spec in this profile")
                : ("PROFILE_UNKNOWN_MESSAGE_TYPE",
                   "Cannot determine message type: MSH-9 is missing or empty");

            rulesChecked++;
            issues.Add(new ValidationIssue
            {
                Location = "MSH.9",
                Severity = ValidationSeverity.Warning,
                Message = $"{detail}; message not validated against a message-type spec",
                RuleId = ruleId,
                Suggestion = "Add this message type to the vendor profile, or confirm the message's MSH-9."
            });
            return (issues, rulesChecked);
        }

        var segments = ParseSegments(singleMessage);
        var profileSegmentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (segmentId, segmentSpec) in spec.Segments)
        {
            profileSegmentIds.Add(segmentId);

            // Check required segments exist in message
            if (segmentSpec.Required)
            {
                rulesChecked++;
                if (!segments.ContainsKey(segmentId))
                {
                    issues.Add(new ValidationIssue
                    {
                        Location = segmentId,
                        Severity = ValidationSeverity.Error,
                        Message = $"Required segment '{segmentId}' is missing from message",
                        RuleId = $"PROFILE_MISSING_SEGMENT_{segmentId}"
                    });
                    continue;
                }
            }

            // Check required fields within segment
            if (segments.TryGetValue(segmentId, out var segmentData))
            {
                foreach (var fieldSpec in segmentSpec.Fields.Where(f => f.Usage == FieldUsage.Required))
                {
                    rulesChecked++;
                    var fieldValue = GetFieldValue(segmentData, fieldSpec.Position);
                    if (string.IsNullOrEmpty(fieldValue))
                    {
                        issues.Add(new ValidationIssue
                        {
                            Location = $"{segmentId}.{fieldSpec.Position}",
                            Severity = ValidationSeverity.Error,
                            Message = $"Required field '{fieldSpec.Name}' ({segmentId}.{fieldSpec.Position}) is empty",
                            RuleId = $"PROFILE_MISSING_FIELD_{segmentId}_{fieldSpec.Position}",
                            ExpectedValue = "non-empty value"
                        });
                    }
                }
            }
        }

        // Flag segments present in the message but not defined by THIS message type's spec.
        if (profileSegmentIds.Count > 0)
        {
            foreach (var segmentId in segments.Keys)
            {
                if (!profileSegmentIds.Contains(segmentId))
                {
                    rulesChecked++;
                    issues.Add(new ValidationIssue
                    {
                        Location = segmentId,
                        Severity = ValidationSeverity.Warning,
                        Message = $"Segment '{segmentId}' is not defined in the vendor profile",
                        RuleId = $"PROFILE_UNEXPECTED_SEGMENT_{segmentId}",
                        Suggestion = "This segment may be vendor-specific or not expected by this profile"
                    });
                }
            }
        }

        return (issues, rulesChecked);
    }

    /// <summary>
    /// Resolves the message-type spec for one message from its MSH-9. Profiles are keyed in
    /// practice by the raw MSH-9 composite ("ADT^A01"), the trigger-event lookup form
    /// ("ADT_A01"), or the bare message code ("ADT"); each candidate is tried in that order
    /// against MessagesToReceiver first (the messages a vendor sends) then MessagesFromReceiver.
    /// Returns a null spec when MSH-9 is absent (msh9Present false) or present but unmatched.
    /// </summary>
    private (MessageTypeSpec? spec, string messageType, bool msh9Present) ResolveMessageTypeSpec(
        string singleMessage, VendorSpecification profile)
    {
        var parsed = _structuralParser.ParseSegments(singleMessage);
        var msh9 = ReadMsh9(parsed);

        if (string.IsNullOrEmpty(msh9))
            return (null, string.Empty, false);

        var candidates = new List<string> { msh9 };

        var triggerCode = _structuralParser.GetTriggerEventCode(parsed);
        if (!string.IsNullOrEmpty(triggerCode) && !candidates.Contains(triggerCode))
            candidates.Add(triggerCode);

        var messageCode = msh9.Split('^')[0];
        if (!string.IsNullOrEmpty(messageCode) && !candidates.Contains(messageCode))
            candidates.Add(messageCode);

        foreach (var key in candidates)
        {
            if (profile.MessagesToReceiver.TryGetValue(key, out var toSpec))
                return (toSpec, msh9, true);
            if (profile.MessagesFromReceiver.TryGetValue(key, out var fromSpec))
                return (fromSpec, msh9, true);
        }

        return (null, msh9, true);
    }

    /// <summary>
    /// Reads the raw MSH-9 (message type) value. MSH-9 is Fields[8] in the pipe-split array —
    /// MSH-1 is the field separator consumed by the split and MSH-2 the encoding characters at
    /// Fields[1] — matching the convention <see cref="IHl7StructuralParser.GetTriggerEventCode"/>
    /// uses. Null when there is no MSH segment or MSH-9 is absent or empty.
    /// </summary>
    private static string? ReadMsh9(IReadOnlyList<RawHl7Segment> segments)
    {
        var header = segments.FirstOrDefault(s => s.Code == "MSH");
        if (header == null || header.Fields.Length <= 8)
            return null;

        var msh9 = header.Fields[8];
        return string.IsNullOrEmpty(msh9) ? null : msh9;
    }

    private static Dictionary<string, string[]> ParseSegments(string message)
    {
        var segments = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Length < 3) continue;

            var segmentId = line.Length >= 3 ? line[..3] : line;
            var separator = '|';

            // MSH is special: first char after MSH is the field separator
            if (segmentId == "MSH" && line.Length > 3)
            {
                separator = line[3];
            }

            var fields = line.Split(separator);
            // Last occurrence of a repeated segment code within a SINGLE message wins here (the
            // per-message split above already isolated each message, so cross-message bleed — the
            // b41 defect — no longer applies; only intra-message repeats collapse, matching the
            // prior single-message behavior).
            segments[segmentId] = fields;
        }

        return segments;
    }

    private static string? GetFieldValue(string[] fields, int position)
    {
        // For MSH, field positions are offset by 1 because MSH.1 is the separator itself
        // For other segments, field[0] is the segment ID, field[1] is position 1, etc.
        if (position < 1) return null;
        if (position >= fields.Length) return null;
        return fields[position];
    }
}
