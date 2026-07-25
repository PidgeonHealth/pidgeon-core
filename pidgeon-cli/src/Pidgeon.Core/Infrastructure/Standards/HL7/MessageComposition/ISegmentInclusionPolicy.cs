// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Decides whether an optional segment (or segment group) is emitted for a given message.
/// Required and pinned segments are always included; otherwise inclusion is a single draw
/// from the per-message RNG on
/// <see cref="SegmentGenerationContext"/> against a caller-supplied or purpose-aware default
/// probability, so output is reproducible for a given seed.
/// </summary>
public interface ISegmentInclusionPolicy
{
    /// <summary>
    /// Returns true when <paramref name="segmentDef"/> should appear in the message. Required
    /// ("R") and pinned segments always return true; otherwise draws once from the segment's
    /// inclusion coordinate off <c>context.Key</c> against the per-segment probability (a custom
    /// override from <see cref="GenerationOptions.SegmentProbabilities"/> or
    /// <see cref="HL7SegmentStatistics.DefaultProbability"/>).
    /// </summary>
    bool ShouldIncludeSegment(TriggerEventSegment segmentDef, SegmentGenerationContext context, GenerationOptions options);
}
