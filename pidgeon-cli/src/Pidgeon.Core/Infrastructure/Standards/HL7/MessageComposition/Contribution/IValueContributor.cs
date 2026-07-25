// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// Contributes the clinically-coherent <em>content</em> of an HL7 segment occurrence as semantics
/// (a <see cref="CoherentValueSet"/>), leaving wire shape to the serializer. A contributor owns
/// only content, so version-awareness, CE/CWE width, withdrawn-field suppression and pins are
/// applied once, in the serializer.
/// </summary>
public interface IValueContributor
{
    /// <summary>Segment codes this contributor handles (e.g., "DG1").</summary>
    IReadOnlyList<string> SupportedSegments { get; }

    /// <summary>Higher priority wins when multiple contributors support the same segment.</summary>
    int Priority { get; }

    /// <summary>
    /// Produces the semantic value set for one occurrence of the segment at <paramref name="setId"/>
    /// (1-based; the repeat resolver passes incrementing ids so repeated segments carry distinct
    /// content). The contributor reads clinical data + the per-message RNG/clock from
    /// <paramref name="context"/>; it must consume the RNG identically across runs for a given seed.
    /// </summary>
    Task<CoherentValueSet> ContributeAsync(
        SegmentGenerationContext context,
        GenerationOptions options,
        int setId = 1);
}
