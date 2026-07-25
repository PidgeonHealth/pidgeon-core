// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Data;

namespace Pidgeon.Core.Application.Interfaces.Data;

/// <summary>
/// Reports the immutable data packages installed in the current composition.
/// </summary>
public interface IDataPackageCatalog
{
    IReadOnlyList<DataPackageCatalogEntry> ListInstalledPackages();

    /// <summary>
    /// The catalog entry for one installed package, or null when the package is
    /// not installed. Lookup is case-insensitive on the package id.
    /// </summary>
    DataPackageCatalogEntry? FindInstalled(string packageId);

    /// <summary>
    /// The absolute on-disk content root of an installed package, or null when
    /// the package is not installed. Installed IG packages are genuinely
    /// directory-shaped artifacts (StructureDefinition JSON trees consumed via
    /// directory walks), so the catalog exposes the root rather than forcing a
    /// stream-per-file protocol onto directory-oriented consumers.
    /// </summary>
    string? GetInstalledContentRoot(string packageId);
}
