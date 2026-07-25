// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Interfaces.VendorIntelligence;

/// <summary>
/// Resolves a vendor selection carried on <see cref="GenerationOptions"/> (the friendly
/// <c>VendorProfile</c> enum, or an already-resolved profile) into a concrete
/// <c>ActiveVendorProfile</c> the HL7 composer's vendor-dialect pass shapes output from.
/// Standard-agnostic seam: a run with no vendor selected is returned unchanged, so default
/// (non-vendor) generation is byte-for-byte identical.
/// </summary>
public interface IVendorProfileResolver
{
    /// <summary>
    /// Returns the options with the selected vendor profile resolved onto
    /// <c>ActiveVendorProfile</c> (and the profile's MSH/version conventions applied). Returns the
    /// input unchanged when no vendor is selected, the vendor has no shipped profile, or a profile is
    /// already resolved (e.g. a manifest replay).
    /// </summary>
    GenerationOptions? Resolve(GenerationOptions? options);
}
