// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Post-assembly vendor-dialect pass — the single data-driven seam that shapes a fully assembled
/// HL7 message to a vendor's conventions (engine-vendor-dialect). Runs once over the assembled
/// message, like <see cref="ITemporalCoherencePass"/>, reading the active
/// <c>VendorInterfaceProfile</c> carried on the context: MSH whole-field overrides (sending
/// application, processing id, …) and segment ordering today; vendor Z-segment emission lands on this
/// same seam (S2). All vendor behavior comes from the profile data — there are no per-vendor branches.
/// </summary>
public interface IVendorDialectPass
{
    /// <summary>
    /// Returns the message shaped to the context's active vendor profile, or unchanged when no vendor
    /// profile is active — so default (non-vendor) generation is byte-for-byte identical.
    /// </summary>
    string Apply(string assembledMessage, SegmentGenerationContext context);
}
