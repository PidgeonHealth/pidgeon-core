// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Capability;

/// <summary>
/// Describes a capability installed by the current composition and its provider identity.
/// </summary>
public sealed record InstalledCapabilityDescriptor(
    string CapabilityId,
    string CapabilityVersion,
    string ProviderId,
    string ProviderVersion,
    string? PackageId = null,
    string? PackageVersion = null);
