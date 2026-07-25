// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Drives base FHIR R4 validation off the embedded StructureDefinition oracle.
/// Validates a resource against the base StructureDefinition for its type
/// (canonical URL <c>http://hl7.org/fhir/StructureDefinition/{type}</c>) via
/// <see cref="IProfileValidator"/>, producing the structural diagnostics —
/// element cardinality, data type, terminology binding, fixed/pattern — that a
/// presence-only required-field check cannot express.
///
/// Returns an empty list (no-op) when no base SD is embedded for the type, so
/// resource types without an oracle entry keep their required-field-only
/// behavior instead of silently losing validation. The
/// <see cref="FHIRValidator"/> composes this on top of its curated, deliberately
/// stricter-than-base required-field floor.
/// </summary>
public sealed class BaseStructureDefinitionValidator
{
    private readonly IProfileValidator _profileValidator;

    public BaseStructureDefinitionValidator(IProfileValidator profileValidator)
    {
        _profileValidator = profileValidator ?? throw new ArgumentNullException(nameof(profileValidator));
    }

    /// <summary>
    /// Validates <paramref name="resourceJson"/> against the base R4
    /// StructureDefinition for <paramref name="resourceType"/>.
    /// </summary>
    /// <param name="resourceJson">The FHIR resource JSON.</param>
    /// <param name="resourceType">The resource type (e.g., "Patient").</param>
    /// <param name="suppressedPaths">
    /// Element paths the caller's required-field floor already reported. SD
    /// diagnostics at these paths are dropped: an absent element only yields a
    /// min-cardinality diagnostic (type/binding/pattern checks are skipped for
    /// absent elements), so suppressing by path collapses the one overlap without
    /// hiding diagnostics on populated elements elsewhere.
    /// </param>
    public async Task<IReadOnlyList<FHIRValidationError>> ValidateAsync(
        string resourceJson, string resourceType, ISet<string> suppressedPaths)
    {
        var baseUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";

        // The string overload resolves the canonical URL through the loader and
        // returns a config failure when no base SD is embedded for this type.
        var conformance = await _profileValidator.ValidateAsync(resourceJson, baseUrl).ConfigureAwait(false);
        if (conformance.IsFailure)
            return Array.Empty<FHIRValidationError>();

        var results = new List<FHIRValidationError>();
        foreach (var diagnostic in conformance.Value.Diagnostics)
        {
            if (suppressedPaths.Contains(diagnostic.Path))
                continue;

            results.Add(new FHIRValidationError
            {
                Code = MapDiagnosticCode(diagnostic),
                Message = diagnostic.Message,
                Path = diagnostic.Path,
                Severity = diagnostic.Severity switch
                {
                    FHIRDiagnosticSeverity.Error => FHIRValidationSeverity.Error,
                    FHIRDiagnosticSeverity.Warning => FHIRValidationSeverity.Warning,
                    _ => FHIRValidationSeverity.Information,
                },
                ExpectedValue = diagnostic.Expected,
                ActualValue = diagnostic.Actual,
                Suggestion = diagnostic.Fix,
            });
        }

        return results;
    }

    /// <summary>
    /// Maps a StructureDefinition diagnostic to a stable machine-readable code so
    /// downstream consumers can route by failure kind. Delegates the shape
    /// classification to <see cref="FhirDiagnosticCategorizer"/> (shared with the
    /// profile path) and renders the base-path code vocabulary.
    /// </summary>
    private static string MapDiagnosticCode(FHIRDiagnostic diagnostic) =>
        FhirDiagnosticCategorizer.ToBaseCode(FhirDiagnosticCategorizer.Categorize(diagnostic));
}
