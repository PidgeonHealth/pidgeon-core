// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

return await new SourceLinkVerifier().RunAsync(args);

internal sealed class SourceLinkVerifier
{
    private const string ExpectedRepository = "PidgeonHealth/pidgeon-core";
    private static readonly Guid SourceLinkKind =
        new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    private static readonly Guid EmbeddedSourceKind =
        new("0E8A571B-6926-466E-B4AD-8AB04611F5FE");
    private static readonly Guid Sha1Kind =
        new("FF1816EC-AA5E-4D10-87F7-6F4963833460");
    private static readonly Guid Sha256Kind =
        new("8829D00F-11B8-4213-878B-770E8597AC16");
    private static readonly HashSet<string> AllowedSymbolPackageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".nuspec",
            ".p7s",
            ".pdb",
            ".psmdcp",
            ".rels",
            ".xml",
        };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    public async Task<int> RunAsync(string[] arguments)
    {
        var options = ParseArguments(arguments);
        if (options is null)
        {
            Console.Error.WriteLine(
                "Usage: Pidgeon.SourceLinkVerifier --commit <40-character public commit SHA> " +
                "[--package <top-level .snupkg>] " +
                "[--symbol-server | --symbol-server-probe]");
            return 2;
        }

        var symbolPackages = options.PackageName is null
            ? Directory
                .EnumerateFiles(
                    Directory.GetCurrentDirectory(),
                    "*.snupkg",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
            : [Path.Combine(Directory.GetCurrentDirectory(), options.PackageName)];
        var expectedPackageCount = options.PackageName is null ? 2 : 1;
        if (symbolPackages.Length != expectedPackageCount)
        {
            Console.Error.WriteLine(
                $"Expected exactly {expectedPackageCount} .snupkg file(s), " +
                $"found {symbolPackages.Length}.");
            return 1;
        }

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Pidgeon-SourceLink-Verifier/0.1");

        try
        {
            var totalDocuments = 0;
            foreach (var symbolPackage in symbolPackages)
            {
                totalDocuments += await VerifyPackageAsync(
                    symbolPackage,
                    options.Commit,
                    options.SymbolServerMode);
            }

            Console.WriteLine(
                $"SOURCE-LINK-OK packages={symbolPackages.Length} documents={totalDocuments} " +
                $"repository={ExpectedRepository} commit={options.Commit}");
            return 0;
        }
        catch (SymbolNotIndexedException exception)
        {
            Console.Error.WriteLine($"SYMBOL-SERVER-MISSING {exception.Message}");
            return 3;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            IOException or
            HttpRequestException or
            JsonException or
            CryptographicException or
            OperationCanceledException)
        {
            Console.Error.WriteLine($"Source Link verification failed: {exception.Message}");
            return 1;
        }
    }

    private async Task<int> VerifyPackageAsync(
        string packagePath,
        string commit,
        SymbolServerMode symbolServerMode)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        ValidateSymbolPackageStructure(archive, packagePath);
        var pdbEntries = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (pdbEntries.Length != 1)
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(packagePath)} must contain exactly one portable PDB.");
        }

        await using var pdbStream = new MemoryStream();
        await using (var entryStream = pdbEntries[0].Open())
        {
            await entryStream.CopyToAsync(pdbStream);
        }
        pdbStream.Position = 0;

        using var provider = MetadataReaderProvider.FromPortablePdbStream(
            pdbStream,
            MetadataStreamOptions.LeaveOpen);
        var reader = provider.GetMetadataReader();
        if (symbolServerMode != SymbolServerMode.None)
        {
            await VerifyPublishedSymbolAsync(
                pdbEntries[0].Name,
                pdbStream.ToArray(),
                reader,
                symbolServerMode == SymbolServerMode.Wait);
        }
        var mappings = ReadMappings(reader, commit);
        var documents = reader.Documents
            .Select(handle => ReadDocument(reader, handle, mappings))
            .ToArray();
        if (documents.Length == 0)
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(packagePath)} contains no source documents.");
        }

        using var concurrency = new SemaphoreSlim(8);
        await Task.WhenAll(documents.Select(async document =>
        {
            await concurrency.WaitAsync();
            try
            {
                await VerifyDocumentAsync(document);
            }
            finally
            {
                concurrency.Release();
            }
        }));

        Console.WriteLine(
            $"SOURCE-LINK-PACKAGE-OK package={Path.GetFileName(packagePath)} " +
            $"documents={documents.Length}");
        return documents.Length;
    }

    private async Task VerifyPublishedSymbolAsync(
        string pdbFileName,
        byte[] expectedBytes,
        MetadataReader reader,
        bool waitForIndexing)
    {
        var header = reader.DebugMetadataHeader ??
            throw new InvalidDataException(
                $"Portable PDB '{pdbFileName}' has no debug metadata header.");
        var id = header.Id;
        if (id.Length < 16)
        {
            throw new InvalidDataException(
                $"Portable PDB '{pdbFileName}' has a truncated debug identifier.");
        }

        var signature = new Guid(id.AsSpan(0, 16))
            .ToString("N")
            .ToLowerInvariant();
        var normalizedName = pdbFileName.ToLowerInvariant();
        var url =
            $"https://symbols.nuget.org/download/symbols/{normalizedName}/" +
            $"{signature}FFFFFFFF/{normalizedName}";
        var expectedHash = SHA256.HashData(expectedBytes);

        var attemptCount = waitForIndexing ? 60 : 1;
        for (var attempt = 1; attempt <= attemptCount; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.AcceptEncoding.ParseAdd("identity");
            request.Headers.TryAddWithoutValidation(
                "SymbolChecksum",
                $"SHA256:{Convert.ToHexString(expectedHash).ToLowerInvariant()}");
            using var response = await _httpClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                if (!waitForIndexing)
                {
                    throw new SymbolNotIndexedException(
                        $"NuGet.org has not indexed '{pdbFileName}'.");
                }
                if (attempt == attemptCount)
                {
                    throw new HttpRequestException(
                        $"NuGet.org did not index '{pdbFileName}' within 15 minutes.");
                }
                await Task.Delay(TimeSpan.FromSeconds(15));
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"NuGet symbol URL returned {(int)response.StatusCode}: {url}");
            }

            var actualBytes = await response.Content.ReadAsByteArrayAsync();
            var actualHash = SHA256.HashData(actualBytes);
            if (!actualHash.AsSpan().SequenceEqual(expectedHash))
            {
                throw new CryptographicException(
                    $"NuGet symbol-server bytes differ for '{pdbFileName}'.");
            }

            Console.WriteLine(
                $"SYMBOL-SERVER-OK pdb={pdbFileName} sha256=" +
                Convert.ToHexString(actualHash).ToLowerInvariant());
            return;
        }
    }

    private static Options? ParseArguments(string[] arguments)
    {
        string? commit = null;
        string? packageName = null;
        var symbolServerMode = SymbolServerMode.None;

        for (var index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--commit" when index + 1 < arguments.Length && commit is null:
                    commit = arguments[++index].ToLowerInvariant();
                    break;
                case "--package" when index + 1 < arguments.Length && packageName is null:
                    packageName = arguments[++index];
                    break;
                case "--symbol-server" when symbolServerMode == SymbolServerMode.None:
                    symbolServerMode = SymbolServerMode.Wait;
                    break;
                case "--symbol-server-probe" when symbolServerMode == SymbolServerMode.None:
                    symbolServerMode = SymbolServerMode.Probe;
                    break;
                default:
                    return null;
            }
        }

        if (commit is null ||
            commit.Length != 40 ||
            commit.Any(character => !Uri.IsHexDigit(character)))
        {
            return null;
        }
        if (packageName is not null &&
            (!string.Equals(
                Path.GetFileName(packageName),
                packageName,
                StringComparison.Ordinal) ||
             !packageName.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return new Options(commit, packageName, symbolServerMode);
    }

    private static void ValidateSymbolPackageStructure(
        ZipArchive archive,
        string packagePath)
    {
        var packageName = Path.GetFileName(packagePath);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!paths.Add(entry.FullName))
            {
                throw new InvalidDataException(
                    $"{packageName} contains duplicate path '{entry.FullName}'.");
            }
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            var extension = Path.GetExtension(entry.FullName);
            if (!AllowedSymbolPackageExtensions.Contains(extension))
            {
                throw new InvalidDataException(
                    $"{packageName} contains forbidden symbol-package entry '{entry.FullName}'.");
            }
        }

        var nuspecEntries = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nuspecEntries.Length != 1)
        {
            throw new InvalidDataException(
                $"{packageName} must contain exactly one nuspec.");
        }

        using var nuspecStream = nuspecEntries[0].Open();
        var nuspec = XDocument.Load(nuspecStream, LoadOptions.None);
        var packageTypes = nuspec
            .Descendants()
            .Where(element => element.Name.LocalName == "packageType")
            .Select(element => element.Attribute("name")?.Value)
            .Where(name => name is not null)
            .ToArray();
        if (packageTypes.Length != 1 ||
            !string.Equals(packageTypes[0], "SymbolsPackage", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{packageName} must declare exactly one SymbolsPackage package type.");
        }
    }

    private static IReadOnlyList<SourceMapping> ReadMappings(
        MetadataReader reader,
        string commit)
    {
        byte[]? sourceLinkBytes = null;
        foreach (var handle in reader.GetCustomDebugInformation(
                     MetadataTokens.EntityHandle(0x00000001)))
        {
            var custom = reader.GetCustomDebugInformation(handle);
            if (reader.GetGuid(custom.Kind) == SourceLinkKind)
            {
                sourceLinkBytes = reader.GetBlobBytes(custom.Value);
                break;
            }
        }

        if (sourceLinkBytes is null)
        {
            throw new InvalidDataException("Portable PDB contains no Source Link mapping.");
        }

        using var document = JsonDocument.Parse(sourceLinkBytes);
        if (!document.RootElement.TryGetProperty("documents", out var mappingsElement) ||
            mappingsElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Source Link JSON has no documents map.");
        }

        var expectedPrefix =
            $"https://raw.githubusercontent.com/{ExpectedRepository}/{commit}/";
        var mappings = new List<SourceMapping>();
        foreach (var property in mappingsElement.EnumerateObject())
        {
            var target = property.Value.GetString();
            if (target is null ||
                !target.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
                property.Name.Count(character => character == '*') != 1 ||
                target.Count(character => character == '*') != 1)
            {
                throw new InvalidDataException(
                    $"Source Link mapping is not pinned to {ExpectedRepository}@{commit}.");
            }
            mappings.Add(new SourceMapping(property.Name, target));
        }

        if (mappings.Count == 0)
        {
            throw new InvalidDataException("Source Link documents map is empty.");
        }

        return mappings;
    }

    private static SourceDocument ReadDocument(
        MetadataReader reader,
        DocumentHandle handle,
        IReadOnlyList<SourceMapping> mappings)
    {
        var document = reader.GetDocument(handle);
        var name = reader.GetString(document.Name);
        var algorithm = reader.GetGuid(document.HashAlgorithm);
        if (algorithm != Sha1Kind && algorithm != Sha256Kind)
        {
            throw new InvalidDataException(
                $"Unsupported document hash algorithm {algorithm} for '{name}'.");
        }

        var expectedHash = reader.GetBlobBytes(document.Hash);
        if (expectedHash.Length == 0)
        {
            throw new InvalidDataException($"Document '{name}' has no checksum.");
        }

        var embeddedSource = ReadEmbeddedSource(reader, handle, name);
        if (IsCompilerGeneratedDocument(name))
        {
            if (embeddedSource is null)
            {
                throw new InvalidDataException(
                    $"Compiler-generated document '{name}' is not embedded.");
            }
            return new SourceDocument(name, null, embeddedSource, algorithm, expectedHash);
        }

        var url = mappings
            .Select(mapping => mapping.Resolve(name))
            .FirstOrDefault(candidate => candidate is not null);
        if (url is null)
        {
            throw new InvalidDataException(
                $"Non-embedded document '{name}' has no Source Link mapping.");
        }

        return new SourceDocument(name, url, null, algorithm, expectedHash);
    }

    private static bool IsCompilerGeneratedDocument(string documentName)
    {
        var normalized = documentName.Replace('\\', '/');
        if (!normalized.Contains("/obj/", StringComparison.Ordinal))
        {
            return false;
        }

        var fileName = Path.GetFileName(normalized);
        return fileName.EndsWith(".AssemblyAttributes.cs", StringComparison.Ordinal) ||
               fileName.EndsWith(".AssemblyInfo.cs", StringComparison.Ordinal) ||
               fileName.EndsWith(".GlobalUsings.g.cs", StringComparison.Ordinal);
    }

    private async Task VerifyDocumentAsync(SourceDocument document)
    {
        byte[] bytes;
        if (document.EmbeddedSource is not null)
        {
            bytes = document.EmbeddedSource;
        }
        else
        {
            using var response = await _httpClient.GetAsync(document.Url);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Source URL returned {(int)response.StatusCode}: {document.Url}");
            }

            bytes = await response.Content.ReadAsByteArrayAsync();
        }

        var actualHash = document.Algorithm == Sha256Kind
            ? SHA256.HashData(bytes)
            : SHA1.HashData(bytes);
        if (!actualHash.AsSpan().SequenceEqual(document.ExpectedHash))
        {
            throw new CryptographicException(
                $"Source checksum mismatch for '{document.Name}'.");
        }
    }

    private static byte[]? ReadEmbeddedSource(
        MetadataReader reader,
        DocumentHandle handle,
        string documentName)
    {
        byte[]? blob = null;
        foreach (var customHandle in reader.GetCustomDebugInformation(handle))
        {
            var custom = reader.GetCustomDebugInformation(customHandle);
            if (reader.GetGuid(custom.Kind) != EmbeddedSourceKind)
            {
                continue;
            }

            if (blob is not null)
            {
                throw new InvalidDataException(
                    $"Document '{documentName}' has multiple embedded-source records.");
            }
            blob = reader.GetBlobBytes(custom.Value);
        }

        if (blob is null)
        {
            return null;
        }
        if (blob.Length < sizeof(int))
        {
            throw new InvalidDataException(
                $"Embedded source for '{documentName}' is truncated.");
        }

        var uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(blob);
        if (uncompressedSize == 0)
        {
            return blob[sizeof(int)..];
        }
        if (uncompressedSize < 0)
        {
            throw new InvalidDataException(
                $"Embedded source for '{documentName}' has an invalid length.");
        }

        using var compressed = new MemoryStream(blob, sizeof(int), blob.Length - sizeof(int));
        using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
        using var source = new MemoryStream(uncompressedSize);
        deflate.CopyTo(source);
        if (source.Length != uncompressedSize)
        {
            throw new InvalidDataException(
                $"Embedded source for '{documentName}' did not expand to its declared length.");
        }
        return source.ToArray();
    }

    private sealed record SourceMapping(string Pattern, string Target)
    {
        public string? Resolve(string documentName)
        {
            var wildcard = Pattern.IndexOf('*', StringComparison.Ordinal);
            var prefix = Pattern[..wildcard];
            var suffix = Pattern[(wildcard + 1)..];
            if (!documentName.StartsWith(prefix, StringComparison.Ordinal) ||
                !documentName.EndsWith(suffix, StringComparison.Ordinal) ||
                documentName.Length < prefix.Length + suffix.Length)
            {
                return null;
            }

            var captureLength = documentName.Length - prefix.Length - suffix.Length;
            var capture = documentName.Substring(prefix.Length, captureLength)
                .Replace('\\', '/');
            return Target.Replace("*", capture, StringComparison.Ordinal);
        }
    }

    private sealed record SourceDocument(
        string Name,
        string? Url,
        byte[]? EmbeddedSource,
        Guid Algorithm,
        byte[] ExpectedHash);

    private sealed record Options(
        string Commit,
        string? PackageName,
        SymbolServerMode SymbolServerMode);

    private enum SymbolServerMode
    {
        None,
        Probe,
        Wait,
    }

    private sealed class SymbolNotIndexedException(string message) : IOException(message);
}
