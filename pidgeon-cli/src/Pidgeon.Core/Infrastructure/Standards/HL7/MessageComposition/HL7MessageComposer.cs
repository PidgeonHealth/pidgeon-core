// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Common;
using System.Text;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Completely data-driven HL7 message composer that generates any HL7 v2.3 message
/// using only data from Pidgeon.Data JSON files:
/// - trigger_events/*.json for message structure and segment rules
/// - segments/*.json for field definitions and constraints
/// - data_types/*.json for field formatting rules
/// - tables/*.json for coded value options
/// Zero hardcoded message types, segment builders, or field values.
/// </summary>
public class HL7MessageComposer
{
    private readonly ILogger<HL7MessageComposer> _logger;
    private readonly IHL7TableValueSource _tableValueSource;
    private readonly IHL7VersionResolver _versionResolver;
    private readonly ISegmentInclusionPolicy _segmentInclusionPolicy;
    private readonly ISegmentRepeatResolver _segmentRepeatResolver;
    private readonly ISegmentComposer _segmentComposer;
    private readonly ITemporalCoherencePass _temporalCoherencePass;
    private readonly IVendorDialectPass _vendorDialectPass;

    // Per-message deterministic sources, (re)initialized at the top of every ComposeMessageAsync call
    // from the message coordinate key. The RNG is the field composer's fallback stream and is
    // itself coordinate-derived, so no System.Random survives in the value path; the clock is a recorded
    // run input. The composer is registered Scoped and composes messages sequentially within a scope, so
    // reassigning these per call is safe. The initializer is a placeholder, overwritten before first use.
    private Random _random = GenerationKey.Root(0).AsRandom();
    private DateTime _clock = DateTime.Now;

    public HL7MessageComposer(
        ILogger<HL7MessageComposer> logger,
        IHL7TableValueSource tableValueSource,
        IHL7VersionResolver versionResolver,
        ISegmentInclusionPolicy segmentInclusionPolicy,
        ISegmentRepeatResolver segmentRepeatResolver,
        ISegmentComposer segmentComposer,
        ITemporalCoherencePass temporalCoherencePass,
        IVendorDialectPass vendorDialectPass)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tableValueSource = tableValueSource ?? throw new ArgumentNullException(nameof(tableValueSource));
        _versionResolver = versionResolver ?? throw new ArgumentNullException(nameof(versionResolver));
        _segmentInclusionPolicy = segmentInclusionPolicy ?? throw new ArgumentNullException(nameof(segmentInclusionPolicy));
        _segmentRepeatResolver = segmentRepeatResolver ?? throw new ArgumentNullException(nameof(segmentRepeatResolver));
        _segmentComposer = segmentComposer ?? throw new ArgumentNullException(nameof(segmentComposer));
        _temporalCoherencePass = temporalCoherencePass ?? throw new ArgumentNullException(nameof(temporalCoherencePass));
        _vendorDialectPass = vendorDialectPass ?? throw new ArgumentNullException(nameof(vendorDialectPass));
    }

    /// <summary>
    /// Composes any HL7 message type using pure data-driven approach.
    /// Reads trigger event JSON, follows segment rules, uses data type/table definitions.
    /// </summary>
    public async Task<Result<string>> ComposeMessageAsync(
        string messageType,
        Patient patient,
        Encounter? encounter = null,
        Prescription? prescription = null,
        ObservationResult? observation = null,
        GenerationOptions? options = null)
    {
        try
        {
            options ??= new GenerationOptions();

            // Per-message coordinate key: the entropy root for segment composition, derived
            // from the effective seed. The composer narrows it per segment instance; the field composer
            // narrows it per field, so values are addressed by coordinate, not draw order.
            var messageKey = GenerationDeterminism.CreateKey(options).Derive(messageType);

            // Per-message wall-clock (shared by every segment for temporal coherence) and the FieldRng
            // fallback for any resolver reached outside the coordinate-keyed field composer. The fallback
            // is itself coordinate-derived, so no System.Random survives in the value path; in the composed
            // path the field composer always sets FieldKey, so this fallback is never drawn.
            _random = messageKey.Derive("rng-fallback").AsRandom();
            _clock = GenerationDeterminism.CreateClock(options);

            // Convert message type to trigger event code (e.g., "ADT^A01" -> "ADT_A01")
            var triggerEventCode = messageType.Replace("^", "_").ToLower();

            // Resolve the version-specific providers + trigger event, applying ascending
            // auto-fallback when the requested version lacks the event.
            var resolution = await _versionResolver.ResolveAsync(messageType, triggerEventCode, options);
            if (resolution.IsFailure)
                return Result<string>.Failure(resolution.Error);

            var version = resolution.Value.Version;
            var triggerEvent = resolution.Value.TriggerEvent;
            var segmentProvider = resolution.Value.SegmentProvider;
            var dataTypeProvider = resolution.Value.DataTypeProvider;
            var tableProvider = resolution.Value.TableProvider;
            var messageBuilder = new StringBuilder();
            var segmentContext = new SegmentGenerationContext(patient, encounter, prescription, observation, messageType)
            {
                Hl7Version = version,
                Rng = _random,
                Clock = _clock,
                Key = messageKey,
                MessageKey = messageKey,
                SegmentProvider = segmentProvider,
                DataTypeProvider = dataTypeProvider,
                TableProvider = tableProvider,
                VendorProfile = options.ActiveVendorProfile,
                HonorSuppliedPrescription = options.HonorSuppliedPrescription
            };

            // Process all segments according to trigger event definition
            await ProcessSegmentsFromTriggerEvent(triggerEvent.Segments, messageBuilder, segmentContext, options);

            var finalMessage = messageBuilder.ToString().TrimEnd();

            // Post-assembly temporal coherence: clinical events must not
            // post-date MSH-7 for result/event messages. Runs over the assembled message so it
            // can see MSH-7 and every event time together; order messages are left untouched.
            finalMessage = _temporalCoherencePass.Apply(finalMessage);

            // Post-assembly vendor-dialect shaping: when a vendor profile is active, apply its MSH
            // conventions, segment ordering, and (S2) Z-segments. A no-op otherwise, so default
            // (non-vendor) output is byte-for-byte unchanged.
            finalMessage = _vendorDialectPass.Apply(finalMessage, segmentContext);

            _logger.LogDebug("Generated {MessageType} with {SegmentCount} segments using trigger event {TriggerEventCode}",
                messageType, finalMessage.Split('\n').Length, triggerEventCode);

            return Result<string>.Success(finalMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error composing HL7 message {MessageType}", messageType);
            return Result<string>.Failure($"Error composing message: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes segments exactly as defined in trigger event JSON with group-atomic inclusion
    /// (ADR-0001 §8): a group's members live and die with the group — an instantiated group
    /// carries every segment its optionality demands, and an excluded group suppresses its whole
    /// subtree, subgroups included. Inclusion scoping is driven by each entry's authoritative
    /// <see cref="TriggerEventSegment.GroupPath"/> rather than a level-tracking stack: the prior
    /// stack popped at most one frame per encounter and let subgroups draw independently inside
    /// excluded parents, which shipped partial groups (a PID-less ORU^R01 PATIENT/VISIT, an
    /// ORC/OBR-less MDM COMMON ORDER carrying only its TIMING subgroup — the strict-census
    /// HL7-ORDER-001 floor).
    /// </summary>
    private async Task ProcessSegmentsFromTriggerEvent(
        IReadOnlyList<TriggerEventSegment> segments,
        StringBuilder messageBuilder,
        SegmentGenerationContext context,
        GenerationOptions options)
    {
        // Inclusion by group NAME (names are unique within a structure — the same assumption the
        // validator's group-presence inference makes). A group re-encountered later in the walk
        // re-draws and overwrites, which is the correct behavior for sequential sibling scopes.
        var groupIncluded = new Dictionary<string, bool>(StringComparer.Ordinal);

        bool EnclosingGroupsIncluded(IReadOnlyList<string> groupPath)
        {
            foreach (var groupName in groupPath)
            {
                if (!groupIncluded.TryGetValue(groupName, out var included) || !included)
                    return false;
            }

            return true;
        }

        // Force-include the PID segment — and the optional groups that enclose it in this trigger
        // event — when the inclusion draw would otherwise roll the enclosing PATIENT group off.
        // Two callers want this: a cohort sequence (decision D2 — it shares ONE patient, so the
        // segment carrying PID-3/PID-5 must always render or field-level cohort continuity in
        // HL7FieldComposer / CoherentValueSetSerializer is meaningless), and the clinical-profile
        // corpus build (RequirePatientGroup — every clean control must carry a patient to read like
        // a believable feed, without cohort patient-sharing). Narrow by design: only PID's own
        // ancestry is pinned, so every other optional segment keeps its seed-driven inclusion and
        // message realism holds.
        var pinnedPatientGroups = options.IsCohortSequence || options.RequirePatientGroup
            ? PidEnclosingGroupNames(segments)
            : null;

        // Clinical-content profile also guarantees a VXU^V04 carries its defining immunization: RXA is
        // Required within the OPTIONAL ORDER group, so an un-pinned ORDER roll-off produced a spec-legal
        // VXU with no vaccination (b67). Pin the groups enclosing the required RXA so every clinical-corpus
        // VXU carries >=1 RXA. Narrow by design — only RXA's ancestry is pinned, and only for VXU, so
        // every other optional segment keeps its seed-driven inclusion and feed variety holds. Empty (a
        // no-op) for any message that has no RXA.
        var pinnedClinicalGroups = options.RequireClinicalContent
            && context.MessageType.StartsWith("VXU", StringComparison.OrdinalIgnoreCase)
            ? RequiredSegmentEnclosingGroupNames(segments, "RXA")
            : null;

        bool GroupPinned(string groupCode) =>
            pinnedPatientGroups?.Contains(groupCode) == true
            || pinnedClinicalGroups?.Contains(groupCode) == true;

        // A VXU^V04's observation groups are for immunization-specific observations — VIS
        // publication/presentation dates, funding-program eligibility, forecast/contraindication
        // findings — NOT the general scenario labs (troponin, HbA1c, creatinine) the OBX contributor
        // draws. With no immunization-observation data source, an included group filled its Required
        // OBX with a lab that reads as a troponin on an immunization record (b120). Both carriers are
        // OPTIONAL groups — the ORDER-nested OBSERVATION (v2.3.1+) and the patient-level
        // PERSON_OBSERVATION (v2.8) — so the honest rendering is to omit them rather than fabricate an
        // out-of-context observation; RXA (the vaccination itself, pinned by b67) is untouched.
        // Suppressing the draw is side-effect-free — inclusion draws are coordinate-addressed, not
        // positional.
        bool SuppressedImmunizationObservationGroup(string groupCode) =>
            (groupCode == "OBSERVATION" || groupCode == "PERSON_OBSERVATION")
            && context.MessageType.StartsWith("VXU", StringComparison.OrdinalIgnoreCase);

        // Message-wide content identities of every emitted segment (Set ID blanked). Spans the whole
        // walk, not one repeat run, so a repeatable segment whose content is byte-identical to one
        // already emitted ANYWHERE in the message is caught as a fabricated duplicate. The motivating
        // case is cross-cell OBX: the required ORDER OBSERVATION result and the optional SPECIMEN OBX
        // both start at the first lab, so the specimen cell restated the first observation verbatim.
        var emittedIdentities = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < segments.Count; i++)
        {
            var segmentDef = segments[i];

            if (segmentDef.IsGroup)
            {
                // A subgroup of an excluded group is excluded with it — it never draws its own
                // inclusion. Skipping the draw is side-effect-free for every other decision
                // because inclusion draws are coordinate-addressed, not positional. A group that
                // encloses PID is force-included when the PATIENT group is pinned (cohort or
                // clinical profile); the pin short-circuits the draw, which — being coordinate-
                // addressed — perturbs no other segment's decision.
                var includeGroup = EnclosingGroupsIncluded(segmentDef.GroupPath)
                    && !SuppressedImmunizationObservationGroup(segmentDef.SegmentCode)
                    && (GroupPinned(segmentDef.SegmentCode)
                        || _segmentInclusionPolicy.ShouldIncludeSegment(segmentDef, context, options));
                groupIncluded[segmentDef.SegmentCode] = includeGroup;

                // Per-segment loop: guard before interpolation.
                if (_logger.IsEnabled(LogLevel.Trace))
                    _logger.LogTrace("Processing group {GroupCode}: included={Include}",
                        segmentDef.SegmentCode, includeGroup);
                continue;
            }

            // Skip segments whose enclosing groups (any depth) are excluded.
            if (!EnclosingGroupsIncluded(segmentDef.GroupPath))
            {
                continue;
            }

            // Check segment inclusion based on optionality rules from JSON. PID is force-included when
            // the PATIENT group is pinned (cohort's shared patient, or the clinical profile's
            // every-control-has-a-patient guarantee); its enclosing groups are pinned above, so this
            // gate is the only remaining suppressor to override.
            var pinnedPid = pinnedPatientGroups is not null && segmentDef.SegmentCode == "PID";
            if (!pinnedPid && !_segmentInclusionPolicy.ShouldIncludeSegment(segmentDef, context, options))
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                    _logger.LogTrace("Skipping optional segment {SegmentCode}", segmentDef.SegmentCode);
                continue;
            }

            // Generate the first occurrence (setId=1); repeatable segments generate the rest below.
            var segmentResult = await _segmentComposer.ComposeSegmentAsync(segmentDef, context, options, setId: 1);
            if (segmentResult.IsFailure)
            {
                _logger.LogWarning("Failed to generate segment {SegmentCode}: {Error}",
                    segmentDef.SegmentCode, segmentResult.Error);
                continue;
            }

            // Emit each occurrence, dropping a fabricated byte-identical duplicate (Set ID ignored)
            // of a segment already emitted ANYWHERE in this message. This is the cross-cell OBX case
            // (the ORDER OBSERVATION result and the SPECIMEN OBX both starting at the first lab) and
            // the same fabricated-repeat class for other repeatable segments (a restated ROL/ARV/TQ2).
            // The skip is per-occurrence, not per-cell: a later cell that mixes a duplicate first row
            // with a distinct serial draw keeps the distinct one. A REQUIRED cell always emits its
            // first occurrence even when it duplicates — a spec-valid duplicate beats a spec-invalid
            // omission (the residual v2.7+ required-OBX case where a scenario has fewer distinct labs
            // than result cells is a follow-up: it wants a distinct OBX-14 time/value per serial draw).
            var isRequired = segmentDef.Optionality == "R";
            var occurrenceCount = segmentDef.Repeatability == "∞"
                ? await _segmentRepeatResolver.ResolveRepeatCountAsync(segmentDef, context, options)
                : 1;

            var cellEmittedAny = false;
            for (int occurrence = 1; occurrence <= occurrenceCount; occurrence++)
            {
                var occurrenceResult = occurrence == 1
                    ? segmentResult
                    : await _segmentComposer.ComposeSegmentAsync(segmentDef, context, options, setId: occurrence);
                if (occurrenceResult.IsFailure)
                    continue;

                var isNovel = emittedIdentities.Add(RepeatContentIdentity(occurrenceResult.Value));
                if (!isNovel && !(isRequired && !cellEmittedAny))
                {
                    if (_logger.IsEnabled(LogLevel.Trace))
                        _logger.LogTrace("Skipping {SegmentCode} occurrence {Occurrence}: duplicates an earlier segment",
                            segmentDef.SegmentCode, occurrence);
                    continue;
                }

                messageBuilder.AppendLine(occurrenceResult.Value);
                cellEmittedAny = true;
            }
        }
    }

    /// <summary>
    /// Content identity of a repeated segment occurrence, with the Set ID (field 1) blanked so two
    /// occurrences that differ only in their sequence number compare equal. HL7 numbers repeatable
    /// clinical segments (OBX/NTE/DG1/AL1) in field 1; that number is bookkeeping, not distinct
    /// content, so an OBX whose analyte/value/time repeat an earlier one is a fabricated duplicate
    /// even though its OBX-1 differs. A distinct observation (e.g. a distinct OBX-14 time or value)
    /// changes the rest of the line, so genuine serial draws keep separate identities and survive.
    /// </summary>
    private static string RepeatContentIdentity(string segmentLine)
    {
        var fields = segmentLine.Split('|');
        if (fields.Length > 1)
            fields[1] = string.Empty;
        return string.Join("|", fields);
    }

    /// <summary>
    /// The names of the optional groups that enclose the PID segment in this trigger event
    /// (its authoritative <see cref="TriggerEventSegment.GroupPath"/>). Used to force PID's
    /// ancestry included for a cohort sequence. Empty when PID sits at the message top level
    /// (e.g. ADT, MDM) or the structure carries no PID at all.
    /// </summary>
    private static HashSet<string> PidEnclosingGroupNames(IReadOnlyList<TriggerEventSegment> segments)
    {
        var pid = segments.FirstOrDefault(s => !s.IsGroup && s.SegmentCode == "PID");
        return pid is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(pid.GroupPath, StringComparer.Ordinal);
    }

    /// <summary>
    /// The names of the optional groups that enclose a given required segment in this trigger event
    /// (its authoritative <see cref="TriggerEventSegment.GroupPath"/>). Used to force a message's
    /// defining clinical segment present under the clinical-content profile — e.g. RXA on a VXU, which
    /// is Required within the optional ORDER group. Once the enclosing groups are pinned, the segment's
    /// own <c>Optionality == "R"</c> guarantees it renders. Empty when the segment sits at the message
    /// top level (already unconditionally included) or the structure carries no such segment (a no-op).
    /// </summary>
    private static HashSet<string> RequiredSegmentEnclosingGroupNames(
        IReadOnlyList<TriggerEventSegment> segments, string segmentCode)
    {
        var segment = segments.FirstOrDefault(s => !s.IsGroup && s.SegmentCode == segmentCode);
        return segment is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(segment.GroupPath, StringComparer.Ordinal);
    }
}

