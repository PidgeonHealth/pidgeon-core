// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Capability;

namespace Pidgeon.Core.Application.Interfaces.Capability;

/// <summary>
/// Reports capabilities actually installed by the current composition.
/// </summary>
public interface ICapabilityCatalog
{
    IReadOnlyList<InstalledCapabilityDescriptor> ListInstalledCapabilities();
}
