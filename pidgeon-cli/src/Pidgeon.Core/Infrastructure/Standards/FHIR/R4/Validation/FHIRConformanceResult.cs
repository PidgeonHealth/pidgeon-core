// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Severity levels for FHIR conformance diagnostics.
/// </summary>
public enum FHIRDiagnosticSeverity
{
    Error,
    Warning,
    Information
}

/// <summary>
/// A single diagnostic from FHIR profile-based validation.
/// Carries structured Expected/Actual/Fix fields for actionable output.
/// </summary>
public record FHIRDiagnostic(
    FHIRDiagnosticSeverity Severity,
    string Path,
    string Message,
    string? Expected = null,
    string? Actual = null,
    string? Fix = null
)
{
    /// <summary>
    /// True when this diagnostic reports an absent FHIR <c>mustSupport</c>
    /// element (a compliance gap emitted as a Warning) — distinct from the
    /// element merely being declared <c>mustSupport</c>. The result translator
    /// maps these to a <c>MUSTSUPPORT-</c>-prefixed rule id so
    /// <c>conform --ci</c> can flip the exit code per CMS-0057-F audit policy;
    /// every other profile diagnostic keeps the generic <c>FHIR-PROFILE</c>
    /// rule id.
    /// </summary>
    public bool IsMustSupportGap { get; init; }
}

/// <summary>
/// Result of validating a FHIR resource against a StructureDefinition profile.
/// </summary>
public record FHIRConformanceResult(
    bool IsValid,
    IReadOnlyList<FHIRDiagnostic> Diagnostics
)
{
    public static FHIRConformanceResult Valid() =>
        new(true, Array.Empty<FHIRDiagnostic>());

    public static FHIRConformanceResult FromDiagnostics(IReadOnlyList<FHIRDiagnostic> diagnostics) =>
        new(!diagnostics.Any(d => d.Severity == FHIRDiagnosticSeverity.Error), diagnostics);
}
