// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Data;

/// <summary>
/// Identifies one immutable version of an installed data package.
/// </summary>
public readonly record struct DataPackageIdentity(string Id, string Version);

/// <summary>
/// Identifies a resource by its owning package and portable package-relative path.
/// </summary>
public readonly record struct DataResourceIdentifier(
    DataPackageIdentity Package,
    string Path);

/// <summary>
/// Describes an installed resource without exposing its storage mechanism or assembly.
/// </summary>
public sealed record DataResourceDescriptor(
    DataResourceIdentifier Identifier,
    string MediaType,
    long Length,
    string Sha256);
