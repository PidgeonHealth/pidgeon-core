// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Data;

/// <summary>
/// Resolves portable package-relative resource paths for engine services.
/// Package identity, storage, and assembly details remain owned by registered
/// <see cref="IDataResourceProvider"/> implementations.
/// </summary>
public interface IDataResourceResolver
{
    /// <summary>
    /// Lists installed resource paths beneath a portable package-relative prefix.
    /// Missing ownership returns <c>PACKAGE_REQUIRED</c>; ambiguous ownership returns
    /// <c>DATA_RESOURCE_CONFLICT</c>.
    /// </summary>
    Result<IReadOnlyList<string>> ListResourcePaths(string pathPrefix);

    /// <summary>
    /// Opens the single installed resource that owns a portable package-relative path.
    /// Missing ownership returns <c>PACKAGE_REQUIRED</c>; ambiguous ownership returns
    /// <c>DATA_RESOURCE_CONFLICT</c>.
    /// </summary>
    ValueTask<Result<Stream>> OpenReadAsync(
        string path,
        CancellationToken cancellationToken = default);
}
