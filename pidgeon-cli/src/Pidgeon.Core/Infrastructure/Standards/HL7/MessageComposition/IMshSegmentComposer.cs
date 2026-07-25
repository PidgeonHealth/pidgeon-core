// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Common;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Composes the MSH header segment. MSH numbering is special (field 1 is the field separator
/// and field 2 the encoding characters), and MSH-1..12 carry version-stable HL7 semantics
/// resolved here from <see cref="MshHeaderDefaults"/> plus the per-message clock and RNG.
/// Fields beyond MSH-12 defer to the caller's generic schema-driven field engine.
/// </summary>
public interface IMshSegmentComposer
{
    /// <summary>
    /// Builds the MSH segment string for the given schema. Whole-field MSH pins override the
    /// defaults; MSH-7 reads <c>context.Clock</c> (and records it under "MSH.7"), MSH-10 draws the
    /// message control ID from the MSH-10 coordinate off <c>context.Key</c>. Any field past MSH-12
    /// is produced by <paramref name="generateTailField"/>, which runs the composer's standard
    /// field path (passed as a delegate rather than an injected service so this composer does not
    /// depend back on the message composer, which depends on it, avoiding a circular registration).
    /// </summary>
    Task<Result<string>> ComposeAsync(
        SegmentSchema segmentSchema,
        SegmentGenerationContext context,
        GenerationOptions options,
        Func<SegmentField, Task<string>> generateTailField);
}
