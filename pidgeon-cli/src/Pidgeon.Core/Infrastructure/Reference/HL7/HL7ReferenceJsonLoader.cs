// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Reference.HL7;

/// <summary>
/// Shared JSON loader used by every HL7 reference loader. All reads go through
/// the context's <see cref="Pidgeon.Core.Application.Interfaces.Data.IDataResourceResolver"/>
/// at portable paths (<c>{PortableBasePath}/{relativePath}</c>), keeping the
/// data-source strategy in exactly one place: the resolver registration decides
/// whether definitions come from a data package, the legacy embedded assembly,
/// or a test double.
/// </summary>
public sealed class HL7ReferenceJsonLoader
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    // Per-(base path, subfolder) listing snapshot backing the synchronous
    // Exists/EnumerateFiles members (GetConfidence/IsKnownElement are sync
    // interface members). One resolver listing per subfolder per process;
    // staleness on a mid-process package install is acceptable - the plugin
    // already caches resolved elements for 30 minutes.
    private readonly ConcurrentDictionary<string, HashSet<string>> _listings =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Reads the JSON content at <paramref name="relativePath"/> (e.g.
    /// <c>"segments/pid.json"</c>) from the context's resolver. Throws
    /// <see cref="FileNotFoundException"/> when the resolver cannot supply the
    /// resource, matching the pre-seam missing-resource semantics callers guard
    /// with <see cref="Exists"/>.
    /// </summary>
    public async Task<string> LoadAsync(
        HL7ResourceContext context,
        string relativePath,
        CancellationToken cancellationToken)
    {
        var portablePath = $"{context.PortableBasePath}/{Normalize(relativePath)}";
        var opened = await context.Resolver.OpenReadAsync(portablePath, cancellationToken);
        if (opened.IsFailure)
            throw new FileNotFoundException($"Reference resource not found: {portablePath} ({opened.Error.Message})");

        using var stream = opened.Value;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// True when the resolver's listing for the path's subfolder contains the
    /// given relative path.
    /// </summary>
    public bool Exists(HL7ResourceContext context, string relativePath)
    {
        var relative = Normalize(relativePath);
        var separator = relative.IndexOf('/');
        if (separator <= 0)
            return false;

        var subfolder = relative[..separator];
        return Listing(context, subfolder).Contains($"{context.PortableBasePath}/{relative}");
    }

    /// <summary>
    /// Enumerates the JSON entries under the given subdirectory as
    /// context-relative paths (e.g. <c>"segments/pid.json"</c>), ordinal-sorted.
    /// Returns empty when no package supplies the subfolder.
    /// </summary>
    public IEnumerable<string> EnumerateFiles(HL7ResourceContext context, string subfolder)
        => Listing(context, subfolder)
            .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(path => path[(context.PortableBasePath.Length + 1)..])
            .OrderBy(path => path, StringComparer.Ordinal);

    private HashSet<string> Listing(HL7ResourceContext context, string subfolder)
        => _listings.GetOrAdd($"{context.PortableBasePath}/{subfolder}", prefix =>
        {
            var listed = context.Resolver.ListResourcePaths(prefix);
            return listed.IsSuccess
                ? new HashSet<string>(listed.Value, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        });

    private static string Normalize(string relativePath)
        => relativePath.Replace('\\', '/');
}
