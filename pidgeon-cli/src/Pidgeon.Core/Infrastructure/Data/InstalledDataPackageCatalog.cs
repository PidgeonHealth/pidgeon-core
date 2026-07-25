// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.DTOs.Data;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Domain.Data;
using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Data;

/// <summary>
/// Filesystem <see cref="IDataPackageCatalog"/> over the standard package install
/// root: <c>PIDGEON_DATA_DIR</c> when set (the test-isolation override shared with
/// the package installer), otherwise <c>~/.pidgeon/data</c>. A package is installed
/// when its directory carries a readable <c>manifest.json</c>; a bare directory is
/// not an installed package. Reads only — installation is a separate concern that
/// never enters this type.
/// </summary>
public sealed class InstalledDataPackageCatalog : IDataPackageCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<InstalledDataPackageCatalog> _logger;
    private readonly string _installDirectory;

    public InstalledDataPackageCatalog(
        ILogger<InstalledDataPackageCatalog> logger,
        string? installDirectory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _installDirectory = installDirectory ?? ResolveDefaultInstallDirectory();
    }

    public IReadOnlyList<DataPackageCatalogEntry> ListInstalledPackages()
    {
        if (!Directory.Exists(_installDirectory))
            return Array.Empty<DataPackageCatalogEntry>();

        var entries = new List<DataPackageCatalogEntry>();
        foreach (var directory in Directory.EnumerateDirectories(_installDirectory))
        {
            var entry = ReadEntry(directory);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries
            .OrderBy(entry => entry.Identity.Id, StringComparer.Ordinal)
            .ToList();
    }

    public DataPackageCatalogEntry? FindInstalled(string packageId)
    {
        var root = GetInstalledContentRoot(packageId);
        return root is null ? null : ReadEntry(root);
    }

    public string? GetInstalledContentRoot(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return null;

        var directory = Path.Combine(_installDirectory, packageId.ToLowerInvariant());
        return File.Exists(Path.Combine(directory, "manifest.json")) ? directory : null;
    }

    /// <summary>
    /// Synthesizes a catalog entry from an installed package's manifest. The v1
    /// install layout carries no payload digest, so <c>Sha256</c> is empty and
    /// dependency digests cannot be synthesized (the manifest's NPM-style
    /// dependency map has versions only) — both stay honest-empty rather than
    /// fabricated. A malformed or unreadable manifest makes the package invisible
    /// (logged), never a crash: a catalog that throws on one bad directory would
    /// take every healthy package down with it.
    /// </summary>
    private DataPackageCatalogEntry? ReadEntry(string packageDirectory)
    {
        var manifestPath = Path.Combine(packageDirectory, "manifest.json");
        PackageManifest manifest;
        try
        {
            using var stream = File.OpenRead(manifestPath);
            var parsed = JsonSerializer.Deserialize<PackageManifest>(stream, JsonOptions);
            if (parsed is null)
                return null;
            manifest = parsed;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                "Skipping installed package at {Directory}: manifest.json unreadable ({Reason})",
                packageDirectory, ex.Message);
            return null;
        }

        return new DataPackageCatalogEntry(
            identity: new DataPackageIdentity(Path.GetFileName(packageDirectory), manifest.Version),
            sha256: string.Empty,
            dependencies: Array.Empty<DataPackageDependency>(),
            redistributionClass: ClassifyLicense(manifest.License),
            coreCompatibility: new CoreCompatibilityRange("0.0.0", null),
            provenance: new DataPackageProvenance(manifest.Source, null, Array.Empty<string>()),
            suppliedCapabilities: Array.Empty<string>());
    }

    /// <summary>
    /// Conservative license classification: only unambiguous public-domain grants
    /// map to <see cref="DataRedistributionClass.PublicRedistributable"/>; every
    /// other license string classifies as bring-your-own-license, never the more
    /// permissive class by default.
    /// </summary>
    private static DataRedistributionClass ClassifyLicense(string license)
    {
        if (license.Contains("CC0", StringComparison.OrdinalIgnoreCase)
            || license.Contains("public domain", StringComparison.OrdinalIgnoreCase))
        {
            return DataRedistributionClass.PublicRedistributable;
        }

        return DataRedistributionClass.BringYourOwnLicense;
    }

    private static string ResolveDefaultInstallDirectory()
    {
        var envPath = Environment.GetEnvironmentVariable("PIDGEON_DATA_DIR");
        if (!string.IsNullOrEmpty(envPath))
            return envPath;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".pidgeon", "data");
    }
}
