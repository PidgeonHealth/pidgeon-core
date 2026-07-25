// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Artifacts;

/// <summary>
/// Local artifact discovery (ARTIFACT_ECOSYSTEM_PROGRAM.md §5.3, the LOCAL subset): one search
/// service over the artifact sources composed on this machine. The public composition exposes the
/// data-package registry; the private composition also adds starter and installed recipes. The CLI (<c>pidgeon artifacts</c>),
/// the Bridge (<c>GET /api/artifacts/search</c>), and the MCP <c>search_artifacts</c> tool are all
/// projections of this one operation (AGENTIC_HARNESS_STANDARD.md H-1); the hosted artifact hub
/// plugs in behind the same seam later. Read-only and never account-gated.
/// </summary>
public interface IArtifactCatalogService
{
    /// <summary>
    /// Searches the local artifact sources. An empty <see cref="ArtifactSearchQuery.Query"/> lists
    /// everything the facet filters admit. Failures are validation-shaped (unknown kind,
    /// contradictory installed/available flags, non-positive limit) — enumeration problems in a
    /// single source degrade to a smaller result, never a failure.
    /// </summary>
    Task<Result<ArtifactSearchResult>> SearchArtifactsAsync(
        ArtifactSearchQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a single artifact by its content-addressed id (a recipe_id) or package name against the same
    /// three local sources, returning the projected item — or null when nothing local carries the id. Unlike
    /// <see cref="SearchArtifactsAsync"/> this is a reverse lookup (recipe_id is recorded but not indexed for
    /// lookup today), the one primitive engagement-pack hydration needs to answer "is this reference already
    /// installed, available locally, or missing?" for each pack reference. Offline: no network fetch, ever.
    /// </summary>
    Task<ArtifactSearchItem?> TryResolveByIdAsync(string id, CancellationToken cancellationToken = default);

}

/// <summary>The artifact kinds the discovery surface understands.</summary>
public static class ArtifactKinds
{
    /// <summary>A redacted vendor-profile recipe (<c>RecipeGuards.VendorProfileKind</c>).</summary>
    public const string VendorProfile = "vendor-profile";

    /// <summary>A generation-config recipe (<c>RecipeGuards.GenerationConfigKind</c>).</summary>
    public const string GenerationConfig = "generation-config";

    /// <summary>A workflow recipe (<c>RecipeGuards.WorkflowKind</c>, format 2).</summary>
    public const string Workflow = "workflow";

    /// <summary>A conform-endpoint set recipe (<c>RecipeGuards.ConformEndpointKind</c>, format 2).</summary>
    public const string ConformEndpoint = "conform-endpoint";

    /// <summary>A flock population-config recipe (<c>RecipeGuards.FlockPopulationConfigKind</c>, format 2).</summary>
    public const string FlockPopulationConfig = "flock-population-config";

    /// <summary>An engagement-pack recipe (<c>RecipeGuards.EngagementPackKind</c>, format 2).</summary>
    public const string EngagementPack = "engagement-pack";

    /// <summary>An installable data package from the package registry.</summary>
    public const string DataPackage = "data-package";

    /// <summary>True when <paramref name="kind"/> is a kind this surface understands.</summary>
    public static bool IsKnown(string? kind)
        => kind is VendorProfile or GenerationConfig or Workflow or ConformEndpoint or FlockPopulationConfig
            or EngagementPack or DataPackage;
}

/// <summary>Where a discovered artifact came from.</summary>
public static class ArtifactSources
{
    /// <summary>The first-party starter recipe pack embedded in the private data assembly.</summary>
    public const string Starter = "starter";

    /// <summary>A recipe the user installed into <c>~/.pidgeon</c> via the recipe trust gate.</summary>
    public const string Installed = "installed";

    /// <summary>The data-package registry (installed or available).</summary>
    public const string PackageRegistry = "package-registry";
}

/// <summary>
/// One artifact search: an optional free-text query plus facet filters. Free text matches
/// case-insensitively over title, description, name, vendor, and message type; every
/// whitespace-separated term must match at least one field.
/// </summary>
public sealed record ArtifactSearchQuery
{
    /// <summary>Free-text terms. Null or blank lists everything the facets admit.</summary>
    public string? Query { get; init; }

    /// <summary>Restrict to one <see cref="ArtifactKinds"/> value.</summary>
    public string? Kind { get; init; }

    /// <summary>Restrict to artifacts whose vendor facet contains this text (case-insensitive).</summary>
    public string? Vendor { get; init; }

    /// <summary>Restrict to artifacts whose standard facet contains this text (case-insensitive).</summary>
    public string? Standard { get; init; }

    /// <summary>Restrict to artifacts whose message-type facet contains this text (case-insensitive).</summary>
    public string? MessageType { get; init; }

    /// <summary>Only artifacts already installed on this machine.</summary>
    public bool InstalledOnly { get; init; }

    /// <summary>Only artifacts not yet installed. Mutually exclusive with <see cref="InstalledOnly"/>.</summary>
    public bool AvailableOnly { get; init; }

    /// <summary>Maximum items returned. Default 20; values above 100 are clamped to 100.</summary>
    public int Limit { get; init; } = 20;
}

/// <summary>One discovered artifact, projected agent-legibly (every consumer renders these fields).</summary>
public sealed record ArtifactSearchItem
{
    /// <summary>Stable identity: the content-addressed recipe id, or the package name.</summary>
    public required string Id { get; init; }

    /// <summary>The human-usable handle: the recipe file stem or the package name.</summary>
    public required string Name { get; init; }

    /// <summary>An <see cref="ArtifactKinds"/> value.</summary>
    public required string Kind { get; init; }

    /// <summary>Human title (recipe metadata title, or the package name).</summary>
    public required string Title { get; init; }

    /// <summary>Human description; empty when the artifact carries none.</summary>
    public required string Description { get; init; }

    /// <summary>Vendor facet (vendor-profile recipes only).</summary>
    public string? Vendor { get; init; }

    /// <summary>Standard facet (recipes only, e.g. <c>HL7v2</c> / <c>hl7</c>).</summary>
    public string? Standard { get; init; }

    /// <summary>Message-type facet (recipes only, e.g. <c>ADT^A01</c>).</summary>
    public string? MessageType { get; init; }

    /// <summary>An <see cref="ArtifactSources"/> value.</summary>
    public required string Source { get; init; }

    /// <summary>True when the artifact is already installed on this machine.</summary>
    public required bool Installed { get; init; }

    /// <summary>
    /// The exact CLI one-liner for this artifact: the install command when it is not yet
    /// installed (including the two-step hint for a starter recipe whose pack is not yet
    /// materialized), or the activation next-step when it already is (install never activates).
    /// </summary>
    public required string InstallCommand { get; init; }

    /// <summary>One-hop fork lineage: the parent recipe id, when the artifact carried one.</summary>
    public string? ForkedFrom { get; init; }
}

/// <summary>A search outcome: the matching items after deterministic ordering and the limit.</summary>
public sealed record ArtifactSearchResult
{
    /// <summary>Matches before the limit was applied.</summary>
    public required int Total { get; init; }

    /// <summary>True when <see cref="Total"/> exceeded the limit and items were cut.</summary>
    public required bool Truncated { get; init; }

    /// <summary>The returned items: installed first, then title ascending, then id.</summary>
    public required IReadOnlyList<ArtifactSearchItem> Items { get; init; }
}
