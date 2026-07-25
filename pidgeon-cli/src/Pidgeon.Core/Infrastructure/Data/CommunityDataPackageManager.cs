// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.DTOs.Data;
using Pidgeon.Core.Application.Interfaces.Data;
using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Data;

/// <summary>
/// Public data-package manager over local, member-supplied, or registry-declared CC0 package
/// directories. It shares the package and license catalog with the monorepo manager but has no
/// legacy assembly reflection or embedded private payload path. Network access occurs only for an
/// explicit install of a catalog entry whose exact download URL is declared in the registry.
/// </summary>
internal sealed class CommunityDataPackageManager : IDataPackageManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ILogger<CommunityDataPackageManager> _logger;
    private readonly IFhirPackageFetcher? _fetcher;
    private readonly IFhirIgDependencyResolver? _dependencyResolver;
    private readonly string _sourceDirectory;
    private readonly string _installDirectory;

    public CommunityDataPackageManager(
        ILogger<CommunityDataPackageManager> logger,
        string? sourceDirectory = null,
        string? installDirectory = null,
        IFhirPackageFetcher? fetcher = null,
        IFhirIgDependencyResolver? dependencyResolver = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fetcher = fetcher;
        _dependencyResolver = dependencyResolver;
        _sourceDirectory = sourceDirectory ?? ResolveDefaultSourceDirectory();
        _installDirectory = installDirectory ?? ResolveDefaultInstallDirectory();
    }

    public async Task<IReadOnlyList<PackageInfo>> ListPackagesAsync()
    {
        var packages = new List<PackageInfo>();
        foreach (var (name, entry) in DataPackageRegistry.Packages)
        {
            var manifestPath = Path.Combine(_installDirectory, name, "manifest.json");
            var manifest = File.Exists(manifestPath) ? await LoadManifestAsync(manifestPath) : null;
            packages.Add(new PackageInfo
            {
                Name = name,
                Description = entry.Description,
                DataType = entry.DataType,
                IsInstalled = manifest is not null,
                Manifest = manifest,
                License = entry.License,
                Version = entry.Version,
                RequiresLicense = DataPackageRegistry.LicensesRequiringAcceptance.ContainsKey(entry.License)
            });
        }
        return packages;
    }

    public IReadOnlyList<BundleInfo> ListBundles() => DataPackageRegistry.Bundles
        .Select(pair => new BundleInfo
        {
            Name = pair.Key,
            DisplayName = pair.Value.DisplayName,
            Description = pair.Value.Description,
            License = pair.Value.License,
            Packages = pair.Value.Packages
        }).ToList();

    public string[]? GetBundlePackages(string name)
        => DataPackageRegistry.Bundles.TryGetValue(name, out var bundle) ? bundle.Packages : null;

    public string? GetInstallPath(string packageName)
        => DataPackageRegistry.Packages.ContainsKey(packageName)
            ? Path.Combine(_installDirectory, packageName.ToLowerInvariant())
            : null;

    public async Task<Result<PackageManifest>> InstallPackageAsync(string packageName, bool acceptLicense = false)
    {
        var normalized = packageName.ToLowerInvariant();
        if (DataPackageRegistry.Bundles.TryGetValue(normalized, out var bundle))
        {
            PackageManifest? last = null;
            var recordCount = 0;
            foreach (var member in bundle.Packages)
            {
                var installed = await InstallOneAsync(member, acceptLicense);
                if (installed.IsFailure)
                    return Result<PackageManifest>.Failure(installed.Error);
                last = installed.Value;
                recordCount += installed.Value.RecordCount;
            }
            return Result<PackageManifest>.Success(new PackageManifest
            {
                Name = normalized,
                Version = "bundle",
                Description = bundle.Description,
                Source = last?.Source ?? "member-supplied",
                License = bundle.License,
                RecordCount = recordCount,
                DataType = "bundle",
                Schema = "1.0",
                GeneratedAt = DateTimeOffset.UtcNow
            });
        }
        return await InstallOneAsync(normalized, acceptLicense);
    }

    private async Task<Result<PackageManifest>> InstallOneAsync(string name, bool acceptLicense)
    {
        if (!DataPackageRegistry.Packages.TryGetValue(name, out var entry))
            return Result<PackageManifest>.Failure(Error.Validation($"Unknown package: '{name}'."));
        if (!acceptLicense && DataPackageRegistry.LicensesRequiringAcceptance.TryGetValue(entry.License, out var license))
            return Result<PackageManifest>.Failure(Error.Validation(
                $"Package '{name}' requires {license} acceptance. Use --accept-license flag to accept."));

        var source = Path.Combine(_sourceDirectory, name);
        var sourceManifest = Path.Combine(source, "manifest.json");
        if (!File.Exists(sourceManifest)
            && _fetcher is not null
            && !string.IsNullOrWhiteSpace(entry.DownloadUrl))
        {
            var fetched = await _fetcher.FetchAsync(new FhirPackageFetchRequest(
                PackageName: name,
                DownloadUrl: entry.DownloadUrl,
                DataType: entry.DataType,
                Version: entry.Version,
                Description: entry.Description,
                Source: entry.Source,
                License: entry.License), source);
            if (fetched.IsFailure)
                return Result<PackageManifest>.Failure(fetched.Error);
        }

        if (!File.Exists(sourceManifest))
            return Result<PackageManifest>.Failure(Error.Create(
                "PACKAGE_NOT_FOUND",
                $"Package source not found at '{source}'. Supply an extracted package locally before installing.",
                name));

        var manifest = await LoadManifestAsync(sourceManifest);
        if (manifest is null)
            return Result<PackageManifest>.Failure(Error.Parsing(
                $"Failed to read manifest for package '{name}'.", sourceManifest));

        var destination = Path.Combine(_installDirectory, name);
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);

        // FHIR IG validation needs the ValueSets and StructureDefinitions from the
        // package's exact dependency tree. Installation is the explicit network
        // operation; validation remains offline-pure. Dependency failures do not
        // erase a successful primary install, but the resolver reports and logs
        // them so validation can degrade to NOT_EVALUATED rather than false-pass.
        if (_dependencyResolver is not null
            && entry.DataType.Equals("fhir-ig", StringComparison.OrdinalIgnoreCase))
        {
            var dependencies = await _dependencyResolver.InstallMissingDependenciesAsync(
                destination,
                acceptLicense);
            if (dependencies.IsFailure)
            {
                _logger.LogWarning(
                    "Dependency resolution for {PackageName} did not run: {Error}",
                    name,
                    dependencies.Error.Message);
            }
            else
            {
                _logger.LogInformation(
                    "Dependency resolution for {PackageName}: {Installed} installed, {Skipped} skipped, {Failed} failed, {LicenseGated} license-gated",
                    name,
                    dependencies.Value.Installed,
                    dependencies.Value.Skipped,
                    dependencies.Value.Failed.Count,
                    dependencies.Value.LicenseGated.Count);
            }
        }

        _logger.LogInformation("Installed package {PackageName} to {InstallPath}", name, destination);
        return Result<PackageManifest>.Success(manifest);
    }

    public async Task<Result<bool>> RemovePackageAsync(string packageName)
    {
        await Task.Yield();
        var normalized = packageName.ToLowerInvariant();
        if (!DataPackageRegistry.Packages.ContainsKey(normalized))
            return Result<bool>.Failure(Error.Validation($"Unknown package: '{packageName}'."));
        var directory = Path.Combine(_installDirectory, normalized);
        if (!Directory.Exists(directory))
            return Result<bool>.Failure(Error.Create("NOT_INSTALLED", $"Package '{normalized}' is not installed."));
        Directory.Delete(directory, recursive: true);
        return Result<bool>.Success(true);
    }

    public async Task<IReadOnlyList<T>?> LoadPackageDataAsync<T>(string dataType) where T : class
    {
        var records = new List<T>();
        var found = false;
        foreach (var name in DataPackageRegistry.Packages
                     .Where(pair => pair.Value.DataType.Equals(dataType, StringComparison.OrdinalIgnoreCase))
                     .Select(pair => pair.Key))
        {
            var path = Path.Combine(_installDirectory, name, "data.json");
            if (!File.Exists(path)) continue;
            found = true;
            await using var stream = File.OpenRead(path);
            records.AddRange(await JsonSerializer.DeserializeAsync<List<T>>(stream, JsonOptions) ?? []);
        }
        return found ? records : null;
    }

    public async Task<IReadOnlyList<DataSourceStatus>> GetDataSourceStatusAsync()
    {
        var packages = await ListPackagesAsync();
        return packages.GroupBy(package => package.DataType, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DataSourceStatus
            {
                DataType = group.Key,
                Source = group.Any(package => package.IsInstalled) ? "package" : "unavailable",
                RecordCount = group.Sum(package => package.Manifest?.RecordCount ?? 0)
            }).ToList();
    }

    private static async Task<PackageManifest?> LoadManifestAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PackageManifest>(stream, JsonOptions);
    }

    private static string ResolveDefaultSourceDirectory()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "pidgeon-cli")))
                return Path.Combine(directory.FullName, ".data", "packages");
            directory = directory.Parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), ".data", "packages");
    }

    private static string ResolveDefaultInstallDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("PIDGEON_DATA_DIR");
        return !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pidgeon", "data");
    }
}
