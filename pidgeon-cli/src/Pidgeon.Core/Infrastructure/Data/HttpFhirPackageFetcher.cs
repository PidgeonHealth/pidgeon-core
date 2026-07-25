// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.DTOs.Data;
using Pidgeon.Core.Application.Interfaces.Data;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Data;

/// <summary>
/// Downloads a CC0 FHIR IG package.tgz from the HL7 registry and extracts it
/// into a local package directory. The C#-side counterpart of the Python
/// <c>extract_fhir_*.py</c> scripts, used by <see cref="DataPackageManager"/>
/// when a known IG has no local source extract.
///
/// <para>
/// Resource filtering: top-level StructureDefinition / ValueSet / CodeSystem
/// JSON resources are kept for the validator, and CapabilityStatement is kept
/// as the machine-readable source of an IG's declared server requirements
/// (the US Core readiness-baseline compiler reads it; the profile loader
/// skips it). SearchParameter, examples, and narrative artefacts are dropped
/// because nothing indexes them. Installs that predate CapabilityStatement
/// retention lack the file — reinstall to populate, the same recovery as the
/// manifest dependency map. The written manifest carries the standard
/// <see cref="PackageManifest"/> fields (the canonical base and per-type
/// counts the Python script also records are informational only and are
/// sourced elsewhere by the loader / IG service).
/// </para>
/// </summary>
public sealed class HttpFhirPackageFetcher : IFhirPackageFetcher
{
    private static readonly HashSet<string> IncludedResourceTypes = new(StringComparer.Ordinal)
    {
        "StructureDefinition", "ValueSet", "CodeSystem", "CapabilityStatement",
    };

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpFhirPackageFetcher> _logger;

    public HttpFhirPackageFetcher(IHttpClientFactory httpClientFactory, ILogger<HttpFhirPackageFetcher> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<int>> FetchAsync(
        FhirPackageFetchRequest request,
        string targetDir,
        CancellationToken cancellationToken = default)
    {
        byte[] tarball;
        try
        {
            _logger.LogInformation(
                "Fetching FHIR IG package {Package} from {Url}",
                request.PackageName, request.DownloadUrl);
            var http = _httpClientFactory.CreateClient(nameof(HttpFhirPackageFetcher));
            tarball = await http.GetByteArrayAsync(request.DownloadUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return Result<int>.Failure(Error.Create(
                "PACKAGE_FETCH_FAILED",
                $"Failed to download package '{request.PackageName}' from {request.DownloadUrl}: {ex.Message}",
                request.PackageName));
        }

        return ExtractPackage(tarball, request, targetDir);
    }

    /// <summary>
    /// Gunzip + untar the package bytes, keep the validation-relevant resources,
    /// and write them plus a manifest.json into <paramref name="targetDir"/>.
    /// Separated from the HTTP fetch so the extraction is unit-testable against
    /// an in-memory tarball without touching the network.
    /// </summary>
    private Result<int> ExtractPackage(byte[] tarball, FhirPackageFetchRequest request, string targetDir)
    {
        List<(string FileName, string Content)> kept;
        string? packageJson;
        try
        {
            (kept, packageJson) = ReadKeptResources(tarball);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return Result<int>.Failure(Error.Create(
                "PACKAGE_FETCH_INVALID",
                $"Downloaded package '{request.PackageName}' is not a readable gzip tarball: {ex.Message}",
                request.PackageName));
        }

        if (kept.Count == 0)
        {
            return Result<int>.Failure(Error.Create(
                "PACKAGE_FETCH_EMPTY",
                $"Package '{request.PackageName}' contained no StructureDefinition / ValueSet / CodeSystem resources. " +
                "The download URL or package layout may have changed.",
                request.PackageName));
        }

        Directory.CreateDirectory(targetDir);
        foreach (var existing in Directory.EnumerateFiles(targetDir, "*.json"))
            File.Delete(existing);

        foreach (var (fileName, content) in kept)
            File.WriteAllText(Path.Combine(targetDir, fileName), content);

        var (dependencies, packageLicense) = ParsePackageMetadata(packageJson, request.PackageName);
        var manifest = new PackageManifest
        {
            Name = request.PackageName,
            Version = request.Version,
            Description = request.Description,
            Source = request.Source,
            // A dependency fetch has no curated registry license; fall back to the
            // license the package declares about itself.
            License = string.IsNullOrEmpty(request.License) ? packageLicense ?? "" : request.License,
            RecordCount = kept.Count,
            DataType = request.DataType,
            Schema = "1.0",
            GeneratedAt = DateTimeOffset.UtcNow,
            Dependencies = dependencies,
        };
        File.WriteAllText(
            Path.Combine(targetDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, ManifestJsonOptions));

        _logger.LogInformation(
            "Extracted {Count} resources for FHIR IG package {Package} into {Dir}",
            kept.Count, request.PackageName, targetDir);

        return Result<int>.Success(kept.Count);
    }

    private static (List<(string FileName, string Content)> Kept, string? PackageJson) ReadKeptResources(byte[] tarball)
    {
        var kept = new List<(string, string)>();
        string? packageJson = null;

        using var raw = new MemoryStream(tarball, writable: false);
        using var gzip = new GZipStream(raw, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        while (tar.GetNextEntry() is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                continue;
            if (entry.DataStream is null)
                continue;

            // FHIR packages prefix every file with `package/`. Keep only the
            // flat top-level JSON resources (drop nested `example/`, `other/`).
            var name = entry.Name;
            if (name.StartsWith("package/", StringComparison.Ordinal))
                name = name["package/".Length..];
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Contains('/'))
                continue;

            // Entry names become on-disk paths via Path.Combine(targetDir, name):
            // reject traversal ("..\"), rooted, or otherwise hostile names so a
            // malicious archive can't write outside the package directory.
            if (IsUnsafeEntryName(name))
                continue;

            // Copy the entry into memory rather than wrapping DataStream in a
            // StreamReader: disposing the reader would dispose the underlying
            // SubReadStream and break TarReader's advance to the next entry.
            string content;
            using (var entryContent = new MemoryStream())
            {
                entry.DataStream.CopyTo(entryContent);
                content = Encoding.UTF8.GetString(entryContent.ToArray());
            }

            // The NPM-style manifest carries the package's dependency map. It is
            // captured for the written manifest.json but never written to the
            // install dir itself (it is not a FHIR resource).
            if (string.Equals(name, "package.json", StringComparison.OrdinalIgnoreCase))
            {
                packageJson = content;
                continue;
            }

            if (!IsIncludedResourceType(content))
                continue;

            kept.Add((name, content));
        }

        return (kept, packageJson);
    }

    /// <summary>
    /// Parse the dependency map and self-declared license out of a package's
    /// NPM-style package.json. Malformed or absent metadata degrades to
    /// (null, null) — dependency resolution is best-effort, never a fetch failure.
    /// </summary>
    private (IReadOnlyDictionary<string, string>? Dependencies, string? License) ParsePackageMetadata(
        string? packageJson, string packageName)
    {
        if (string.IsNullOrEmpty(packageJson))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(packageJson);
            var root = doc.RootElement;

            string? license = root.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.String
                ? lic.GetString()
                : null;

            Dictionary<string, string>? deps = null;
            if (root.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Object)
            {
                deps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in d.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        deps[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            return (deps is { Count: > 0 } ? deps : null, license);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Package {Package} has an unreadable package.json; dependencies not captured", packageName);
            return (null, null);
        }
    }

    private static bool IsIncludedResourceType(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("resourceType", out var rt)
                && rt.ValueKind == JsonValueKind.String
                && IncludedResourceTypes.Contains(rt.GetString()!);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsUnsafeEntryName(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return true;
        if (entryName.Contains('\0')) return true;
        if (entryName.StartsWith('/') || entryName.StartsWith('\\')) return true;
        if (entryName.Contains(':') || Path.IsPathRooted(entryName)) return true;
        return entryName.Split('/', '\\').Any(segment => segment == "..");
    }
}
