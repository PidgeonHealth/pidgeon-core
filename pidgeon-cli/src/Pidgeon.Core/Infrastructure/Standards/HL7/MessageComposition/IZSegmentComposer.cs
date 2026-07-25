// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Pidgeon.Core.Domain.VendorIntelligence;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Composes the vendor-specific Z-segment lines for a message from a vendor interface profile.
/// Every Z-segment defined under the profile's <c>segments</c> (a key starting with 'Z') is emitted
/// with its fields populated from <see cref="VendorFieldSpec"/> — constants, clinical-context bindings
/// (<see cref="VendorFieldSpec.Source"/>), or deterministic fakers keyed off the message coordinate.
/// Pure and deterministic; all vendor behavior comes from the profile data (no per-vendor branches).
/// </summary>
public interface IZSegmentComposer
{
    /// <summary>
    /// Returns the Z-segment lines for the profile, in a stable (segment-code-sorted) order, ready to
    /// append to the assembled message. Empty when the profile defines no Z-segments.
    /// </summary>
    IReadOnlyList<string> Compose(VendorInterfaceProfile profile, SegmentGenerationContext context);
}
