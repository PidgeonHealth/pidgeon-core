// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Decides how many times a repeatable ("∞") segment is emitted for a given message.
/// Non-repeatable segments return 1; a caller
/// override from <see cref="GenerationOptions.SegmentRepeatCounts"/> wins when present; otherwise
/// the count is a single draw from the per-message RNG on <see cref="SegmentGenerationContext"/>
/// against a purpose-aware default range, so output is reproducible for a given seed.
/// </summary>
public interface ISegmentRepeatResolver
{
    /// <summary>
    /// Returns the repeat count (>= 1) for <paramref name="segmentDef"/>. Returns 1 for
    /// non-repeatable segments; honors a <see cref="GenerationOptions.SegmentRepeatCounts"/>
    /// override; otherwise draws once from the segment's repeat coordinate off <c>context.Key</c>
    /// within <see cref="HL7SegmentStatistics.DefaultRepeatRange"/>.
    /// </summary>
    Task<int> ResolveRepeatCountAsync(
        TriggerEventSegment segmentDef,
        SegmentGenerationContext context,
        GenerationOptions options);
}
