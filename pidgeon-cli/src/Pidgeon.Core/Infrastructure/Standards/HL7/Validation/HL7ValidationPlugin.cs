// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Standards;
using Pidgeon.Core.Application.Interfaces.Standards.HL7;
using Pidgeon.Core.Domain.Validation;
using Pidgeon.Core.Infrastructure.Standards.Common.HL7;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.Validation;

/// <summary>
/// HL7 v2.x plugin for <see cref="IStandardValidationPlugin"/>. Performs
/// structural validation (required fields, segment order, data type
/// conformance, table-value checks) using HL7 data providers.
///
/// All HL7-specific logic, literals, and provider dependencies live inside this
/// plugin; the dispatcher
/// (<see cref="Application.Services.Validation.MessageValidationService"/>) does not
/// know about HL7. Per-field rule checks (required, timestamp,
/// table-value) live in <see cref="HL7FieldRuleValidator"/>; trigger-event
/// structure rules (required segments, segment order) live in
/// <see cref="HL7StructureRuleValidator"/>; cross-message batch rules
/// (duplicate MSH-10 control ids) live in <see cref="HL7BatchRuleValidator"/>;
/// structural parsing (wire → raw segments, version recovery, trigger
/// derivation) lives in <see cref="IHl7StructuralParser"/>; this plugin owns
/// message partitioning, segment iteration, and issue aggregation.
///
/// Multi-message files (multiple MSH segments) are partitioned per MSH and
/// each message is validated against its own MSH-9 trigger and MSH-12 version;
/// issues carry a 1-based <see cref="ValidationIssue.MessageIndex"/> and the
/// result's Metadata carries "messageCount". Single-message behavior — issues,
/// statistics, and wire shape — is unchanged.
///
/// Validation is version-aware (ENGINE-BREADTH S3 #2): the message's declared
/// version (MSH-12) selects the segment, trigger-event, and table providers via
/// <see cref="IHL7DataProviderFactory"/>, so a v2.7 message validates against
/// v2.7 definitions and tables rather than a fixed v2.3 baseline. Unknown or
/// missing versions fall back to v2.3, matching prior behavior.
/// </summary>
internal sealed class HL7ValidationPlugin : IStandardValidationPlugin
{
    private readonly IHL7DataProviderFactory _dataProviderFactory;
    private readonly IHl7StructuralParser _structuralParser;
    private readonly ILogger<HL7ValidationPlugin> _logger;

    private const string MshSegmentCode = "MSH";
    private const string DefaultVersion = "2.3";

    public string StandardName => "hl7";

    public HL7ValidationPlugin(
        IHL7DataProviderFactory dataProviderFactory,
        IHl7StructuralParser structuralParser,
        ILogger<HL7ValidationPlugin> logger)
    {
        _dataProviderFactory = dataProviderFactory ?? throw new ArgumentNullException(nameof(dataProviderFactory));
        _structuralParser = structuralParser ?? throw new ArgumentNullException(nameof(structuralParser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandle(string messageContent)
    {
        if (string.IsNullOrWhiteSpace(messageContent))
            return false;

        // HL7 v2 pipe-delimited content: an MSH message header, or a batch/file that opens with
        // an FHS/BHS envelope header (HL7 batch protocol). Detect an embedded MSH after either
        // segment terminator (\r, or the \n of a \r\n pair) so an FHS-led batch is still routed
        // to HL7 without an explicit --standard.
        return messageContent.StartsWith($"{MshSegmentCode}|", StringComparison.Ordinal)
            || messageContent.StartsWith("FHS|", StringComparison.Ordinal)
            || messageContent.StartsWith("BHS|", StringComparison.Ordinal)
            || messageContent.Contains($"\r{MshSegmentCode}|", StringComparison.Ordinal)
            || messageContent.Contains($"\n{MshSegmentCode}|", StringComparison.Ordinal);
    }

    public async Task<Result<ValidationResult>> ValidateAsync(
        string messageContent,
        string? profile,
        ValidationMode mode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(messageContent))
            {
                return Result<ValidationResult>.Failure("Message content cannot be empty");
            }

            // The profile parameter is reserved. HL7 vendor profiles today go
            // through IProfileValidationService (a separate code path); when that
            // migrates to the plugin, profile will drive per-vendor rules here.
            if (!string.IsNullOrWhiteSpace(profile))
            {
                _logger.LogDebug(
                    "HL7 profile parameter '{Profile}' ignored by HL7ValidationPlugin (use IProfileValidationService directly).",
                    profile);
            }

            var result = await ValidateHL7Async(messageContent, mode).ConfigureAwait(false);
            return Result<ValidationResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HL7 validation failed with exception");
            return Result<ValidationResult>.Failure($"HL7 validation failed: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // HL7 Validation
    // -------------------------------------------------------------------------

    private async Task<ValidationResult> ValidateHL7Async(string messageContent, ValidationMode mode)
    {
        var segments = _structuralParser.ParseSegments(messageContent);

        if (segments.Count == 0)
        {
            var parseIssues = new List<ValidationIssue>
            {
                new()
                {
                    Location = "Message",
                    Severity = ValidationSeverity.Error,
                    Message = "No segments found in HL7 message",
                    RuleId = "HL7-PARSE-001"
                }
            };

            return BuildResult(mode, parseIssues, 1, 0, 0);
        }

        // Batch/file envelope segments (FHS/BHS/BTS/FTS) frame a group of messages per the HL7
        // batch protocol (2.10.3); they are not messages and belong to no trigger structure.
        // When any are present, route to the envelope-aware path so message partitioning and
        // per-message rules see only messages — otherwise FHS at index 0 of message 1 trips a
        // false HL7-STRUCT-001 (MSH-first) and every envelope segment trips a false
        // HL7-STRUCT-003 (foreign-to-trigger). A file with no envelope keeps the exact prior
        // path, so the single-message fast path and existing multi-message behavior never move.
        if (segments.Any(s => IsEnvelopeSegment(s.Code)))
        {
            return await ValidateBatchWithEnvelopeAsync(segments, mode).ConfigureAwait(false);
        }

        // A file may carry multiple messages (one MSH each). Each message is
        // validated independently against its OWN MSH-9 trigger and MSH-12
        // version — validating the flat segment list against message 1's
        // header mis-graded messages 2..N (e.g. cross-message HL7-CARD-001 on
        // PIDs that belong to separate messages). Single-MSH input is one
        // partition — the same list through the same pipeline as before.
        var partitions = Hl7MessagePartitioner.PartitionByMessageHeader(segments);

        if (partitions.Count == 1)
        {
            var (issues, rulesChecked, rulesPassed, fieldsValidated) =
                await ValidateSingleMessageAsync(partitions[0], mode).ConfigureAwait(false);

            return BuildResult(mode, issues, rulesChecked, rulesPassed, fieldsValidated);
        }

        var allIssues = new List<ValidationIssue>();
        int totalRulesChecked = 0;
        int totalRulesPassed = 0;
        int totalFieldsValidated = 0;

        for (int k = 0; k < partitions.Count; k++)
        {
            var (issues, rulesChecked, rulesPassed, fieldsValidated) =
                await ValidateSingleMessageAsync(partitions[k], mode).ConfigureAwait(false);

            int messageIndex = k + 1;
            allIssues.AddRange(issues.Select(i => i with { MessageIndex = messageIndex }));
            totalRulesChecked += rulesChecked;
            totalRulesPassed += rulesPassed;
            totalFieldsValidated += fieldsValidated;
        }

        // Cross-message rules exist only for multi-message input, so
        // single-message statistics (and the strict census) never move.
        var (batchIssues, batchRulesChecked, batchRulesPassed) =
            HL7BatchRuleValidator.ValidateBatchRules(partitions, mode);
        allIssues.AddRange(batchIssues);
        totalRulesChecked += batchRulesChecked;
        totalRulesPassed += batchRulesPassed;

        var result = BuildResult(mode, allIssues, totalRulesChecked, totalRulesPassed, totalFieldsValidated);
        result.Metadata["messageCount"] = partitions.Count;
        return result;
    }

    // FHS/BHS/BTS/FTS frame a batch/file (HL7 batch protocol, 2.10.3); they are not messages.
    private static readonly HashSet<string> EnvelopeSegmentCodes =
        new(StringComparer.Ordinal) { "FHS", "BHS", "BTS", "FTS" };

    private static bool IsEnvelopeSegment(string code) => EnvelopeSegmentCodes.Contains(code);

    /// <summary>
    /// Validates a file that carries HL7 batch-protocol envelope segments (FHS/BHS/BTS/FTS).
    /// The envelope frames the messages but is not itself a message: its segments are
    /// field-validated against their own schemas (they carry required fields — the file/batch
    /// field separator and encoding characters) but never against a trigger structure and never
    /// the MSH-first rule. The framed messages are partitioned per MSH and validated exactly as
    /// a normal multi-message file, including the cross-message batch rules. A file with an
    /// envelope is always a batch, so issues carry a per-message MessageIndex and the result
    /// carries "messageCount" — a plain (envelope-free) single message is untouched by this path.
    /// </summary>
    private async Task<ValidationResult> ValidateBatchWithEnvelopeAsync(
        IReadOnlyList<RawHl7Segment> segments, ValidationMode mode)
    {
        var envelopeSegments = new List<RawHl7Segment>();
        var messageSegments = new List<RawHl7Segment>();
        foreach (var segment in segments)
        {
            if (IsEnvelopeSegment(segment.Code))
                envelopeSegments.Add(segment);
            else
                messageSegments.Add(segment);
        }

        // Envelopes carry no version of their own; grade them against the version the first
        // framed message declares (MSH-12), falling back to the default when it is absent,
        // empty, or unsupported — the same policy the per-message path uses.
        var declaredVersion = _structuralParser.GetDeclaredVersion(messageSegments);
        var envelopeVersion = declaredVersion is not null && _dataProviderFactory.IsVersionSupported(declaredVersion)
            ? declaredVersion
            : DefaultVersion;

        var allIssues = new List<ValidationIssue>();
        int totalRulesChecked = 0;
        int totalRulesPassed = 0;
        int totalFieldsValidated = 0;

        var (envIssues, envChecked, envPassed, envFields) =
            await ValidateEnvelopeSegmentsAsync(envelopeSegments, envelopeVersion, mode).ConfigureAwait(false);
        allIssues.AddRange(envIssues);
        totalRulesChecked += envChecked;
        totalRulesPassed += envPassed;
        totalFieldsValidated += envFields;

        var partitions = messageSegments.Count > 0
            ? Hl7MessagePartitioner.PartitionByMessageHeader(messageSegments)
            : (IReadOnlyList<IReadOnlyList<RawHl7Segment>>)Array.Empty<IReadOnlyList<RawHl7Segment>>();

        for (int k = 0; k < partitions.Count; k++)
        {
            var (issues, rulesChecked, rulesPassed, fieldsValidated) =
                await ValidateSingleMessageAsync(partitions[k], mode).ConfigureAwait(false);

            int messageIndex = k + 1;
            allIssues.AddRange(issues.Select(i => i with { MessageIndex = messageIndex }));
            totalRulesChecked += rulesChecked;
            totalRulesPassed += rulesPassed;
            totalFieldsValidated += fieldsValidated;
        }

        if (partitions.Count > 0)
        {
            var (batchIssues, batchRulesChecked, batchRulesPassed) =
                HL7BatchRuleValidator.ValidateBatchRules(partitions, mode);
            allIssues.AddRange(batchIssues);
            totalRulesChecked += batchRulesChecked;
            totalRulesPassed += batchRulesPassed;
        }

        var result = BuildResult(mode, allIssues, totalRulesChecked, totalRulesPassed, totalFieldsValidated);
        result.Metadata["messageCount"] = partitions.Count;
        return result;
    }

    /// <summary>
    /// Field-validates each batch-protocol envelope segment against its own schema for the
    /// resolved file version. No trigger-structure, order, membership, or MSH-first rule
    /// applies — an envelope frames messages and belongs to no message type. Envelope field
    /// issues are file-level and carry no MessageIndex (like the cross-message batch rules).
    /// </summary>
    private async Task<(List<ValidationIssue> issues, int rulesChecked, int rulesPassed, int fieldsValidated)>
        ValidateEnvelopeSegmentsAsync(IReadOnlyList<RawHl7Segment> envelopeSegments, string version, ValidationMode mode)
    {
        var issues = new List<ValidationIssue>();
        int rulesChecked = 0;
        int rulesPassed = 0;
        int fieldsValidated = 0;

        var segmentProvider = _dataProviderFactory.GetSegmentProvider(version);
        var tableProvider = _dataProviderFactory.GetTableProvider(version);
        var dataTypeProvider = _dataProviderFactory.GetDataTypeProvider(version);
        var fieldRuleValidator = new HL7FieldRuleValidator(tableProvider, dataTypeProvider);

        foreach (var segment in envelopeSegments)
        {
            var schemaResult = await segmentProvider.GetSegmentAsync(segment.Code).ConfigureAwait(false);
            if (!schemaResult.IsSuccess)
            {
                if (mode == ValidationMode.Strict)
                {
                    issues.Add(new ValidationIssue
                    {
                        Location = segment.Code,
                        Severity = ValidationSeverity.Warning,
                        Message = $"Unknown segment '{segment.Code}' — no schema available for validation",
                        RuleId = "HL7-SEG-UNKNOWN",
                        Suggestion = $"Verify the segment code is valid for HL7 v{version}"
                    });
                }
                continue;
            }

            var schema = schemaResult.Value!;
            var (segIssues, segRulesChecked, segRulesPassed, segFieldsValidated) =
                await fieldRuleValidator.ValidateSegmentFieldsAsync(segment, schema, mode).ConfigureAwait(false);

            issues.AddRange(segIssues);
            rulesChecked += segRulesChecked;
            rulesPassed += segRulesPassed;
            fieldsValidated += segFieldsValidated;
        }

        return (issues, rulesChecked, rulesPassed, fieldsValidated);
    }

    /// <summary>
    /// Runs every per-message validation face — version resolution (MSH-12),
    /// header-first structural check, per-segment field rules, and trigger-event
    /// structure rules — over one message's segments. The caller owns parsing,
    /// aggregation, and result construction.
    /// </summary>
    private async Task<(List<ValidationIssue> issues, int rulesChecked, int rulesPassed, int fieldsValidated)>
        ValidateSingleMessageAsync(IReadOnlyList<RawHl7Segment> segments, ValidationMode mode)
    {
        var issues = new List<ValidationIssue>();

        // Resolve the providers for the version the message declares in MSH-12 so
        // segment shapes, trigger-event structure, and code tables all match that
        // version (not a fixed v2.3 baseline).
        // Recover the version declared in MSH-12; fall back to the default when MSH-12 is
        // absent, empty, or names a version with no embedded reference data, so validation
        // never regresses below the prior v2.3 baseline for unrecognized input.
        var declaredVersion = _structuralParser.GetDeclaredVersion(segments);
        var version = declaredVersion is not null && _dataProviderFactory.IsVersionSupported(declaredVersion)
            ? declaredVersion
            : DefaultVersion;

        // HL7-VER-001 (audit D-15d): the silent v2.3 fallback meant a message declaring an
        // unsupported version was graded against 2.3 definitions with no signal that every
        // finding (and non-finding) came from a different version's rules. Warning in both
        // modes: a coverage disclosure, not a defect verdict.
        if (declaredVersion is not null && version != declaredVersion)
        {
            issues.Add(new ValidationIssue
            {
                Location = "MSH.12",
                Severity = ValidationSeverity.Warning,
                Message = $"MSH-12 declares HL7 version '{declaredVersion}', which has no embedded reference data; validation ran against v{DefaultVersion} definitions",
                RuleId = "HL7-VER-001",
                ExpectedValue = "A version with embedded reference data (2.3–2.8)",
                ActualValue = declaredVersion,
                Suggestion = $"Verify MSH-12; findings below reflect v{DefaultVersion} rules"
            });
        }
        var segmentProvider = _dataProviderFactory.GetSegmentProvider(version);
        var triggerEventProvider = _dataProviderFactory.GetTriggerEventProvider(version);
        var tableProvider = _dataProviderFactory.GetTableProvider(version);
        var dataTypeProvider = _dataProviderFactory.GetDataTypeProvider(version);
        var fieldRuleValidator = new HL7FieldRuleValidator(tableProvider, dataTypeProvider);
        var structureRuleValidator = new HL7StructureRuleValidator(triggerEventProvider, _logger);

        // Header segment must be first
        if (segments[0].Code != MshSegmentCode)
        {
            issues.Add(new ValidationIssue
            {
                Location = MshSegmentCode,
                Severity = ValidationSeverity.Error,
                Message = $"{MshSegmentCode} segment must be the first segment in an HL7 message",
                RuleId = "HL7-STRUCT-001",
                ExpectedValue = $"{MshSegmentCode} as first segment",
                ActualValue = $"{segments[0].Code} as first segment"
            });
        }

        var triggerEventCode = _structuralParser.GetTriggerEventCode(segments);
        int rulesChecked = 0;
        int rulesPassed = 0;
        int fieldsValidated = 0;

        // Segment codes the version's oracle recognizes — the structure-membership rule
        // grades only KNOWN segments (unknown ones already carry HL7-SEG-UNKNOWN).
        var knownSegmentCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var segment in segments)
        {
            var schemaResult = await segmentProvider.GetSegmentAsync(segment.Code).ConfigureAwait(false);
            if (!schemaResult.IsSuccess)
            {
                if (mode == ValidationMode.Strict)
                {
                    issues.Add(new ValidationIssue
                    {
                        Location = segment.Code,
                        Severity = ValidationSeverity.Warning,
                        Message = $"Unknown segment '{segment.Code}' — no schema available for validation",
                        RuleId = "HL7-SEG-UNKNOWN",
                        Suggestion = $"Verify the segment code is valid for HL7 v{version}"
                    });
                }
                continue;
            }

            var schema = schemaResult.Value!;
            knownSegmentCodes.Add(segment.Code);
            var (segIssues, segRulesChecked, segRulesPassed, segFieldsValidated) =
                await fieldRuleValidator.ValidateSegmentFieldsAsync(segment, schema, mode).ConfigureAwait(false);

            issues.AddRange(segIssues);
            rulesChecked += segRulesChecked;
            rulesPassed += segRulesPassed;
            fieldsValidated += segFieldsValidated;
        }

        if (!string.IsNullOrEmpty(triggerEventCode))
        {
            var (structureIssues, structureChecked, structurePassed) =
                await structureRuleValidator.ValidateStructureAsync(
                    segments, triggerEventCode, knownSegmentCodes, mode).ConfigureAwait(false);
            issues.AddRange(structureIssues);
            rulesChecked += structureChecked;
            rulesPassed += structurePassed;
        }

        return (issues, rulesChecked, rulesPassed, fieldsValidated);
    }

    private static ValidationResult BuildResult(
        ValidationMode mode,
        List<ValidationIssue> issues,
        int rulesChecked,
        int rulesPassed,
        int fieldsValidated)
    {
        bool isValid = !issues.Any(i => i.Severity == ValidationSeverity.Error);

        return new ValidationResult
        {
            IsValid = isValid,
            Standard = "hl7",
            Mode = mode,
            Issues = issues,
            Statistics = new ValidationStatistics
            {
                TotalRulesChecked = rulesChecked,
                RulesPassed = rulesPassed,
                RulesFailed = rulesChecked - rulesPassed,
                FieldsValidated = fieldsValidated,
                ValidationTime = TimeSpan.Zero
            }
        };
    }
}
