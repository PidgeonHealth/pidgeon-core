// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.ObjectModel;

namespace Pidgeon.Core.Domain.Data;

/// <summary>
/// Declares the redistribution policy applied to an installed package payload.
/// </summary>
public enum DataRedistributionClass
{
    PublicRedistributable,
    BringYourOwnLicense,
    PidgeonCommercial,
    TenantPrivate,
}

/// <summary>
/// Identifies an exact dependency and the expected SHA-256 digest of its payload.
/// </summary>
public sealed record DataPackageDependency(
    DataPackageIdentity Identity,
    string Sha256);

/// <summary>
/// Declares the Core API range with which a package is compatible.
/// </summary>
public sealed record CoreCompatibilityRange(
    string MinimumInclusive,
    string? MaximumExclusive);

/// <summary>
/// Records where a package came from and which deterministic transformations produced it.
/// </summary>
public sealed record DataPackageProvenance
{
    public DataPackageProvenance(
        string source,
        string? sourceRevision,
        IEnumerable<string> transformations)
    {
        Source = source;
        SourceRevision = sourceRevision;
        Transformations = Array.AsReadOnly(transformations.ToArray());
    }

    public string Source { get; }

    public string? SourceRevision { get; }

    public ReadOnlyCollection<string> Transformations { get; }
}

/// <summary>
/// Immutable catalog entry for one installed package version.
/// </summary>
public sealed record DataPackageCatalogEntry
{
    public DataPackageCatalogEntry(
        DataPackageIdentity identity,
        string sha256,
        IEnumerable<DataPackageDependency> dependencies,
        DataRedistributionClass redistributionClass,
        CoreCompatibilityRange coreCompatibility,
        DataPackageProvenance provenance,
        IEnumerable<string> suppliedCapabilities)
    {
        Identity = identity;
        Sha256 = sha256;
        Dependencies = Array.AsReadOnly(dependencies.ToArray());
        RedistributionClass = redistributionClass;
        CoreCompatibility = coreCompatibility;
        Provenance = provenance;
        SuppliedCapabilities = Array.AsReadOnly(suppliedCapabilities.ToArray());
    }

    public DataPackageIdentity Identity { get; }

    public string Sha256 { get; }

    public ReadOnlyCollection<DataPackageDependency> Dependencies { get; }

    public DataRedistributionClass RedistributionClass { get; }

    public CoreCompatibilityRange CoreCompatibility { get; }

    public DataPackageProvenance Provenance { get; }

    public ReadOnlyCollection<string> SuppliedCapabilities { get; }
}
