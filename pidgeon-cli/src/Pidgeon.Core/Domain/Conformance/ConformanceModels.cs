// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Validation;

namespace Pidgeon.Core.Domain.Conformance;

/// <summary>
/// Provenance stamp for one Implementation Guide a conformance run validated
/// against: the IG key, a human-readable name, and the installed package
/// version. A CMS-0057-F auditor reads a scorecard and asks "which version of
/// the spec did this grade against?" — this is that answer, rendered as
/// "US Core 6.1.0" / "Da Vinci PAS 2.1.0" on the scorecard and folded into the
/// status badge so the artifact is self-describing.
/// </summary>
/// <param name="Key">The IG key, e.g. <c>davinci-pas-2.1</c>.</param>
/// <param name="DisplayName">Human-readable IG name, e.g. <c>Da Vinci PAS</c>.</param>
/// <param name="Version">Installed package version, e.g. <c>2.1.0</c>.</param>
public record IgVersionStamp(string Key, string DisplayName, string Version)
{
    /// <summary>
    /// Version placeholder stamped when the IG's package is NOT installed, so a
    /// scorecard reads "US Core embedded subset" instead of falsely claiming the
    /// full IG version over a hand-trimmed embedded stub (FABLE_CONFORM_AUDIT C13).
    /// </summary>
    public const string EmbeddedSubsetVersion = "embedded subset";

    /// <summary>Compact "Name Version" label, e.g. <c>Da Vinci PAS 2.1.0</c>.</summary>
    public string Label => $"{DisplayName} {Version}";
}

/// <summary>
/// A request to probe a live FHIR endpoint for conformance to a specific
/// profile. One request per (endpoint, resource, profile) tuple — the
/// <see cref="Application.Interfaces.Conformance.IConformanceService"/>
/// orchestrates the read + validate + scorecard-emit sequence.
/// </summary>
/// <param name="BaseUrl">FHIR endpoint base URL, e.g. <c>https://payer.example.com/fhir</c>. Trailing slash optional.</param>
/// <param name="BearerToken">Optional OAuth 2.0 bearer token. SMART on FHIR / CMS-0057-F endpoints typically require authentication; if the endpoint is unauthenticated (staging, internal test) pass null.</param>
/// <param name="ResourceType">FHIR resource type, e.g. <c>Patient</c> or <c>Claim</c>.</param>
/// <param name="ResourceId">Logical ID of the resource to fetch, e.g. <c>example-patient-1</c>.</param>
/// <param name="ProfileName">Short profile name (e.g. <c>us-core-patient</c>) or canonical URL. The conformance service normalizes short names via the existing FHIR validation plugin.</param>
public record ConformanceRequest(
    string BaseUrl,
    string? BearerToken,
    string ResourceType,
    string ResourceId,
    string ProfileName);

/// <summary>
/// The outcome of a single conformance probe. Aggregates the HTTP result,
/// the profile resolution, and the per-issue validation report into one
/// artifact suitable for CI scorecard output or human-readable summary.
/// </summary>
public record ConformanceReport
{
    /// <summary>
    /// Rule-id prefix marking a FHIR must-support compliance gap. The conform
    /// <c>--ci</c> gate treats any Warning whose <see cref="ValidationIssue.RuleId"/>
    /// starts with this prefix as a hard fail (the strictest audit-grade reading
    /// of the IG's must-support obligations; CMS publishes no must-support audit
    /// policy); a day-to-day run keeps it a non-blocking warning. Single source of truth
    /// for the producer (FHIR result translation) and the consumers (the
    /// conform exit-code policy and the HTML scorecard highlighting).
    /// </summary>
    public const string MustSupportRulePrefix = "MUSTSUPPORT-";

    /// <summary>
    /// Rule id marking a probe that validated against an embedded subset stub
    /// rather than the full installed IG package (the owning IG was not
    /// installed). Emitted as a Warning by the conformance service and treated
    /// by the <c>conform --ci</c> gate as a hard fail — audit-grade mode refuses
    /// stub-backed evidence — while a day-to-day run keeps it a visible warning
    /// (FABLE_CONFORM_AUDIT C13). Single source of truth for the producer and
    /// the exit-code consumer.
    /// </summary>
    public const string StubProfileRuleId = "CONFORM_STUB_PROFILE";

    /// <summary>The endpoint URL exactly as requested (no normalization).</summary>
    public required string Endpoint { get; init; }

    /// <summary>The profile requested (short name or canonical URL).</summary>
    public required string ProfileRequested { get; init; }

    /// <summary>The canonical URL the profile resolved to, or null if
    /// resolution failed before validation could run.</summary>
    public string? ProfileResolved { get; init; }

    /// <summary>FHIR resource type the probe targeted.</summary>
    public required string ResourceType { get; init; }

    /// <summary>Resource logical ID the probe targeted.</summary>
    public required string ResourceId { get; init; }

    /// <summary>HTTP status code returned by the endpoint, or null if the
    /// request failed before a response was received (network error, TLS
    /// failure, timeout).</summary>
    public int? HttpStatusCode { get; init; }

    /// <summary>True when the probe completed successfully AND the
    /// returned resource passed profile validation with zero errors.
    /// Warnings (including must-support warnings) do not cause
    /// <see cref="Passed"/> to flip to false; callers can inspect
    /// <see cref="Issues"/> to apply stricter policy.</summary>
    public required bool Passed { get; init; }

    /// <summary>Structured issues from profile validation, plus any
    /// endpoint-level issues (HTTP errors, payload parse failures)
    /// surfaced through the same validation-issue shape so callers have
    /// one thing to iterate.</summary>
    public required IReadOnlyList<ValidationIssue> Issues { get; init; }

    /// <summary>Duration of the probe end-to-end (HTTP + validation).</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>UTC timestamp when the probe completed.</summary>
    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// A request to walk an entire FHIR endpoint — fetch the server's
/// <c>CapabilityStatement</c>, discover every resource type the server
/// claims to support, and probe one representative instance of each
/// against the profile the server itself declared.
/// </summary>
/// <param name="BaseUrl">FHIR endpoint base URL.</param>
/// <param name="BearerToken">Optional OAuth 2.0 bearer token.</param>
/// <param name="IncludedResourceTypes">
/// When non-null and non-empty, only these resource types (e.g. <c>["Patient", "Encounter"]</c>)
/// are walked. When null, every resource type the server declares in its
/// CapabilityStatement is walked. Useful for narrowing a conformance run
/// to the CMS-0057-F-required resources only.
/// </param>
/// <param name="IgFilter">
/// When non-null, only resources whose declared profile starts with this
/// canonical-URL prefix are probed (e.g. <c>"http://hl7.org/fhir/us/core/"</c>
/// to probe US Core profiles only). When null, every declared profile is
/// probed regardless of IG source.
/// </param>
public record EndpointProbeRequest(
    string BaseUrl,
    string? BearerToken,
    IReadOnlyList<string>? IncludedResourceTypes = null,
    string? IgFilter = null);

/// <summary>
/// A single <c>(ResourceType, Profile)</c> tuple extracted from a server's
/// CapabilityStatement. One entry per resource × supportedProfile pairing,
/// so a resource that declares multiple supportedProfiles produces multiple
/// entries (each gets probed independently).
/// </summary>
public record CapabilityStatementEntry
{
    /// <summary>FHIR resource type the server supports (e.g. <c>Patient</c>).</summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// Canonical URL of a profile the server declares conformance to for
    /// <see cref="ResourceType"/>. Sourced from <c>rest.resource[].profile</c>
    /// (the singular required-conformance profile) or <c>rest.resource[].supportedProfile[]</c>
    /// (additional profiles the server supports).
    /// </summary>
    public required string DeclaredProfile { get; init; }

    /// <summary>
    /// True when <see cref="DeclaredProfile"/> came from the CapabilityStatement's
    /// <c>rest.resource[].profile</c> field (the FHIR-required conformance
    /// claim). False when it came from <c>supportedProfile</c> — which is
    /// still a real claim but an optional one.
    /// </summary>
    public required bool IsRequiredProfile { get; init; }
}

/// <summary>
/// Endpoint-wide conformance result. Aggregates one <see cref="ConformanceReport"/>
/// per resource type × profile tuple discovered from the server's
/// CapabilityStatement, plus top-line counts suitable for a scorecard.
/// </summary>
public record EndpointConformanceReport
{
    /// <summary>Endpoint URL exactly as requested (no normalization).</summary>
    public required string Endpoint { get; init; }

    /// <summary>The resource×profile tuples the server itself advertised
    /// in its CapabilityStatement. Useful for reporting resources the
    /// server claims to support but we couldn't probe (e.g. empty
    /// search results).</summary>
    public required IReadOnlyList<CapabilityStatementEntry> DeclaredEntries { get; init; }

    /// <summary>Per-resource probe results. One report per entry the
    /// walker actually attempted.</summary>
    public required IReadOnlyList<ConformanceReport> Reports { get; init; }

    /// <summary>
    /// True when every <see cref="Reports"/> entry is itself
    /// <see cref="ConformanceReport.Passed"/> AND no declared entry was
    /// skipped for a fetchable reason (CapabilityStatement failure,
    /// auth failure). A declared entry that produced zero search
    /// results is counted as skipped-but-acceptable and doesn't fail the
    /// endpoint.
    /// </summary>
    public required bool Passed { get; init; }

    /// <summary>Resource types declared but not probed — typically
    /// because <c>{resourceType}?_count=1</c> returned an empty Bundle.
    /// The CLI surfaces these so operators can provide test fixtures or
    /// explicitly opt out.</summary>
    public required IReadOnlyList<string> SkippedResourceTypes { get; init; }

    /// <summary>True when the walk validated zero resources — nothing was
    /// declared, or every declared type was skipped. A vacuous walk is not
    /// conformance evidence; the exit-code policy and the status badge must
    /// not present it as passing.</summary>
    public bool HasNoCoverage => Reports.Count == 0;

    /// <summary>Total duration of the walk.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>UTC timestamp when the walk completed.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Total Error-severity issues across all per-resource reports.</summary>
    public int TotalErrors => Reports.Sum(r => r.Issues.Count(i => i.Severity == ValidationSeverity.Error));

    /// <summary>Total Warning-severity issues across all per-resource reports
    /// (includes <c>MUSTSUPPORT-*</c> warnings — the CLI <c>--ci</c> flag
    /// flips these into build-failing signals).</summary>
    public int TotalWarnings => Reports.Sum(r => r.Issues.Count(i => i.Severity == ValidationSeverity.Warning));

    /// <summary>Count of per-resource reports that passed their profile check.</summary>
    public int ResourcesPassed => Reports.Count(r => r.Passed);

    /// <summary>Count of per-resource reports that failed their profile check.</summary>
    public int ResourcesFailed => Reports.Count(r => !r.Passed);
}

/// <summary>
/// One event emitted while streaming an endpoint walk
/// (<see cref="Application.Interfaces.Conformance.IConformanceService.ProbeEndpointStreamAsync"/>).
/// The walk yields a <see cref="Report"/> per probed entry and a
/// <see cref="Skipped"/> per declared-but-unseeded resource as they happen, then
/// a terminal <see cref="Summary"/> carrying the coverage-honesty counts.
///
/// <para>
/// Coverage honesty (Declared / Attempted / Skipped) rides through the stream so
/// a GUI can show skipped resources live — they are never silently dropped, which
/// is the failure mode a CMS auditor catches.
/// </para>
/// </summary>
public abstract record ConformanceWalkEvent
{
    private ConformanceWalkEvent() { }

    /// <summary>A per-resource probe completed — carries its full
    /// <see cref="ConformanceReport"/>, identical to one entry of
    /// <see cref="EndpointConformanceReport.Reports"/>.</summary>
    public sealed record Report(ConformanceReport Value) : ConformanceWalkEvent;

    /// <summary>A declared resource type was skipped because its search Bundle
    /// was empty (the server declares conformance but has no seed data). Mirrors
    /// one entry of <see cref="EndpointConformanceReport.SkippedResourceTypes"/>.</summary>
    public sealed record Skipped(string ResourceType) : ConformanceWalkEvent;

    /// <summary>The terminal event: top-line coverage + verdict for the whole
    /// walk. Emitted exactly once, after every <see cref="Report"/> and
    /// <see cref="Skipped"/> event.</summary>
    public sealed record Summary(
        int Declared,
        int Attempted,
        IReadOnlyList<string> SkippedResourceTypes,
        int TotalErrors,
        int TotalWarnings,
        int ResourcesPassed,
        int ResourcesFailed,
        bool Passed) : ConformanceWalkEvent;
}
