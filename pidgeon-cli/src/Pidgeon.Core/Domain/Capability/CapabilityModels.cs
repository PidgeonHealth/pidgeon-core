// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Pidgeon.Core.Domain.Capability;

/// <summary>
/// One row of the capability matrix: what we can honestly claim for a single
/// (standard, type, version) cell, plus the evidence that backs the claim.
/// </summary>
/// <param name="Standard">Canonical standard id, sourced from a plugin's StandardName (e.g. "hl7").</param>
/// <param name="MessageType">The generatable message/resource type (e.g. "ADT^A01", "Patient", "NewRx").</param>
/// <param name="Version">Standard version (e.g. "2.5.1"); null for single-version standards like FHIR R4.</param>
/// <param name="Level">The derived capability tier.</param>
/// <param name="Basis">Human-readable provenance of the level (for audit/display, not for branching).</param>
public sealed record CapabilityCell(
    string Standard,
    string MessageType,
    string? Version,
    CapabilityLevel Level,
    string Basis)
{
    /// <summary>
    /// The generation tier serving this cell, when the standard distinguishes
    /// tiers — FHIR's "clinical" (curated hand-built builder, scenario-coherent
    /// content) vs "structural" (schema-derived synthesizer: valid instance,
    /// minimal population). Sourced from a registered tier source, never
    /// asserted here; null for standards without a tier axis. Kept distinct
    /// from <see cref="Level"/>: the tier names WHICH path generates, the
    /// level names how well-proven that output is — a structural cell must
    /// never read as a clinical-coherence claim, whatever its level.
    /// </summary>
    public string? GenerationTier { get; init; }
}

/// <summary>
/// The derived level plus its provenance for one cell, before the (standard, type, version)
/// coordinates are attached. Returned by the evidence source; the signaling service folds it into
/// a <see cref="CapabilityCell"/>.
/// </summary>
public sealed record CapabilityEvidence(CapabilityLevel Level, string Basis);

/// <summary>
/// One row of the VALIDATE-capability matrix: how strongly we can validate a single artifact
/// type (e.g. a C-CDA document template) for a standard we check but do not generate.
///
/// A distinct axis from <see cref="CapabilityCell"/> (which is generation-rooted). Keeping the
/// validate signal in its own cell type prevents an artifact we only validate from masquerading
/// as something we can generate conformantly.
/// </summary>
/// <param name="Standard">Canonical standard id, sourced from a validate source (e.g. "ccda").</param>
/// <param name="ArtifactType">The validatable artifact identity — for C-CDA, the document template title.</param>
/// <param name="Level">The derived validate-capability tier.</param>
/// <param name="Basis">Human-readable provenance of the level (for audit/display, not for branching).</param>
public sealed record ValidateCapabilityCell(
    string Standard,
    string ArtifactType,
    ValidateCapabilityLevel Level,
    string Basis);

/// <summary>
/// Optional scoping for a capability report. Both filters are case-insensitive and optional;
/// an all-null query describes the full matrix.
/// </summary>
public sealed record CapabilityQuery(
    string? Standard = null,
    string? Version = null);

/// <summary>
/// The full derived capability matrix across both axes. <see cref="Cells"/> is the
/// generation-capability ladder (what we can generate, and how well-validated that output is);
/// <see cref="ValidateCells"/> is the separate validate-capability axis (how strongly we can
/// validate artifacts for standards we check but do not generate, such as C-CDA). The two axes
/// coexist by design — a future generate axis for C-CDA / X12 extends <see cref="Cells"/>
/// without disturbing the validate axis. <see cref="GeneratedAt"/> is supplied by the service
/// that builds the report (through an injected clock) — the domain record takes no clock
/// dependency of its own, which keeps it pure and the timestamp testable.
/// </summary>
public sealed record CapabilityReport(
    IReadOnlyList<CapabilityCell> Cells,
    DateTimeOffset GeneratedAt)
{
    /// <summary>
    /// The validate-capability rows. Additive and optional: defaults to empty so the generate
    /// axis is unaffected, and callers opt in via <c>with { ValidateCells = … }</c> or the
    /// object initializer. Never null.
    /// </summary>
    public IReadOnlyList<ValidateCapabilityCell> ValidateCells { get; init; } =
        Array.Empty<ValidateCapabilityCell>();

    /// <summary>Distinct standards present in the generate matrix, in stable order.</summary>
    public IReadOnlyList<string> Standards =>
        Cells.Select(c => c.Standard)
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
             .ToList();

    /// <summary>Cells for one standard (case-insensitive).</summary>
    public IEnumerable<CapabilityCell> ForStandard(string standard) =>
        Cells.Where(c => string.Equals(c.Standard, standard, StringComparison.OrdinalIgnoreCase));

    /// <summary>Validate-capability rows for one standard, case-insensitive (peer of <see cref="ForStandard"/>).</summary>
    public IEnumerable<ValidateCapabilityCell> ForValidateStandard(string standard) =>
        ValidateCells.Where(c => string.Equals(c.Standard, standard, StringComparison.OrdinalIgnoreCase));

    /// <summary>Distinct standards present in the validate matrix, in stable order.</summary>
    public IReadOnlyList<string> ValidateStandards =>
        ValidateCells.Select(c => c.Standard)
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
             .ToList();
}
