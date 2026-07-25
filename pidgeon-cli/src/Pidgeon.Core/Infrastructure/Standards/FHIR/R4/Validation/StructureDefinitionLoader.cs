// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Loads FHIR R4 StructureDefinition JSON files from embedded resources, files, and directories.
/// Indexes by canonical URL and resource type for fast lookup.
/// </summary>
public interface IStructureDefinitionLoader
{
    Task<Result<FHIRStructureDefinition>> LoadAsync(string canonicalUrl);
    Task<Result<IReadOnlyList<FHIRStructureDefinition>>> LoadProfilesForResourceAsync(string resourceType);
    Task<Result<FHIRStructureDefinition>> LoadFromFileAsync(string filePath);

    /// <summary>
    /// Load every StructureDefinition / ValueSet / Bundle under a directory.
    /// <paramref name="overwriteExisting"/> false loads dependency-package
    /// directories without displacing already-indexed canonical URLs (primary
    /// IGs always win; dependencies never overwrite — ValueSets already behave
    /// this way via first-write-wins).
    /// </summary>
    Task<Result<int>> LoadDirectoryAsync(string directoryPath, bool overwriteExisting = true);
    bool IsLoaded(string canonicalUrl);

    /// <summary>
    /// The currently-indexed profile for a canonical URL, or null when nothing
    /// is indexed. Reports what a probe would actually validate against right
    /// now (its <see cref="FHIRStructureDefinition.Origin"/> and
    /// <see cref="FHIRStructureDefinition.Version"/>) — the single source of
    /// truth shared by the stub warning and the IG version stamp so the two
    /// artifacts cannot contradict each other. Accepts version-pinned
    /// canonicals (<c>url|version</c>) like every other lookup.
    /// </summary>
    FHIRStructureDefinition? GetIndexed(string canonicalUrl);
}

/// <summary>
/// Loads and caches StructureDefinition JSON from embedded resources and external files.
/// </summary>
public partial class StructureDefinitionLoader : IStructureDefinitionLoader
{
    private readonly ILogger<StructureDefinitionLoader> _logger;
    private readonly IDataResourceResolver _resourceResolver;
    private readonly ConcurrentDictionary<string, FHIRStructureDefinition> _byUrl = new();
    private readonly ConcurrentDictionary<string, List<FHIRStructureDefinition>> _byType = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _embeddedLoaded;

    // Portable prefix under which the base R4 profiles/ValueSets are served by a
    // registered data package (Baseline or BYO), replacing the former Core-embedded
    // resource bootstrap. Missing package -> index nothing (honest offline posture).
    private const string ProfilesPrefix = "standards/fhir/r4/profiles";

    // ValueSets loaded alongside profiles
    private readonly ConcurrentDictionary<string, FHIRValueSet> _valueSets = new();

    // CodeSystems loaded alongside profiles (first-write-wins, like ValueSets).
    // Lets whole-system ValueSet includes over HL7-published code systems be
    // enumerated offline — the upgrade path the ValueSetValidator ponytail note
    // names, consumed first by the structural synthesizer's binding fills.
    private readonly ConcurrentDictionary<string, FHIRCodeSystem> _codeSystems = new();

    public StructureDefinitionLoader(
        ILogger<StructureDefinitionLoader> logger,
        IDataResourceResolver resourceResolver)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
    }

    public async Task<Result<FHIRStructureDefinition>> LoadAsync(string canonicalUrl)
    {
        await EnsureEmbeddedLoadedAsync();

        if (_byUrl.TryGetValue(canonicalUrl, out var sd))
            return Result<FHIRStructureDefinition>.Success(sd);

        return Result<FHIRStructureDefinition>.Failure(
            Error.Create("PROFILE_NOT_FOUND", $"StructureDefinition not found: {canonicalUrl}"));
    }

    public FHIRStructureDefinition? GetIndexed(string canonicalUrl)
        => _byUrl.TryGetValue(canonicalUrl, out var sd) ? sd : null;

    public async Task<Result<IReadOnlyList<FHIRStructureDefinition>>> LoadProfilesForResourceAsync(string resourceType)
    {
        await EnsureEmbeddedLoadedAsync();

        if (_byType.TryGetValue(resourceType, out var profiles))
            return Result<IReadOnlyList<FHIRStructureDefinition>>.Success(profiles.AsReadOnly());

        return Result<IReadOnlyList<FHIRStructureDefinition>>.Success(
            Array.Empty<FHIRStructureDefinition>());
    }

    public async Task<Result<FHIRStructureDefinition>> LoadFromFileAsync(string filePath)
    {
        await EnsureEmbeddedLoadedAsync();

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            // A user-supplied file is not an embedded stub; its provenance is File.
            var parsed = ParseStructureDefinitionJson(json, StructureDefinitionOrigin.File);
            if (parsed.IsSuccess)
                IndexProfile(parsed.Value);

            // Success or failure, return the parse result so a differential-only
            // or malformed profile surfaces its specific reason to the caller
            // instead of a generic "could not parse".
            return parsed;
        }
        catch (Exception ex)
        {
            return Result<FHIRStructureDefinition>.Failure(
                Error.Create("FILE_ERROR", $"Error reading profile file: {ex.Message}"));
        }
    }

    public async Task<Result<int>> LoadDirectoryAsync(string directoryPath, bool overwriteExisting = true)
    {
        await EnsureEmbeddedLoadedAsync();

        if (!Directory.Exists(directoryPath))
            return Result<int>.Failure(
                Error.Create("DIRECTORY_NOT_FOUND", $"Directory not found: {directoryPath}"));

        var count = 0;
        foreach (var file in Directory.GetFiles(directoryPath, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("resourceType", out var rt))
                {
                    var resourceType = rt.GetString();
                    if (resourceType == "StructureDefinition")
                    {
                        var parsed = ParseFromJsonElement(root, StructureDefinitionOrigin.Package);
                        if (parsed.IsSuccess)
                        {
                            if (overwriteExisting || !_byUrl.ContainsKey(parsed.Value.Url))
                            {
                                IndexProfile(parsed.Value);
                                count++;
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Skipped profile from {File}: {Reason}",
                                file, parsed.Error.Message);
                        }
                    }
                    else if (resourceType == "Bundle")
                    {
                        count += LoadFromBundle(root, StructureDefinitionOrigin.Package);
                    }
                    else if (resourceType == "ValueSet")
                    {
                        var vs = StructureDefinitionElementParser.ParseValueSet(root);
                        if (vs != null)
                            _valueSets.TryAdd(vs.Url, vs);
                    }
                    else if (resourceType == "CodeSystem")
                    {
                        var cs = StructureDefinitionElementParser.ParseCodeSystem(root);
                        if (cs != null)
                            _codeSystems.TryAdd(cs.Url, cs);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse profile from {File}", file);
            }
        }

        return Result<int>.Success(count);
    }

    public bool IsLoaded(string canonicalUrl) => _byUrl.ContainsKey(canonicalUrl);

    internal IReadOnlyDictionary<string, FHIRValueSet> GetValueSets() => _valueSets;

    internal async Task EnsureInitializedAsync() => await EnsureEmbeddedLoadedAsync();

    private async Task EnsureEmbeddedLoadedAsync()
    {
        if (_embeddedLoaded) return;

        await _initLock.WaitAsync();
        try
        {
            if (_embeddedLoaded) return;
            await LoadBaseProfilesAsync();
            _embeddedLoaded = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task LoadBaseProfilesAsync()
    {
        var listing = _resourceResolver.ListResourcePaths(ProfilesPrefix);
        if (listing.IsFailure)
        {
            // PACKAGE_REQUIRED / conflict — index nothing. Honest offline posture:
            // LoadAsync then returns PROFILE_NOT_FOUND and BaseStructureDefinitionValidator
            // degrades to the required-field floor until a data package supplies the SDs.
            _logger.LogDebug("No FHIR R4 base profiles available: {Reason}", listing.Error.Message);
            return;
        }

        var resourcePaths = listing.Value
            .Where(n => n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogDebug("Found {Count} FHIR base profile resources", resourcePaths.Count);

        foreach (var resourcePath in resourcePaths)
        {
            try
            {
                var streamResult = await _resourceResolver.OpenReadAsync(resourcePath).ConfigureAwait(false);
                if (streamResult.IsFailure) continue;

                using var stream = streamResult.Value;
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("resourceType", out var rt)) continue;

                var type = rt.GetString();
                if (type == "StructureDefinition")
                {
                    var parsed = ParseFromJsonElement(root, StructureDefinitionOrigin.Embedded);
                    if (parsed.IsSuccess)
                    {
                        IndexProfile(parsed.Value);
                        _logger.LogDebug("Loaded base profile: {Url}", parsed.Value.Url);
                    }
                    else
                    {
                        _logger.LogWarning("Skipped base profile {Name}: {Reason}",
                            resourcePath, parsed.Error.Message);
                    }
                }
                else if (type == "ValueSet")
                {
                    var vs = StructureDefinitionElementParser.ParseValueSet(root);
                    if (vs != null)
                    {
                        _valueSets.TryAdd(vs.Url, vs);
                        _logger.LogDebug("Loaded base ValueSet: {Url}", vs.Url);
                    }
                }
                else if (type == "CodeSystem")
                {
                    var cs = StructureDefinitionElementParser.ParseCodeSystem(root);
                    if (cs != null)
                        _codeSystems.TryAdd(cs.Url, cs);
                }
                else if (type == "Bundle")
                {
                    LoadFromBundle(root, StructureDefinitionOrigin.Embedded);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse base resource: {Name}", resourcePath);
            }
        }

        _logger.LogInformation("Loaded {ProfileCount} FHIR profiles and {ValueSetCount} ValueSets from the base data package",
            _byUrl.Count, _valueSets.Count);
    }

    private int LoadFromBundle(JsonElement root, StructureDefinitionOrigin origin)
    {
        var count = 0;
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return 0;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("resource", out var resource)) continue;
            if (!resource.TryGetProperty("resourceType", out var rt)) continue;

            var type = rt.GetString();
            if (type == "StructureDefinition")
            {
                var parsed = ParseFromJsonElement(resource, origin);
                if (parsed.IsSuccess)
                {
                    IndexProfile(parsed.Value);
                    count++;
                }
                else
                {
                    _logger.LogWarning("Skipped profile from bundle entry: {Reason}", parsed.Error.Message);
                }
            }
            else if (type == "ValueSet")
            {
                var vs = StructureDefinitionElementParser.ParseValueSet(resource);
                if (vs != null)
                    _valueSets.TryAdd(vs.Url, vs);
            }
            else if (type == "CodeSystem")
            {
                var cs = StructureDefinitionElementParser.ParseCodeSystem(resource);
                if (cs != null)
                    _codeSystems.TryAdd(cs.Url, cs);
            }
        }

        return count;
    }

    private Result<FHIRStructureDefinition> ParseStructureDefinitionJson(string json, StructureDefinitionOrigin origin)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("resourceType", out var rt) || rt.GetString() != "StructureDefinition")
                return Result<FHIRStructureDefinition>.Failure(
                    Error.Create("INVALID_PROFILE", "JSON is not a FHIR StructureDefinition resource."));

            return ParseFromJsonElement(root, origin);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse StructureDefinition JSON");
            return Result<FHIRStructureDefinition>.Failure(
                Error.Create("INVALID_PROFILE", $"Malformed StructureDefinition JSON: {ex.Message}"));
        }
    }

    private Result<FHIRStructureDefinition> ParseFromJsonElement(JsonElement root, StructureDefinitionOrigin origin)
    {
        var sd = new FHIRStructureDefinition
        {
            Url = root.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
            Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Type = root.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
            BaseDefinition = root.TryGetProperty("baseDefinition", out var bd) ? bd.GetString() : null,
            Version = root.TryGetProperty("version", out var ver) ? ver.GetString() : null,
            Origin = origin,
        };

        if (string.IsNullOrEmpty(sd.Url) || string.IsNullOrEmpty(sd.Type))
            return Result<FHIRStructureDefinition>.Failure(
                Error.Create("INVALID_PROFILE", "StructureDefinition is missing a url or type."));

        if (root.TryGetProperty("snapshot", out var snapshot) &&
            snapshot.TryGetProperty("element", out var elements) &&
            elements.ValueKind == JsonValueKind.Array)
        {
            sd.Elements = elements.EnumerateArray()
                .Select(StructureDefinitionElementParser.ParseElement)
                .ToList();
        }

        // C10: a profile carrying only a differential (no expanded snapshot)
        // parses to zero constraints and would then pass every resource
        // vacuously. Refuse it loudly rather than index a 0-element profile —
        // we do not snapshot-expand against the base definition here.
        if (sd.Elements.Count == 0 && HasDifferentialElements(root))
            return Result<FHIRStructureDefinition>.Failure(Error.Create(
                "PROFILE_DIFFERENTIAL_ONLY",
                $"Differential-only StructureDefinition; snapshot required for validation: {sd.Url}"));

        return Result<FHIRStructureDefinition>.Success(sd);
    }

    private static bool HasDifferentialElements(JsonElement root) =>
        root.TryGetProperty("differential", out var diff)
        && diff.TryGetProperty("element", out var els)
        && els.ValueKind == JsonValueKind.Array
        && els.GetArrayLength() > 0;

    private void IndexProfile(FHIRStructureDefinition sd)
    {
        // Last-write-wins: a profile loaded later (typically from an installed
        // IG package via FhirIGService.EnsureAllInstalledLoadedAsync) replaces
        // any previously indexed profile at the same canonical URL. This is
        // what makes the IG dispatcher actually useful — embedded stubs
        // (loaded first, during EnsureEmbeddedLoadedAsync) serve as a
        // baseline, and package-delivered profiles override them at the
        // same URL when the user installs the real IG.
        var hadPrevious = _byUrl.ContainsKey(sd.Url);
        _byUrl[sd.Url] = sd;

        // Also index the version-pinned canonical (url|version, the FHIR
        // canonical-version form). Two IG releases share the bare canonical
        // (US Core 3.1.1 beside 6.1); the pinned key never collides across
        // releases, so a |-suffixed profile request resolves the exact release
        // even when the bare URL was last-write-won by the other one.
        if (!string.IsNullOrEmpty(sd.Version))
            _byUrl[$"{sd.Url}|{sd.Version}"] = sd;

        _byType.AddOrUpdate(
            sd.Type,
            _ => new List<FHIRStructureDefinition> { sd },
            (_, list) =>
            {
                lock (list)
                {
                    // If we're replacing an existing profile at the same URL,
                    // drop the stale entry from the resource-type index too —
                    // otherwise LoadProfilesForResourceAsync would return two
                    // entries for the same URL (old embedded stub + new
                    // package profile).
                    if (hadPrevious)
                        list.RemoveAll(p => p.Url == sd.Url);
                    list.Add(sd);
                }
                return list;
            });
    }
}

/// <summary>
/// Lightweight ValueSet model for code validation. Captures the composition
/// shapes membership checking can decide offline: enumerated codes (inline
/// concepts or a pre-computed expansion), whole-system includes, and imported
/// ValueSets — plus a flag for content that cannot be expanded offline
/// (filters, excludes), which forces the tri-state membership check to report
/// "not validatable" instead of guessing.
/// </summary>
public class FHIRValueSet
{
    public string Url { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Systems { get; set; } = new();
    public IReadOnlyList<FHIRValueSetCode> Codes { get; set; } = Array.Empty<FHIRValueSetCode>();

    /// <summary>Systems included whole (an include with a system but no concept
    /// enumeration): any coding from such a system is a member.</summary>
    public IReadOnlyList<string> WholeSystemIncludes { get; set; } = Array.Empty<string>();

    /// <summary>Canonical URLs of imported ValueSets (compose.include[].valueSet),
    /// resolved lazily against the loaded index at validation time.</summary>
    public IReadOnlyList<string> ImportedValueSets { get; set; } = Array.Empty<string>();

    /// <summary>True when the composition carries content that cannot be
    /// expanded offline (filters, excludes, partial expansions): a code that
    /// matches nothing enumerable might still be a member, so absence is
    /// undecidable.</summary>
    public bool HasUnexpandableContent { get; set; }
}

/// <summary>
/// A single code within a ValueSet.
/// </summary>
public class FHIRValueSetCode
{
    public string System { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Display { get; set; }
}
