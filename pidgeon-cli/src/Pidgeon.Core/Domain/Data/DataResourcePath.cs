// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Data;

/// <summary>
/// Validates package-relative resource paths shared by code and data packages.
/// Static by design: a pure, stateless predicate over its input (the sanctioned
/// static carve-out for pure functions), never a swappable service.
/// </summary>
public static class DataResourcePath
{
    public static bool IsPortable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Path.IsPathRooted(path) ||
            path.Contains('\\') ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.EndsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        return path.Split('/').All(segment => segment is not ("" or "." or ".."));
    }
}
