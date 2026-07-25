// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Common;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Composes a single HL7 segment occurrence to its wire string. The composer keeps
/// message-level orchestration (version resolution, trigger-event/group iteration, repeat
/// sequencing) and this seam owns the per-segment decision: a value contributor (OBX/RXE/DG1,
/// content as semantics, rendered version-correct by the serializer) when one is registered,
/// the MSH header composer for MSH, otherwise the schema-driven field pipeline; with a
/// minimal-segment fallback when neither a contributor nor a schema is available.
///
/// This is the observation-composer boundary: the OBX/RXE/DG1 builders are value contributors
/// returning coherent value sets, and the serializer owns shape.
/// </summary>
public interface ISegmentComposer
{
    /// <summary>
    /// Composes the wire string for one occurrence of <paramref name="segmentDef"/> at
    /// <paramref name="setId"/> (1-based; the repeat resolver passes incrementing ids so builders
    /// produce distinct content per repetition). Pin overlays are applied to builder output as a
    /// post-pass. Never fails the message on a single segment: a missing schema or a thrown builder
    /// degrades to a minimal segment.
    /// </summary>
    Task<Result<string>> ComposeSegmentAsync(
        TriggerEventSegment segmentDef,
        SegmentGenerationContext context,
        GenerationOptions options,
        int setId = 1);
}
