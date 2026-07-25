// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Conformance;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Resolves FHIR Implementation Guide profiles delivered as installable data
/// packages. Complements <see cref="IStructureDefinitionLoader"/>: the loader
/// handles the mechanics of parsing and indexing StructureDefinition JSON from
/// anywhere (embedded, file, directory); this service owns the mapping between
/// an IG key (e.g. <c>us-core-6.0</c>) and the package directory on disk, and
/// between a friendly profile short-name (e.g. <c>us-core-patient</c>) and its
/// canonical URL.
///
/// <para>
/// Current-scope IGs:
/// </para>
///
/// <list type="bullet">
///   <item><description><c>us-core-6.0</c> — HL7 US Core STU 6.0 (USCDI v3).</description></item>
///   <item><description><c>davinci-pas-2.1</c> — HL7 Da Vinci Prior Authorization Support 2.1 (drives CMS-0057-F compliance).</description></item>
/// </list>
///
/// <para>
/// Additional IGs follow the same pattern: register the package in the data
/// package registry, add the IG key + canonical base URL to the known-IG map
/// in <see cref="FhirIGService"/>, and install with
/// <c>pidgeon data install &lt;package-name&gt;</c>.
/// </para>
/// </summary>
public interface IFhirIGService
{
    /// <summary>
    /// True when the package for <paramref name="igKey"/> is installed on disk
    /// and its profiles have been loaded (or can be loaded on demand) by the
    /// structure definition loader.
    /// </summary>
    bool IsIGInstalled(string igKey);

    /// <summary>
    /// Load every StructureDefinition and ValueSet in the installed package
    /// directory into the <see cref="IStructureDefinitionLoader"/>'s index.
    /// Idempotent: safe to call multiple times; profiles already indexed are
    /// skipped.
    /// </summary>
    /// <returns>The number of StructureDefinitions added to the index on this
    /// call (new entries only — not a cumulative total).</returns>
    Task<Result<int>> LoadIGAsync(string igKey);

    /// <summary>
    /// Resolve a profile short name (e.g. <c>us-core-patient</c>) against an
    /// IG key to a canonical URL and return the parsed StructureDefinition.
    /// Lazily loads the IG if it is installed but not yet loaded.
    /// </summary>
    /// <remarks>
    /// Short names are matched case-insensitively against the IG's canonical
    /// URL base. For <c>us-core-6.0</c> the base is
    /// <c>http://hl7.org/fhir/us/core/StructureDefinition/</c>, so
    /// <c>us-core-patient</c> → <c>http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient</c>.
    /// </remarks>
    Task<Result<FHIRStructureDefinition>> GetProfileAsync(string igKey, string profileShortName);

    /// <summary>
    /// Enumerate the known IG keys this service can resolve regardless of
    /// whether the corresponding package is installed. Consumers use this to
    /// drive help text and the <c>--profile</c> autocomplete surface.
    /// </summary>
    IReadOnlyList<string> KnownIGKeys { get; }

    /// <summary>
    /// Load every IG whose package is currently installed. Used by the FHIR
    /// validation plugin as a one-shot lazy initializer so a user who runs
    /// <c>pidgeon data install fhir-us-core-6.0</c> and then
    /// <c>pidgeon validate --profile us-core-patient ...</c> picks up the
    /// IG-delivered profile without having to call <see cref="LoadIGAsync"/>
    /// explicitly. Idempotent: IGs already loaded are skipped. Returns the
    /// total number of StructureDefinitions added to the loader index on
    /// this call (0 if every installed IG was already loaded).
    /// </summary>
    Task<Result<int>> EnsureAllInstalledLoadedAsync();

    /// <summary>
    /// Map a set of profile canonical URLs (as they appear in a conformance
    /// report's <c>ProfileResolved</c> / <c>ProfileRequested</c>) to the IGs
    /// that own them, each carrying its installed package version. Drives the
    /// "Validated against US Core 6.1.0, Da Vinci PAS 2.1.0" stamp on a
    /// conformance scorecard and the version label folded into the status badge.
    /// </summary>
    /// <remarks>
    /// A canonical URL is attributed to an IG when it starts with that IG's
    /// canonical base. Profiles matching no known IG (e.g. a server's custom
    /// profile) are ignored — the stamp reports only the IGs Pidgeon graded
    /// against. The result is deduplicated by IG and ordered by display name so
    /// the same run always stamps identically (byte-stable artifact).
    /// </remarks>
    IReadOnlyList<IgVersionStamp> ResolveStampsForProfiles(IEnumerable<string> profileCanonicalUrls);

    /// <summary>
    /// After a conformance probe validated against <paramref name="profileCanonicalUrl"/>,
    /// report the package key to install for full-IG validation IF the profile
    /// that actually loaded was an embedded subset stub (i.e. the owning IG
    /// package is not installed, so the loader fell back to the compiled-in
    /// hand-trimmed stub). Returns <c>null</c> when the profile was backed by a
    /// real installed package, is not a known-IG profile, or was never loaded.
    /// </summary>
    /// <remarks>
    /// Drives the <c>CONFORM_STUB_PROFILE</c> warning so an archival scorecard
    /// never presents subset-stub validation as full-IG conformance
    /// (FABLE_CONFORM_AUDIT C13). Reads the loaded profile's
    /// <see cref="FHIRStructureDefinition.Origin"/> as the source of truth, so it
    /// is precise per-profile rather than per-IG.
    /// </remarks>
    Task<string?> ResolveStubPackageKeyAsync(string profileCanonicalUrl);

    /// <summary>
    /// Resolve a bare profile short name that carries no IG prefix (e.g. Da Vinci's
    /// <c>profile-claim</c> / <c>profile-pas-request-bundle</c>) to the canonical URL
    /// of the first known IG whose loaded index contains it. US Core short names
    /// (<c>us-core-*</c>) are prefix-mapped upstream and never reach here; this covers
    /// profiles whose short name gives no hint of the owning IG.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> when no known IG has the short name indexed — the package
    /// is not installed, or the name is not a profile of any known IG. Requires the
    /// installed packages to have been loaded first (the FHIR validation plugin runs
    /// that bootstrap before it calls this). Without this resolution a bare Da Vinci
    /// short name falls through to the validator as a canonical URL and fails
    /// <c>PROFILE_NOT_FOUND</c> even when the owning package is installed
    /// (FABLE_CONFORM_AUDIT C11).
    /// </remarks>
    Task<string?> ResolveProfileCanonicalAsync(string profileShortName);

    /// <summary>
    /// Human-facing remediation for a profile reference that failed to resolve. When
    /// <paramref name="profileRequested"/> is a canonical URL under a known IG whose
    /// package is not installed, returns the exact <c>pidgeon data install</c> command
    /// for that IG (never a different IG's package). When the owning package IS
    /// installed — so the profile name is wrong, not missing — returns a check-the-name
    /// hint with no install command. For a bare short name that resolved nowhere (its
    /// owning IG is unknowable without the package), returns a hint listing EVERY known
    /// IG package as a candidate — never attributing the name to a single IG. Returns
    /// <c>null</c> only for a canonical/path-shaped reference outside every known IG, so
    /// the caller can fall back to a generic message rather than naming an unrelated
    /// package (FABLE_CONFORM_AUDIT C11 — the misleading "install fhir-us-core" hint
    /// fired for a PAS profile).
    /// </summary>
    string? DescribeUnresolvedProfileHint(string profileRequested);
}
