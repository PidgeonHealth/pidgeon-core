// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default statistical knobs for a segment within a trigger event: how likely an
/// optional segment is to appear, and how many times a repeatable segment repeats.
/// </summary>
/// <remarks>
/// These are the same defaults the composer applies during generation. Exposing them
/// as a pure function lets the TUNING workbench show "default vs modified" without the
/// composer and the schema endpoint drifting apart. A trigger-event JSON cell can
/// override these per segment via `inclusion_probability` / `repeat_range` (carried on
/// <see cref="TriggerEventSegment"/>); this class is the fallback when the oracle is silent.
/// </remarks>
public static class HL7SegmentStatistics
{
    /// <summary>
    /// Default inclusion probability (0.0–1.0) for a segment in a generated message.
    /// Required segments are always 1.0; optional segments use purpose-aware odds.
    /// </summary>
    /// <param name="segmentCode">HL7 segment code (e.g. "OBX", "DG1").</param>
    /// <param name="optionality">Trigger-event optionality: "R", "O", or "C".</param>
    /// <param name="groupPath">The segment's group hierarchy within the trigger event.</param>
    public static double DefaultProbability(
        string segmentCode,
        string optionality,
        IReadOnlyList<string>? groupPath)
    {
        // Required segments always render.
        if (optionality == "R")
            return 1.0;

        // NTE (Notes and Comments): occasional only where it sits after a coded fact it can coherently
        // restate, and absent everywhere else. b69/b71 gave the composer real note sources (a lab
        // restatement after an OBX; the corpus + Tier-2 notes for the ordered test, the ordered
        // medication, and the order↔result / all-normal panel summary), so a note carries a coherent
        // remark instead of the fabricated "No additional comments" boilerplate that stacked ten-deep
        // and read as broken output. Each supported position is keyed off its group path by
        // NarrativeNotePosition so this odds table, the inclusion precondition, and the content
        // dispatcher never disagree on which NTE carries which axis. Every other NTE (patient-level,
        // top-level) has no fact in scope and stays absent. The inclusion policy adds the runtime
        // precondition — even at a supported position the NTE renders only when the message actually
        // emitted the fact — so an included note always has a coded fact to wrap. A caller can still
        // surface any NTE explicitly via options.SegmentProbabilities["NTE"] or a pin; fault injection
        // plants its own NTE (SEM-F15).
        if (segmentCode == "NTE")
        {
            return NarrativeNotePosition.Classify(groupPath) switch
            {
                NarrativeNoteKind.Observation => 0.20,
                NarrativeNoteKind.OrderStatus => 0.25,
                NarrativeNoteKind.Medication => 0.25,
                _ => 0.0,
            };
        }

        // OBX inside an ORDER OBSERVATION group (i.e. ORU result payloads): 95%.
        if (segmentCode == "OBX" && groupPath is not null && groupPath.Contains("ORDER OBSERVATION"))
            return 0.95;

        // OBX elsewhere (e.g. admission lab results in ADT): 30%.
        if (segmentCode == "OBX")
            return 0.30;

        // AL1 (Patient Allergy Information): most patients carry no documented drug allergy, so an
        // allergy segment on every-other message reads as fabricated. A documented allergen is the
        // exception, not the rule (the beta-lactam allergen matrix is small and penicillin-weighted,
        // matching that most flagged allergies are drug-class allergies). When present it draws a
        // real allergen from that matrix (AllergyCodedElementResolver); the low odds keep the corpus
        // believable and the segment absent for the clear majority.
        if (segmentCode == "AL1")
            return 0.12;

        // DG1 (Diagnosis): commonly populated.
        if (segmentCode == "DG1")
            return 0.80;

        // Reasonable default for any other optional segment.
        return 0.60;
    }

    /// <summary>
    /// The stable coordinate key for a segment occurrence: its group path joined with its segment
    /// code (e.g. "PATIENT RESULT/ORDER OBSERVATION/OBX"). This is the same (group-path, code) identity
    /// <see cref="DefaultProbability"/> keys on, so a distinct schema cell gets an independent
    /// coordinate-addressed inclusion/repeat draw while the same cell reproduces across runs.
    /// Shared by the inclusion policy and the repeat resolver so a cell's structure (present? how many?)
    /// is addressed identically by both.
    /// </summary>
    public static string CoordinatePath(TriggerEventSegment segmentDef)
        => $"{string.Join("/", segmentDef.GroupPath)}/{segmentDef.SegmentCode}";

    /// <summary>
    /// Default repeat range (inclusive min, inclusive max) for a segment. Non-repeating
    /// segments are fixed at (1, 1). Repeatable segments use purpose-aware ranges derived
    /// from the segment's description.
    /// </summary>
    /// <param name="segmentCode">HL7 segment code (e.g. "OBX", "NTE") — matched exactly, never
    /// against the free-text description, so a segment whose prose merely mentions "note"/"comment"
    /// (ARV, OBX, ORC, ERR, …) is not miscaptured.</param>
    /// <param name="repeatability">Trigger-event repeatability marker ("∞" = repeats).</param>
    /// <param name="description">Segment description (drives the purpose-aware range).</param>
    public static (int Min, int Max) DefaultRepeatRange(string segmentCode, string repeatability, string? description)
    {
        if (repeatability != "∞")
            return (1, 1);

        // NTE (Notes and Comments) emits once when present. A repeated boilerplate remark is the
        // "up to ten 'No additional comments'" pathology; when a caller opts an NTE in, it is a
        // single note, not a stack.
        if (segmentCode == "NTE")
            return (1, 1);

        // Segments whose clinical identity IS the classification get a code-keyed range so the
        // decision never rides free-text prose. The keyword ladder below once carried these, but
        // spec prose is not a contract: the counsel-mandated description trim (2026-07-17) showed
        // some ladder hits were incidental English ("as a result of…" gave ERR five repeats) —
        // those misfires now correct to the default, and the true classifications live here.
        switch (segmentCode)
        {
            case "OBX": case "OBR": case "SPM": case "CSS":
                return (1, 5);
            case "IAM":
                return (1, 2);
        }

        var desc = description?.ToLowerInvariant() ?? string.Empty;

        if (desc.Contains("next of kin") || desc.Contains("emergency contact"))
            return (1, 2);
        if (desc.Contains("observation") || desc.Contains("result"))
            return (1, 5);
        // Allergy: a patient who has a documented allergy usually has one or two, not a stack.
        if (desc.Contains("allergy") || desc.Contains("adverse"))
            return (1, 2);
        if (desc.Contains("diagnosis") || desc.Contains("condition"))
            return (1, 3);
        if (desc.Contains("guarantor") || desc.Contains("financial"))
            return (1, 2);

        // Default repeatable fallback.
        return (1, 2);
    }
}
