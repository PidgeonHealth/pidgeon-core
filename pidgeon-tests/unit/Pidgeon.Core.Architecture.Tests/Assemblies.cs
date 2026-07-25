// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

namespace Pidgeon.Core.Architecture.Tests;

/// <summary>
/// Resolves the single assembly the public Core-invariant architecture suite
/// reasons about. This project references only Pidgeon.Core (Data arrives
/// transitively) — the cross-product and module-gating rules that need the
/// product/host assemblies stay in the private Pidgeon.Architecture.Tests suite.
/// Marker-type resolution survives renames and surfaces load failures as
/// compile errors instead of silent zero-match passes.
/// </summary>
internal static class Assemblies
{
    // The marker type must exist in BOTH Core compositions: the monorepo's commercial
    // build and the public mirror's community build (which excludes the commercial DI
    // registration extensions). StandardNames is a domain constant type admitted to the
    // community compile manifest, so the same suite runs unchanged in either tree.
    public static Assembly Core { get; } =
        typeof(Pidgeon.Core.Domain.Messaging.StandardNames).Assembly;
}
