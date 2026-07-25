// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default <see cref="ISegmentRepeatResolver"/>: data-driven repeat-count resolution. Uses
/// caller-supplied counts when present, otherwise a medically realistic default range derived
/// from the segment's purpose and shared with the tuning workbench via
/// <see cref="HL7SegmentStatistics"/>. Every random draw reads the per-message RNG off the
/// supplied context, so output is reproducible for a given seed.
/// </summary>
public class SegmentRepeatResolver : ISegmentRepeatResolver
{
    public async Task<int> ResolveRepeatCountAsync(
        TriggerEventSegment segmentDef,
        SegmentGenerationContext context,
        GenerationOptions options)
    {
        if (segmentDef.Repeatability != "∞")
            return 1;

        // Check if user provided specific repeat counts in options
        if (options.SegmentRepeatCounts?.TryGetValue(segmentDef.SegmentCode, out var repeatCount) == true)
        {
            return repeatCount;
        }

        // Coordinate-addressed repeat draw: a pure function of the message key and the
        // segment's stable schema position, so the repeat count is invariant under field draw-order
        // changes instead of consuming the positional per-message RNG.
        var rng = context.Key.Derive("segment-repeat").Derive(HL7SegmentStatistics.CoordinatePath(segmentDef)).AsRandom();

        // Oracle-declared repeat range from the trigger-event JSON, when the cell carries one.
        if (segmentDef is { RepeatMin: { } declaredMin, RepeatMax: { } declaredMax })
        {
            return rng.Next(declaredMin, declaredMax + 1);
        }

        // Medically realistic defaults are derived from the segment's purpose and shared
        // with the tuning workbench via HL7SegmentStatistics.
        var segmentSchemaResult = await context.SegmentProvider!.GetSegmentAsync(segmentDef.SegmentCode);
        var description = segmentSchemaResult.IsSuccess ? segmentSchemaResult.Value.Description : null;
        var (min, max) = HL7SegmentStatistics.DefaultRepeatRange(segmentDef.SegmentCode, segmentDef.Repeatability, description);
        return rng.Next(min, max + 1);
    }
}
