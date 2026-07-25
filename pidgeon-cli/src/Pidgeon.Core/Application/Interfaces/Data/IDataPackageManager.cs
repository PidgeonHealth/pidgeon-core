// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.DTOs.Data;

namespace Pidgeon.Core.Application.Interfaces.Data;

/// <summary>
/// Manages data packages: install, list, remove, and load clinical terminology datasets.
/// Packages are stored in ~/.pidgeon/data/ (user install) or .data/packages/ (dev mode).
/// </summary>
public interface IDataPackageManager
{
    /// <summary>
    /// Lists all available packages (both installed and uninstalled).
    /// </summary>
    Task<IReadOnlyList<PackageInfo>> ListPackagesAsync();

    /// <summary>
    /// Installs a package from the source directory to the user install directory.
    /// </summary>
    /// <param name="packageName">Package name to install</param>
    /// <param name="acceptLicense">Whether the user has accepted required license terms (e.g., UMLS)</param>
    Task<Result<PackageManifest>> InstallPackageAsync(string packageName, bool acceptLicense = false);

    /// <summary>
    /// Removes an installed package.
    /// </summary>
    Task<Result<bool>> RemovePackageAsync(string packageName);

    /// <summary>
    /// Loads all records from an installed package matching the specified data type.
    /// Returns null if no package is installed for this data type.
    /// </summary>
    Task<IReadOnlyList<T>?> LoadPackageDataAsync<T>(string dataType) where T : class;

    /// <summary>
    /// Gets the status of active data sources and their record counts.
    /// </summary>
    Task<IReadOnlyList<DataSourceStatus>> GetDataSourceStatusAsync();

    /// <summary>
    /// Returns the constituent package names if the given name is a bundle, null otherwise.
    /// </summary>
    string[]? GetBundlePackages(string name);

    /// <summary>
    /// Lists all available bundles with their metadata.
    /// </summary>
    IReadOnlyList<BundleInfo> ListBundles();

    /// <summary>
    /// Returns the absolute install directory for a package, or null when the
    /// package is unknown. The directory is not guaranteed to exist on disk
    /// (callers should check <see cref="Directory.Exists"/> to distinguish
    /// "known but uninstalled" from "installed"). Used by consumers that need
    /// to enumerate files inside a package directly (e.g. FHIR profile
    /// resolution) rather than load a flat record list via
    /// <see cref="LoadPackageDataAsync{T}"/>.
    /// </summary>
    string? GetInstallPath(string packageName);
}

/// <summary>
/// Information about a known package (installed or available).
/// </summary>
public record PackageInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string DataType { get; init; }
    public required bool IsInstalled { get; init; }
    public PackageManifest? Manifest { get; init; }

    // Catalog metadata sourced from the registry entry, so it is present even when the
    // package is uninstalled (the installed Manifest is null until install). Lets a catalog
    // surface (Post Packages panel / MCP) render license + version + the "License Needed"
    // state without reaching into Core's internal DataPackageRegistry. License,
    // redistribution, and provenance dispositions are cleared per package before admission.
    public string License { get; init; } = "";
    public string Version { get; init; } = "";

    /// <summary>
    /// True when the package's data is member-supplied behind the <c>--accept-license</c>
    /// gate (UMLS / AMA / NCPDP / X12 / commercial KB). Computed from the registry's
    /// license-acceptance policy.
    /// </summary>
    public bool RequiresLicense { get; init; }
}

/// <summary>
/// Status of an active data source showing where data is loaded from.
/// </summary>
public record DataSourceStatus
{
    public required string DataType { get; init; }
    public required string Source { get; init; }
    public required int RecordCount { get; init; }
}

/// <summary>
/// Metadata for a named package bundle (a group of packages installable with one command).
/// </summary>
public record BundleInfo
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required string License { get; init; }
    public required string[] Packages { get; init; }
}
