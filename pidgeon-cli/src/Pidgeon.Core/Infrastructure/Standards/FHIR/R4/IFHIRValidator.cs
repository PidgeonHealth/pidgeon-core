// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4;

/// <summary>
/// Validates FHIR R4 resources and bundles for structural compliance.
/// Checks required fields, reference integrity, and code system usage.
/// </summary>
public interface IFHIRValidator
{
    /// <summary>
    /// Validates a single FHIR resource JSON string against R4 rules for the given resource type.
    /// </summary>
    /// <param name="resourceJson">The FHIR resource JSON to validate</param>
    /// <param name="resourceType">The expected FHIR resource type (e.g., "Patient", "Encounter")</param>
    /// <returns>A validation result with any errors found</returns>
    Task<Result<FHIRValidationResult>> ValidateResourceAsync(string resourceJson, string resourceType);

    /// <summary>
    /// Validates a FHIR Bundle JSON string including internal reference integrity.
    /// </summary>
    /// <param name="bundleJson">The FHIR Bundle JSON to validate</param>
    /// <returns>A validation result with any errors found</returns>
    Task<Result<FHIRValidationResult>> ValidateBundleAsync(string bundleJson);

    /// <summary>
    /// Validates a FHIR resource against a StructureDefinition profile.
    /// Supports canonical URLs (e.g., "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"),
    /// aliases (e.g., "us-core"), and file paths to custom profiles.
    /// </summary>
    /// <param name="resourceJson">The FHIR resource JSON to validate</param>
    /// <param name="profileUrlOrPath">Profile canonical URL, alias, or file path</param>
    /// <returns>A conformance result with structured diagnostics</returns>
    Task<Result<Validation.FHIRConformanceResult>> ValidateWithProfileAsync(
        string resourceJson, string profileUrlOrPath);
}

/// <summary>
/// The result of a FHIR validation operation.
/// </summary>
public record FHIRValidationResult
{
    /// <summary>
    /// Gets whether the FHIR resource or bundle passed all validation rules.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets the list of validation errors found during validation.
    /// </summary>
    public IReadOnlyList<FHIRValidationError> Errors { get; init; } = Array.Empty<FHIRValidationError>();

    /// <summary>
    /// Gets the list of validation warnings (non-blocking issues).
    /// </summary>
    public IReadOnlyList<FHIRValidationError> Warnings { get; init; } = Array.Empty<FHIRValidationError>();
}

/// <summary>
/// Represents a single validation error or warning from FHIR validation.
/// </summary>
public record FHIRValidationError
{
    /// <summary>
    /// Gets a machine-readable error code (e.g., "REQUIRED_FIELD_MISSING", "INVALID_REFERENCE").
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the human-readable error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the JSON path where the error was found (e.g., "Patient.name", "Bundle.entry[0].resource.subject").
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Gets the severity of the validation issue.
    /// </summary>
    public FHIRValidationSeverity Severity { get; init; } = FHIRValidationSeverity.Error;

    /// <summary>
    /// Gets what the StructureDefinition expected (e.g., "min: 1", "Type: code"),
    /// when the error originates from oracle-driven structural validation. Null for
    /// presence/reference checks that have no structured expectation.
    /// </summary>
    public string? ExpectedValue { get; init; }

    /// <summary>
    /// Gets the actual value or shape that was found (e.g., "found: 0").
    /// Null for checks that have no structured actual.
    /// </summary>
    public string? ActualValue { get; init; }

    /// <summary>
    /// Gets a remediation hint for the issue. Null when no fix guidance applies.
    /// </summary>
    public string? Suggestion { get; init; }
}

/// <summary>
/// Severity levels for FHIR validation issues.
/// </summary>
public enum FHIRValidationSeverity
{
    Error,
    Warning,
    Information
}
