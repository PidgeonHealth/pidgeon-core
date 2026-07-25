// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Data;

namespace Pidgeon.Core.Application.Interfaces.Data;

/// <summary>
/// Enumerates and opens installed standards and data resources without naming their assembly.
/// </summary>
public interface IDataResourceProvider
{
    IReadOnlyList<DataResourceDescriptor> ListResources();

    /// <summary>
    /// Opens an exact installed resource. Missing package/resource ownership returns
    /// <c>PACKAGE_REQUIRED</c>; conflicting ownership returns <c>DATA_RESOURCE_CONFLICT</c>.
    /// </summary>
    ValueTask<Result<Stream>> OpenReadAsync(
        DataResourceIdentifier resource,
        CancellationToken cancellationToken = default);
}
