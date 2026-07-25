// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default <see cref="ISegmentInclusionPolicy"/>: the optional-segment inclusion decision.
/// Required and pinned segments are forced in; every probabilistic decision reads the
/// per-message RNG off the supplied context, so output is reproducible for a given seed.
/// Default odds come from <see cref="HL7SegmentStatistics"/> so the generation and tuning
/// surfaces never drift.
/// </summary>
public class SegmentInclusionPolicy : ISegmentInclusionPolicy
{
    private readonly IHL7FieldPinner _fieldPinner;

    public SegmentInclusionPolicy(IHL7FieldPinner fieldPinner)
    {
        _fieldPinner = fieldPinner ?? throw new ArgumentNullException(nameof(fieldPinner));
    }

    public bool ShouldIncludeSegment(TriggerEventSegment segmentDef, SegmentGenerationContext context, GenerationOptions options)
    {
        // Required segments always included
        if (segmentDef.Optionality == "R")
            return true;

        // If any pin targets this segment, force-include it. Pinning a field in an otherwise
        // optional segment guarantees the segment appears in the rendered message.
        if (_fieldPinner.HasAnyPinForSegment(segmentDef.SegmentCode, options))
            return true;

        // Narrative-NTE precondition (b71/b73): an occasional NTE restates a coded fact the message
        // already carries, so it is suppressed unless the fact for its structural position has been
        // emitted. With nothing to wrap the note could only be an empty/boilerplate remark. The
        // position→fact mapping is the same NarrativeNotePosition the odds table and the content
        // dispatcher key on, so the three never disagree. Skipping the draw is side-effect-free: the
        // inclusion draws below are coordinate-addressed, not positional. Required and pinned NTEs are
        // already returned above; those the spec or a caller demanded, and the contributor degrades to
        // an honest empty comment when the fact is absent.
        if (segmentDef.SegmentCode == "NTE" && !NarrativeNoteFactAvailable(segmentDef, context))
            return false;

        // Coordinate-addressed inclusion draw: a pure function of the message key and the
        // segment's stable schema position, so the include/exclude decision is invariant under field
        // draw-order changes instead of consuming the positional per-message RNG.
        var rng = context.Key.Derive("segment-include").Derive(HL7SegmentStatistics.CoordinatePath(segmentDef)).AsRandom();

        // Use custom probabilities if provided
        if (options.SegmentProbabilities?.TryGetValue(segmentDef.SegmentCode, out var probability) == true)
        {
            return rng.NextDouble() < probability;
        }

        // Oracle-declared probability from the trigger-event JSON, when the cell carries one.
        if (segmentDef.InclusionProbability is { } declaredProbability)
        {
            return rng.NextDouble() < declaredProbability;
        }

        // Purpose-aware default odds. Shared with the TUNING workbench via
        // HL7SegmentStatistics so the "default vs modified" surface never drifts.
        var defaultProbability = HL7SegmentStatistics.DefaultProbability(
            segmentDef.SegmentCode, segmentDef.Optionality, segmentDef.GroupPath);
        return rng.NextDouble() < defaultProbability;
    }

    /// <summary>
    /// Whether the coded fact a narrative NTE at this position would restate has already been emitted
    /// this message. Keyed on the same <see cref="NarrativeNotePosition"/> the odds table and the
    /// content dispatcher use: an observation NTE needs an emitted OBX; an order NTE needs the ordered
    /// test (OBR-4, cached as the message's first lab); a pharmacy-order NTE needs the resolved chain
    /// medication. An unsupported position (patient-level, top-level) has no fact and is suppressed.
    /// </summary>
    private static bool NarrativeNoteFactAvailable(TriggerEventSegment segmentDef, SegmentGenerationContext context)
        => NarrativeNotePosition.Classify(segmentDef.GroupPath) switch
        {
            NarrativeNoteKind.Observation => context.EmittedObservations.MostRecent is not null,
            NarrativeNoteKind.OrderStatus => context.ObxLabs.Results is { Count: > 0 },
            NarrativeNoteKind.Medication => context.OrderMedication is { Resolved: true, Value: not null },
            _ => false,
        };
}
