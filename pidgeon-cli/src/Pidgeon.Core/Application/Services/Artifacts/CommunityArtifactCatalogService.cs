// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Interfaces.Artifacts;
using Pidgeon.Core.Application.Interfaces.Data;

namespace Pidgeon.Core.Application.Services.Artifacts;

/// <summary>
/// Public artifact catalog over the installable data-package registry. Recipe sources remain in
/// the private composition; this implementation keeps <c>pidgeon artifacts</c> useful and honest
/// without reaching across that boundary.
/// </summary>
internal sealed class CommunityArtifactCatalogService : IArtifactCatalogService
{
    private const int MaxLimit = 100;
    private readonly IDataPackageManager _packageManager;

    public CommunityArtifactCatalogService(IDataPackageManager packageManager)
    {
        _packageManager = packageManager ?? throw new ArgumentNullException(nameof(packageManager));
    }

    public async Task<Result<ArtifactSearchResult>> SearchArtifactsAsync(
        ArtifactSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query is null)
            return Result<ArtifactSearchResult>.Failure(Error.Validation("A search query is required."));
        if (query.Kind is { Length: > 0 } && !ArtifactKinds.IsKnown(query.Kind))
            return Result<ArtifactSearchResult>.Failure(Error.Validation($"Unknown artifact kind '{query.Kind}'."));
        if (query.InstalledOnly && query.AvailableOnly)
            return Result<ArtifactSearchResult>.Failure(Error.Validation(
                "installedOnly and availableOnly are mutually exclusive."));
        if (query.Limit <= 0)
            return Result<ArtifactSearchResult>.Failure(Error.Validation(
                $"limit must be positive (default 20, max {MaxLimit})."));

        cancellationToken.ThrowIfCancellationRequested();
        var items = (await _packageManager.ListPackagesAsync())
            .Select(ProjectPackage)
            .Where(item => MatchesFacets(item, query) && MatchesFreeText(item, query.Query))
            .OrderByDescending(item => item.Installed)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        var limit = Math.Min(query.Limit, MaxLimit);

        return Result<ArtifactSearchResult>.Success(new ArtifactSearchResult
        {
            Total = items.Count,
            Truncated = items.Count > limit,
            Items = items.Take(limit).ToList()
        });
    }

    public async Task<ArtifactSearchItem?> TryResolveByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        var package = (await _packageManager.ListPackagesAsync())
            .FirstOrDefault(candidate => string.Equals(candidate.Name, id, StringComparison.OrdinalIgnoreCase));
        return package is null ? null : ProjectPackage(package);
    }

    private static ArtifactSearchItem ProjectPackage(PackageInfo package) => new()
    {
        Id = package.Name,
        Name = package.Name,
        Kind = ArtifactKinds.DataPackage,
        Title = package.Name,
        Description = package.Description,
        Source = ArtifactSources.PackageRegistry,
        Installed = package.IsInstalled,
        InstallCommand = package.RequiresLicense
            ? $"pidgeon data install {package.Name} --accept-license"
            : $"pidgeon data install {package.Name}"
    };

    private static bool MatchesFacets(ArtifactSearchItem item, ArtifactSearchQuery query)
    {
        if (query.Kind is { Length: > 0 } && !string.Equals(item.Kind, query.Kind, StringComparison.OrdinalIgnoreCase))
            return false;
        if (query.InstalledOnly && !item.Installed)
            return false;
        if (query.AvailableOnly && item.Installed)
            return false;
        if (query.Vendor is { Length: > 0 } && !Contains(item.Vendor, query.Vendor))
            return false;
        if (query.Standard is { Length: > 0 } && !Contains(item.Standard, query.Standard))
            return false;
        if (query.MessageType is { Length: > 0 } && !Contains(item.MessageType, query.MessageType))
            return false;
        return true;
    }

    private static bool MatchesFreeText(ArtifactSearchItem item, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.All(term =>
            Contains(item.Title, term)
            || Contains(item.Description, term)
            || Contains(item.Name, term));
    }

    private static bool Contains(string? haystack, string needle)
        => haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
