// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Applies HL7-positional "pins" — caller-supplied exact values for a segment / field /
/// component path (e.g. <c>PID.5.1 = "SMITH"</c>) — over generated output. A pin forces
/// its value in place of the generator's choice and forces an otherwise-optional segment
/// to be present. Pure positional overlay: holds no generation state.
/// </summary>
public interface IHL7FieldPinner
{
    /// <summary>
    /// True when <paramref name="options"/> contains at least one pin targeting the given
    /// segment. Used to force-include otherwise-optional segments.
    /// </summary>
    bool HasAnyPinForSegment(string segmentCode, GenerationOptions options);

    /// <summary>
    /// Pin overrides for a single (segment, field), keyed by component position. Key
    /// <c>0</c> is a whole-field pin (e.g. <c>PID.19 = "999"</c>); keys 1+ are per-component
    /// (e.g. <c>PID.5.1 = "SMITH"</c>). Empty when nothing targets the field.
    /// </summary>
    Dictionary<int, string> GetPinsForField(string segmentCode, int fieldPosition, GenerationOptions options);

    /// <summary>
    /// Post-pass overlay on a fully-rendered segment line (e.g. the output of a dedicated
    /// segment builder): applies whole-field and component-level pins, no-op when none
    /// target the segment. Does not handle MSH — MSH pins live in the composer's MSH path.
    /// </summary>
    string ApplySegmentLevelPins(string segmentLine, string segmentCode, GenerationOptions options);

    /// <summary>
    /// Overlays component-level pins onto a generated composite (<c>^</c>-joined) field
    /// value, extending the component slots if a pin reaches past the generated count.
    /// </summary>
    string ApplyComponentPins(string fieldValue, Dictionary<int, string> fieldPins);
}
