// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.DTOs.Data;
using Pidgeon.Core.Application.Interfaces.Data;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pidgeon.Core.Infrastructure.Data;

/// <summary>
/// Walks the NPM-style dependency map captured in a FHIR IG package's
/// manifest.json (see <see cref="PackageManifest.Dependencies"/>) and installs
/// each missing dependency from the FHIR package registry into a sibling
/// <c>fhir-dep-{name}-{version}</c> directory. The versioned directory name
/// lets two versions of the same package coexist (PAS pulls two US Core
/// versions); precedence between colliding canonical URLs is decided at load
/// time by <c>FhirIGService</c> (primary IGs always win; dependencies never
/// overwrite).
/// </summary>
public sealed class FhirIgDependencyResolver : IFhirIgDependencyResolver
{
    /// <summary>
    /// Packages deliberately never auto-fetched. The base FHIR spec ships as
    /// embedded subset stubs (the full package is roughly one hundred megabytes and the loader
    /// intentionally stays offline-lean), and us.nlm.vsac is UMLS-licensed
    /// content that must go through the member-supplied
    /// <c>--accept-license</c> flow, never a silent transitive download
    /// (mirrors <see cref="DataPackageRegistry.LicensesRequiringAcceptance"/>).
    /// </summary>
    private static readonly HashSet<string> SkippedPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "hl7.fhir.r4.core",
        "hl7.fhir.r4b.core",
        "hl7.fhir.r5.core",
        "hl7.fhir.core",
        "hl7.fhir.r4.examples",
        "us.nlm.vsac",
    };

    // Exact published versions only — never "current"/"dev" builds or npm-style
    // ranges, which have no stable registry tarball.
    private static readonly Regex ExactVersionPattern = new(
        @"^[0-9A-Za-z][0-9A-Za-z.\-]*$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IFhirPackageFetcher _fetcher;
    private readonly ILogger<FhirIgDependencyResolver> _logger;

    public FhirIgDependencyResolver(IFhirPackageFetcher fetcher, ILogger<FhirIgDependencyResolver> logger)
    {
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<DependencyResolutionSummary>> InstallMissingDependenciesAsync(
        string packageInstallDir, bool acceptLicense = false, CancellationToken cancellationToken = default)
    {
        var rootManifest = ReadManifest(packageInstallDir);
        if (rootManifest is null)
        {
            return Result<DependencyResolutionSummary>.Failure(Error.Create(
                "DEP_RESOLVE_NO_MANIFEST",
                $"No readable manifest.json in '{packageInstallDir}' — cannot resolve dependencies."));
        }

        var installRoot = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(packageInstallDir))!;
        int installed = 0, skipped = 0;
        var failed = new List<string>();
        var licenseGated = new List<string>();

        foreach (var (name, version) in WalkDependencyLevels(rootManifest, installRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = $"{name}#{version}";

            if (SkippedPackages.Contains(name))
            {
                _logger.LogDebug("Skipping dependency {Dependency}: on the deliberate no-fetch list", key);
                skipped++;
                continue;
            }
            if (!IsExactVersion(version))
            {
                _logger.LogWarning(
                    "Skipping dependency {Dependency}: '{Version}' is not an exact published version", key, version);
                skipped++;
                continue;
            }

            var depDir = DepDir(installRoot, name, version);
            if (IsInstalled(depDir))
            {
                skipped++;
                continue;
            }

            var request = new FhirPackageFetchRequest(
                PackageName: DepDirName(name, version),
                DownloadUrl: $"https://packages.fhir.org/{name}/{version}",
                DataType: "fhir-ig",
                Version: version,
                Description: $"Transitive dependency {key}",
                Source: "packages.fhir.org",
                License: "");

            var fetch = await _fetcher.FetchAsync(request, depDir, cancellationToken);
            if (!fetch.IsSuccess)
            {
                failed.Add(key);
                _logger.LogWarning(
                    "Failed to install IG dependency {Dependency}: {Error} — binding validation against its "
                    + "ValueSets degrades to 'binding not validated'", key, fetch.Error.Message);
                continue;
            }

            // A transitively-pulled dependency must clear the same
            // license-acceptance gate a direct install does (see
            // DataPackageManager.InstallSinglePackageAsync). A dependency's license
            // is only knowable from its own manifest, written by the fetch above, so
            // the gate runs here: a dependency that self-declares a license requiring
            // acceptance is rolled back and reported rather than left installed
            // unprompted. The hard-licensed FHIR dep (us.nlm.vsac) never reaches this
            // point — it is on the no-fetch SkippedPackages list.
            var declaredLicense = ReadManifest(depDir)?.License ?? string.Empty;
            if (!acceptLicense
                && DataPackageRegistry.LicensesRequiringAcceptance.TryGetValue(declaredLicense, out var licenseName))
            {
                RemoveDir(depDir);
                licenseGated.Add(key);
                _logger.LogWarning(
                    "IG dependency {Dependency} requires {License} acceptance and was not installed. "
                    + "Install it explicitly with --accept-license.", key, licenseName);
                continue;
            }

            installed++;
            _logger.LogInformation("Installed IG dependency {Dependency} ({Count} resources)", key, fetch.Value);
        }

        return Result<DependencyResolutionSummary>.Success(
            new DependencyResolutionSummary(installed, skipped, failed, licenseGated));
    }

    /// <summary>
    /// Rolls back a dependency directory whose fetched content is license-gated.
    /// I/O failure (a locked file, a race) is logged, never thrown — the caller
    /// still reports the dependency as gated and not installed.
    /// </summary>
    private void RemoveDir(string depDir)
    {
        try
        {
            if (Directory.Exists(depDir))
                Directory.Delete(depDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove license-gated dependency directory {Dir}", depDir);
        }
    }

    public IReadOnlyList<string> ResolveInstalledDependencyDirs(string packageInstallDir)
    {
        var rootManifest = ReadManifest(packageInstallDir);
        if (rootManifest is null)
            return Array.Empty<string>();

        var installRoot = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(packageInstallDir))!;
        var dirs = new List<string>();

        foreach (var (name, version) in WalkDependencyLevels(rootManifest, installRoot))
        {
            if (SkippedPackages.Contains(name))
                continue;

            var depDir = DepDir(installRoot, name, version);
            if (IsInstalled(depDir))
            {
                dirs.Add(depDir);
            }
            else
            {
                _logger.LogDebug(
                    "IG dependency {Name}#{Version} is not installed; reinstall the parent package to fetch it",
                    name, version);
            }
        }

        return dirs;
    }

    /// <summary>
    /// Breadth-first walk of the dependency tree, yielding each name/version
    /// pair exactly once in deterministic order (per-level ordinal sort).
    /// Children are discovered from the manifests of dependency dirs already
    /// on disk, so the walk is transitive across previously installed levels.
    /// </summary>
    private IEnumerable<(string Name, string Version)> WalkDependencyLevels(
        PackageManifest rootManifest, string installRoot)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var level = DependenciesOf(rootManifest);

        while (level.Count > 0)
        {
            var next = new List<(string Name, string Version)>();
            foreach (var (name, version) in level
                .Where(d => visited.Add($"{d.Name}#{d.Version}"))
                .OrderBy(d => $"{d.Name}#{d.Version}", StringComparer.Ordinal))
            {
                yield return (name, version);

                var depManifest = ReadManifest(DepDir(installRoot, name, version));
                if (depManifest is not null)
                    next.AddRange(DependenciesOf(depManifest));
            }
            level = next;
        }
    }

    private static List<(string Name, string Version)> DependenciesOf(PackageManifest manifest)
        => manifest.Dependencies is null
            ? new List<(string, string)>()
            : manifest.Dependencies.Select(kvp => (kvp.Key, kvp.Value)).ToList();

    private PackageManifest? ReadManifest(string packageDir)
    {
        var manifestPath = Path.Combine(packageDir, "manifest.json");
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(manifestPath), ManifestJsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Unreadable manifest at {Path}; treating as no dependencies", manifestPath);
            return null;
        }
    }

    private static bool IsExactVersion(string version)
        => ExactVersionPattern.IsMatch(version)
           && !string.Equals(version, "current", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(version, "dev", StringComparison.OrdinalIgnoreCase);

    private static string DepDir(string installRoot, string name, string version)
        => Path.Combine(installRoot, DepDirName(name, version));

    private static bool IsInstalled(string depDir)
        => File.Exists(Path.Combine(depDir, "manifest.json"));

    internal static string DepDirName(string name, string version)
        => $"fhir-dep-{name}-{version}".ToLowerInvariant();
}
