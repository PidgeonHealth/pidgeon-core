// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Data;

/// <summary>
/// Resolves a FHIR IG package's NPM-style dependency tree. Two halves with a
/// deliberate split: installation walks the tree and downloads missing
/// dependencies (network), while load-time resolution only enumerates what is
/// already on disk (offline-pure) — validation must never hit the network.
/// Dependency packages install as <c>fhir-dep-{name}-{version}</c> siblings of
/// the primary package directory, so two versions of the same package can
/// coexist (e.g. PAS pulls two US Core versions).
/// </summary>
public interface IFhirIgDependencyResolver
{
    /// <summary>
    /// Walk the manifest dependency map of an installed package transitively
    /// and fetch each missing dependency into a sibling
    /// <c>fhir-dep-{name}-{version}</c> directory. Per-dependency failures
    /// (network, 404, non-exact version) are logged and reported in the
    /// summary, never failing the call — the primary install already
    /// succeeded, and validation degrades honestly when a dependency is
    /// absent. Fails only when <paramref name="packageInstallDir"/> has no
    /// readable manifest.
    /// <para>
    /// A transitively-pulled dependency that self-declares a license requiring
    /// acceptance goes through the same gate a direct install does: unless
    /// <paramref name="acceptLicense"/> is set, it is not left installed and is
    /// reported in <see cref="DependencyResolutionSummary.LicenseGated"/>.
    /// </para>
    /// </summary>
    Task<Result<DependencyResolutionSummary>> InstallMissingDependenciesAsync(
        string packageInstallDir, bool acceptLicense = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// The transitive closure of this package's dependency directories that
    /// exist on disk, in deterministic order (breadth-first, name#version
    /// ordinal-sorted per level). Missing dependencies are logged and omitted.
    /// Never touches the network.
    /// </summary>
    IReadOnlyList<string> ResolveInstalledDependencyDirs(string packageInstallDir);
}

/// <summary>
/// Outcome counts for a transitive dependency installation walk.
/// <paramref name="LicenseGated"/> lists dependencies (<c>name#version</c>) that
/// self-declared a license requiring acceptance and were refused because
/// <c>--accept-license</c> was not passed — reported honestly rather than
/// silently installed, and never counted as <paramref name="Installed"/>.
/// </summary>
public sealed record DependencyResolutionSummary(
    int Installed, int Skipped, IReadOnlyList<string> Failed, IReadOnlyList<string> LicenseGated);
