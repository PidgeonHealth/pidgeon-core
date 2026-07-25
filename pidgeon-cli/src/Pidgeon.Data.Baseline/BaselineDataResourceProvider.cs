// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Security.Cryptography;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Domain.Data;
using Pidgeon.Core;

namespace Pidgeon.Data.Baseline;

/// <summary>
/// Owns the explicit, provenance-cleared resource manifest for the public offline baseline.
/// Every embedded resource is an exact entry in the community composition manifest
/// (its logical name IS the served portable path), with license, redistribution,
/// and provenance dispositions cleared before admission.
/// The manifest-reflection here reads THIS assembly's own explicitly admitted
/// resources — the exact sanctioned owner the compile-boundary contract describes;
/// the community banned-substring scan exists to stop Core naming a legacy data
/// assembly, which this is not.
/// </summary>
public sealed class BaselineDataResourceProvider : IDataResourceProvider
{
    // ponytail: descriptors hashed once at first resolve (~tens of ms over the full
    // payload); a precomputed index only if a profiler complains.
    private static readonly Lazy<IReadOnlyList<DataResourceDescriptor>> Resources =
        new(BuildDescriptors);

    private static DataPackageIdentity PackageIdentity => new(
        "Pidgeon.Data.Baseline",
        typeof(BaselineDataResourceProvider).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0");

    public IReadOnlyList<DataResourceDescriptor> ListResources() => Resources.Value;

    public ValueTask<Result<Stream>> OpenReadAsync(
        DataResourceIdentifier resource,
        CancellationToken cancellationToken = default)
    {
        if (resource.Package != PackageIdentity)
        {
            return ValueTask.FromResult(Result<Stream>.Failure(Error.Create(
                "PACKAGE_REQUIRED",
                $"The installed public baseline does not supply '{resource.Path}'.",
                resource.Path)));
        }

        var stream = typeof(BaselineDataResourceProvider).Assembly
            .GetManifestResourceStream(resource.Path);
        return ValueTask.FromResult(stream is null
            ? Result<Stream>.Failure(Error.Create(
                "PACKAGE_REQUIRED",
                $"The installed public baseline does not supply '{resource.Path}'.",
                resource.Path))
            : Result<Stream>.Success(stream));
    }

    private static IReadOnlyList<DataResourceDescriptor> BuildDescriptors()
    {
        var assembly = typeof(BaselineDataResourceProvider).Assembly;
        var package = PackageIdentity;
        var descriptors = new List<DataResourceDescriptor>();

        foreach (var name in assembly.GetManifestResourceNames()
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Manifest names and streams disagree for '{name}'.");
            descriptors.Add(new DataResourceDescriptor(
                new DataResourceIdentifier(package, name),
                MediaTypeFor(name),
                stream.Length,
                Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()));
        }

        return descriptors;
    }

    private static string MediaTypeFor(string path) => System.IO.Path.GetExtension(path) switch
    {
        ".json" => "application/json",
        ".yaml" or ".yml" => "application/x-yaml",
        _ => "application/octet-stream",
    };
}
