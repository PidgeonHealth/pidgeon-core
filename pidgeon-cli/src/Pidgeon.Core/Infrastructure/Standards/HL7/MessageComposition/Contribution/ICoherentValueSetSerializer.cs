// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// Renders a contributor's <see cref="CoherentValueSet"/> to a segment wire string against the
/// version's segment schema: the serializer owns shape. It is the single place that decides
/// version-correct shape for contributed segments: CE/CWE component width (via
/// <see cref="CodedElementRenderer"/> and the field's data type), withdrawn-field suppression
/// (optionality "W" becomes empty), and the segment's field count (the version schema, not a
/// hardcoded array). Pins are applied by the caller as a post-pass.
/// </summary>
public interface ICoherentValueSetSerializer
{
    /// <summary>
    /// Serializes <paramref name="valueSet"/> against <paramref name="schema"/> at the context's
    /// version. Each <see cref="AtomicGroup"/> gets one present/absent decision (present if any
    /// member field is schema-required, pinned, or part of a cohort sequence; otherwise a single
    /// optional-inclusion draw — never one draw per field). A present group's values are emitted in
    /// schema field order; every other schema field is empty.
    /// </summary>
    Task<string> SerializeAsync(
        CoherentValueSet valueSet,
        SegmentSchema schema,
        SegmentGenerationContext context,
        GenerationOptions options);
}
