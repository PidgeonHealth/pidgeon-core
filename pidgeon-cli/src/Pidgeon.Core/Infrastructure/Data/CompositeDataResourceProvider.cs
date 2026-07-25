// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Domain.Data;

namespace Pidgeon.Core.Infrastructure.Data;

/// <summary>
/// Composes explicitly registered data providers without naming their assemblies or packages.
/// </summary>
public sealed class CompositeDataResourceProvider : IDataResourceProvider, IDataResourceResolver
{
    private readonly IReadOnlyList<IDataResourceProvider> _providers;

    public CompositeDataResourceProvider(IEnumerable<IDataResourceProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var materializedProviders = providers.ToArray();
        if (materializedProviders.Any(provider => provider is null))
            throw new ArgumentException("Data resource providers cannot contain null entries.", nameof(providers));

        _providers = materializedProviders;
    }

    public IReadOnlyList<DataResourceDescriptor> ListResources() =>
        _providers
            .SelectMany(provider => provider.ListResources())
            .OrderBy(resource => resource.Identifier.Package.Id, StringComparer.Ordinal)
            .ThenBy(resource => resource.Identifier.Package.Version, StringComparer.Ordinal)
            .ThenBy(resource => resource.Identifier.Path, StringComparer.Ordinal)
            .ToArray();

    public Result<IReadOnlyList<string>> ListResourcePaths(string pathPrefix)
    {
        var normalizedPrefix = pathPrefix?.TrimEnd('/');
        if (!DataResourcePath.IsPortable(normalizedPrefix))
            return InvalidPath<IReadOnlyList<string>>(pathPrefix);

        var paths = ListResources()
            .Where(resource =>
                string.Equals(resource.Identifier.Path, normalizedPrefix, StringComparison.Ordinal) ||
                resource.Identifier.Path.StartsWith(normalizedPrefix + "/", StringComparison.Ordinal))
            .Select(resource => resource.Identifier.Path)
            .ToArray();

        var conflict = paths
            .GroupBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (conflict is not null)
        {
            return Result<IReadOnlyList<string>>.Failure(Error.Create(
                "DATA_RESOURCE_CONFLICT",
                $"Multiple installed packages supply '{conflict.Key}'.",
                conflict.Key));
        }

        if (paths.Length == 0)
            return PackageRequired<IReadOnlyList<string>>(normalizedPrefix!);

        return Result<IReadOnlyList<string>>.Success(
            Array.AsReadOnly(paths.OrderBy(path => path, StringComparer.Ordinal).ToArray()));
    }

    public async ValueTask<Result<Stream>> OpenReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!DataResourcePath.IsPortable(path))
            return InvalidPath<Stream>(path);

        var owners = ListResources()
            .Where(resource => string.Equals(resource.Identifier.Path, path, StringComparison.Ordinal))
            .ToArray();

        if (owners.Length == 0)
            return PackageRequired<Stream>(path);

        if (owners.Length > 1)
        {
            return Result<Stream>.Failure(Error.Create(
                "DATA_RESOURCE_CONFLICT",
                $"Multiple installed packages supply '{path}'.",
                path));
        }

        return await OpenReadAsync(owners[0].Identifier, cancellationToken);
    }

    public async ValueTask<Result<Stream>> OpenReadAsync(
        DataResourceIdentifier resource,
        CancellationToken cancellationToken = default)
    {
        var owners = _providers
            .Where(provider => provider.ListResources().Any(candidate => candidate.Identifier == resource))
            .ToArray();

        if (owners.Length == 0)
        {
            return Result<Stream>.Failure(Error.Create(
                "PACKAGE_REQUIRED",
                $"Data package '{resource.Package.Id}@{resource.Package.Version}' is not installed or does not supply '{resource.Path}'.",
                resource.Path));
        }

        if (owners.Length > 1)
        {
            return Result<Stream>.Failure(Error.Create(
                "DATA_RESOURCE_CONFLICT",
                $"Multiple installed providers claim '{resource.Path}' from '{resource.Package.Id}@{resource.Package.Version}'.",
                resource.Path));
        }

        return await owners[0].OpenReadAsync(resource, cancellationToken);
    }

    private static Result<T> InvalidPath<T>(string? path) =>
        Result<T>.Failure(Error.Create(
            "INVALID_DATA_RESOURCE_PATH",
            "Data resource paths must be portable, package-relative paths without traversal segments.",
            path));

    private static Result<T> PackageRequired<T>(string path) =>
        Result<T>.Failure(Error.Create(
            "PACKAGE_REQUIRED",
            $"No installed data package supplies '{path}'.",
            path));
}
