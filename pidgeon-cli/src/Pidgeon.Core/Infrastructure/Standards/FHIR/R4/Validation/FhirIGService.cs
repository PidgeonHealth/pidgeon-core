// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Domain.Conformance;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Default <see cref="IFhirIGService"/> backed by <see cref="IDataPackageCatalog"/>
/// for on-disk resolution and <see cref="IStructureDefinitionLoader"/> for
/// profile parsing + indexing.
///
/// <para>
/// Design note: the known-IG map is intentionally hard-coded rather than
/// discovered from the package registry. IG key → package name → canonical
/// URL base is a small, stable mapping that benefits from being reviewed in
/// code (the canonical URL base is normative to the IG spec, not data-driven).
/// Adding an IG is a two-line change here plus a package registry entry.
/// </para>
/// </summary>
public partial class FhirIGService : IFhirIGService
{
    private readonly IDataPackageCatalog _packages;
    private readonly IStructureDefinitionLoader _loader;
    private readonly ILogger<FhirIGService> _logger;
    private readonly IFhirIgDependencyResolver? _depResolver;

    private record KnownIG(string PackageName, string CanonicalBase, string DisplayName);

    private static readonly Dictionary<string, KnownIG> _knownIGs = new(StringComparer.OrdinalIgnoreCase)
    {
        // us-core-6.0 (the ecosystem target) is listed before us-core-3.1.1 (the
        // CMS-0057-F required baseline) deliberately: the two releases share one
        // canonical base, and listing order decides both the not-installed hint
        // (first owner) and the bare-canonical winner when BOTH are installed
        // (EnsureAllInstalledLoadedAsync loads in listing order, last write wins,
        // so the explicitly-installed required baseline overrides the default).
        // Version stamps follow the loader's indexed content, so evidence always
        // names the release that actually backed validation. Exact per-run
        // isolation (two contexts, no bleed) is the CP1 artifact-version-isolation
        // slice over ArtifactContext; until it lands, |version-pinned canonicals
        // resolve exactly and the bare canonical is deterministic last-write-wins.
        ["us-core-6.0"] = new(
            PackageName: "fhir-us-core-6.0",
            CanonicalBase: "http://hl7.org/fhir/us/core/StructureDefinition/",
            DisplayName: "US Core"),
        ["us-core-3.1.1"] = new(
            PackageName: "fhir-us-core-3.1.1",
            CanonicalBase: "http://hl7.org/fhir/us/core/StructureDefinition/",
            DisplayName: "US Core"),
        ["davinci-pas-2.1"] = new(
            PackageName: "fhir-davinci-pas-2.1",
            CanonicalBase: "http://hl7.org/fhir/us/davinci-pas/StructureDefinition/",
            DisplayName: "Da Vinci PAS"),
        ["davinci-crd-2.1"] = new(
            PackageName: "fhir-davinci-crd-2.1",
            CanonicalBase: "http://hl7.org/fhir/us/davinci-crd/StructureDefinition/",
            DisplayName: "Da Vinci CRD"),
        ["davinci-dtr-2.0"] = new(
            PackageName: "fhir-davinci-dtr-2.0",
            CanonicalBase: "http://hl7.org/fhir/us/davinci-dtr/StructureDefinition/",
            DisplayName: "Da Vinci DTR"),
    };

    private readonly HashSet<string> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loadedDepDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public FhirIGService(
        IDataPackageCatalog packages,
        IStructureDefinitionLoader loader,
        ILogger<FhirIGService> logger,
        IFhirIgDependencyResolver? depResolver = null)
    {
        _packages = packages;
        _loader = loader;
        _logger = logger;
        _depResolver = depResolver;
    }

    public IReadOnlyList<string> KnownIGKeys => _knownIGs.Keys.ToList();

    // ResolveStampsForProfiles + its helper live in FhirIGService.Stamps.cs.

    public async Task<string?> ResolveStubPackageKeyAsync(string profileCanonicalUrl)
    {
        if (FindOwningIG(profileCanonicalUrl) is not (_, var ig))
            return null; // Not a known-IG profile (e.g. a server's custom profile) — no stub to flag.

        // Matched a known IG. The loader's Origin is the source of truth for
        // whether the profile actually validated against was the embedded
        // subset stub (package not installed) or the full package profile.
        var load = await _loader.LoadAsync(profileCanonicalUrl).ConfigureAwait(false);
        return load.IsSuccess && load.Value.Origin == StructureDefinitionOrigin.Embedded
            ? ig.PackageName
            : null;
    }

    /// <summary>
    /// The first canonical-base prefix match against the known-IG map: which
    /// known IG owns this canonical URL, or null for a profile outside every
    /// known IG (e.g. a server's custom profile). When two releases share a
    /// base (US Core), listing order picks the default owner for hints.
    /// </summary>
    private static (string IgKey, KnownIG Ig)? FindOwningIG(string profileCanonicalUrl)
    {
        var owners = FindOwningIGs(profileCanonicalUrl);
        return owners.Count > 0 ? owners[0] : null;
    }

    /// <summary>
    /// Every known IG whose canonical base prefixes this URL, in listing order.
    /// More than one entry means two releases of the same IG share the base.
    /// </summary>
    private static List<(string IgKey, KnownIG Ig)> FindOwningIGs(string profileCanonicalUrl)
    {
        var owners = new List<(string, KnownIG)>();
        if (string.IsNullOrWhiteSpace(profileCanonicalUrl))
            return owners;

        foreach (var (igKey, ig) in _knownIGs)
        {
            if (profileCanonicalUrl.StartsWith(ig.CanonicalBase, StringComparison.OrdinalIgnoreCase))
                owners.Add((igKey, ig));
        }

        return owners;
    }

    public async Task<string?> ResolveProfileCanonicalAsync(string profileShortName)
    {
        if (string.IsNullOrWhiteSpace(profileShortName))
            return null;

        // Da Vinci short names (profile-claim, profile-pas-request-bundle) give no
        // owning-IG hint, so the only sound resolution is to probe each known IG's
        // canonical base against the loaded index. First hit wins; callers run the
        // installed-package bootstrap before this, so a miss means "not installed".
        foreach (var (_, ig) in _knownIGs)
        {
            var canonical = ig.CanonicalBase + profileShortName;
            var load = await _loader.LoadAsync(canonical).ConfigureAwait(false);
            if (load.IsSuccess)
                return canonical;
        }

        return null;
    }

    public string? DescribeUnresolvedProfileHint(string profileRequested)
    {
        if (string.IsNullOrWhiteSpace(profileRequested))
            return null;

        var owners = FindOwningIGs(profileRequested);
        if (owners.Count > 0)
        {
            // ANY owning release installed → the profile name is wrong, not missing
            // (don't tell a customer to install what they already have — with two
            // US Core releases sharing the base, having either one counts). None
            // installed → the exact install command for the default owning IG,
            // never a different IG's package.
            var installed = owners.FirstOrDefault(o => IsIGInstalled(o.IgKey));
            if (installed != default)
                return $"The {installed.Ig.DisplayName} package is installed but defines no profile '{profileRequested}'. Check the profile name.";
            var (_, ig) = owners[0];
            return $"Install the {ig.DisplayName} package: pidgeon data install {ig.PackageName}";
        }

        // A bare short name that resolved nowhere (the owning IG is unknowable
        // without its package: short names carry no IG prefix). Name every
        // candidate rather than guessing one — the pre-fix defect was a hint
        // that confidently named the WRONG package (FABLE_CONFORM_AUDIT C11).
        if (!profileRequested.Contains('/') && !profileRequested.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var candidates = string.Join(" | ", _knownIGs.Values.Select(k => k.PackageName));
            return $"Profile '{profileRequested}' does not match any loaded profile. " +
                $"If it is an IG short name, install the owning package and retry ({candidates}); " +
                "otherwise check the profile name or pass the full canonical URL.";
        }

        return null;
    }

    public bool IsIGInstalled(string igKey)
    {
        if (!_knownIGs.TryGetValue(igKey, out var ig))
            return false;

        return _packages.GetInstalledContentRoot(ig.PackageName) is not null;
    }

    public async Task<Result<int>> LoadIGAsync(string igKey)
    {
        if (!_knownIGs.TryGetValue(igKey, out var ig))
        {
            return Result<int>.Failure(Error.Create(
                "FHIR_IG_UNKNOWN",
                $"Unknown FHIR IG key '{igKey}'. Known keys: {string.Join(", ", _knownIGs.Keys)}"));
        }

        var path = _packages.GetInstalledContentRoot(ig.PackageName);
        if (path == null)
        {
            return Result<int>.Failure(Error.Create(
                "FHIR_IG_NOT_INSTALLED",
                $"FHIR IG '{igKey}' requires package '{ig.PackageName}'. Install with: pidgeon data install {ig.PackageName}"));
        }

        // Guard against concurrent loads for the same IG, and short-circuit if
        // we've already loaded this IG in this process.
        await _loadLock.WaitAsync();
        try
        {
            if (_loaded.Contains(igKey))
                return Result<int>.Success(0);

            // Two releases of one IG share a canonical base (US Core 3.1.1
            // beside 6.1); the shared index resolves the bare canonical
            // last-write-wins, so loading a second release displaces the first
            // at unpinned URLs. Deterministic (known-IG listing order) and
            // stamped truthfully, but named out loud until the
            // artifact-version-isolation slice gives each run its own context.
            var displaced = _loaded.FirstOrDefault(loaded =>
                _knownIGs.TryGetValue(loaded, out var other)
                && string.Equals(other.CanonicalBase, ig.CanonicalBase, StringComparison.OrdinalIgnoreCase));
            if (displaced is not null)
            {
                _logger.LogWarning(
                    "FHIR IG {IgKey} shares its canonical base with already-loaded {DisplacedKey}; "
                    + "unpinned canonical URLs now resolve to {IgKey}'s profiles (version-pinned "
                    + "'url|version' requests still resolve each release exactly).",
                    igKey, displaced, igKey);
            }

            var result = await _loader.LoadDirectoryAsync(path);
            if (!result.IsSuccess)
                return result;

            _loaded.Add(igKey);
            _logger.LogInformation(
                "Loaded FHIR IG {IgKey} from {Path}: {Count} StructureDefinitions indexed",
                igKey, path, result.Value);

            await LoadDependencyDirsAsync(igKey, path);

            return result;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Load this IG's installed dependency-package directories into the shared
    /// index without displacing anything already indexed: primary IGs always
    /// win; dependencies never overwrite; dep-vs-dep collisions resolve
    /// first-loaded-wins, made deterministic by the resolver's ordered walk and
    /// the fixed known-IG iteration order. Runs inside the load lock. Missing
    /// or unloadable dependency dirs degrade to the validator's explicit
    /// "binding not validated" outcome rather than failing the IG load.
    /// </summary>
    private async Task LoadDependencyDirsAsync(string igKey, string packagePath)
    {
        if (_depResolver is null)
            return;

        foreach (var depDir in _depResolver.ResolveInstalledDependencyDirs(packagePath))
        {
            if (!_loadedDepDirs.Add(depDir))
                continue;

            var depResult = await _loader.LoadDirectoryAsync(depDir, overwriteExisting: false);
            if (depResult.IsSuccess)
            {
                _logger.LogInformation(
                    "Loaded {Count} dependency StructureDefinitions for IG {IgKey} from {Dir}",
                    depResult.Value, igKey, depDir);
            }
            else
            {
                _logger.LogWarning(
                    "Failed to load IG dependency dir {Dir} for {IgKey}: {Error}",
                    depDir, igKey, depResult.Error.Message);
            }
        }
    }

    public async Task<Result<int>> EnsureAllInstalledLoadedAsync()
    {
        var totalLoaded = 0;
        foreach (var igKey in _knownIGs.Keys)
        {
            if (!IsIGInstalled(igKey))
                continue;

            var result = await LoadIGAsync(igKey);
            if (!result.IsSuccess)
            {
                // A package that's on disk but unloadable (corrupt, wrong
                // version) shouldn't sink the caller. Log it, keep going with
                // the other IGs so validation still works for the healthy ones.
                _logger.LogWarning(
                    "Failed to load installed FHIR IG {IgKey}: {Error}",
                    igKey, result.Error.Message);
                continue;
            }
            totalLoaded += result.Value;
        }
        return Result<int>.Success(totalLoaded);
    }

    public async Task<Result<FHIRStructureDefinition>> GetProfileAsync(string igKey, string profileShortName)
    {
        if (!_knownIGs.TryGetValue(igKey, out var ig))
        {
            return Result<FHIRStructureDefinition>.Failure(Error.Create(
                "FHIR_IG_UNKNOWN",
                $"Unknown FHIR IG key '{igKey}'. Known keys: {string.Join(", ", _knownIGs.Keys)}"));
        }

        // Lazy-load on first access. If the package isn't installed the load
        // call returns the "not installed" error, which we propagate rather
        // than raising a separate "profile not found" downstream.
        if (!_loaded.Contains(igKey))
        {
            var loadResult = await LoadIGAsync(igKey);
            if (!loadResult.IsSuccess)
                return Result<FHIRStructureDefinition>.Failure(loadResult.Error);
        }

        var canonical = ig.CanonicalBase + profileShortName;
        return await _loader.LoadAsync(canonical);
    }
}
