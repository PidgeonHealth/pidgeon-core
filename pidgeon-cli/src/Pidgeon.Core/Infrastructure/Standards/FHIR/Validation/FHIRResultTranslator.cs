// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Conformance;
using Pidgeon.Core.Domain.Validation;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.Validation;

/// <summary>
/// Translates the pre-existing FHIR validation types (<see cref="FHIRValidationResult"/>
/// from base R4 validation, and <see cref="FHIRConformanceResult"/> from profile
/// validation) into the shared <see cref="ValidationResult"/> / <see cref="ValidationIssue"/>
/// surface that HL7 and NCPDP plugins already use.
///
/// Lets the FHIR validation stack plug into
/// <see cref="Application.Interfaces.Standards.IStandardValidationPlugin"/> without
/// forcing downstream consumers (CLI, Bridge, Loft, tests) to know about two
/// parallel result hierarchies.
/// </summary>
internal static class FHIRResultTranslator
{
    /// <summary>
    /// Translate a base-R4 result (from <see cref="IFHIRValidator.ValidateResourceAsync"/>
    /// or <see cref="IFHIRValidator.ValidateBundleAsync"/>) into the shared shape.
    /// </summary>
    public static ValidationResult FromBase(
        FHIRValidationResult source,
        ValidationMode mode,
        string? profile,
        TimeSpan? elapsed = null)
    {
        var issues = new List<ValidationIssue>();

        foreach (var error in source.Errors)
        {
            issues.Add(MapError(error, ValidationSeverity.Error));
        }
        foreach (var warning in source.Warnings)
        {
            issues.Add(MapError(warning, ValidationSeverity.Warning));
        }

        return new ValidationResult
        {
            IsValid = source.IsValid,
            Standard = "fhir",
            Profile = profile,
            Mode = mode,
            Issues = issues,
            Statistics = new ValidationStatistics
            {
                TotalRulesChecked = issues.Count == 0 ? 1 : issues.Count,
                RulesPassed = issues.Count == 0 ? 1 : 0,
                RulesFailed = source.Errors.Count,
                FieldsValidated = issues.Count,
                ValidationTime = elapsed ?? TimeSpan.Zero,
            }
        };
    }

    /// <summary>
    /// Translate a profile-validation result (from
    /// <see cref="IFHIRValidator.ValidateWithProfileAsync"/> or
    /// <see cref="IProfileValidator"/> directly) into the shared shape.
    /// </summary>
    public static ValidationResult FromProfile(
        FHIRConformanceResult source,
        ValidationMode mode,
        string? profile,
        TimeSpan? elapsed = null)
    {
        var issues = source.Diagnostics.Select(MapDiagnostic).ToList();

        int errorCount = issues.Count(i => i.Severity == ValidationSeverity.Error);

        return new ValidationResult
        {
            IsValid = source.IsValid,
            Standard = "fhir",
            Profile = profile,
            Mode = mode,
            Issues = issues,
            Statistics = new ValidationStatistics
            {
                TotalRulesChecked = issues.Count == 0 ? 1 : issues.Count,
                RulesPassed = issues.Count - errorCount,
                RulesFailed = errorCount,
                FieldsValidated = issues.Count,
                ValidationTime = elapsed ?? TimeSpan.Zero,
            }
        };
    }

    private static ValidationIssue MapError(FHIRValidationError error, ValidationSeverity defaultSeverity)
    {
        var severity = error.Severity switch
        {
            FHIRValidationSeverity.Error => ValidationSeverity.Error,
            FHIRValidationSeverity.Warning => ValidationSeverity.Warning,
            FHIRValidationSeverity.Information => ValidationSeverity.Info,
            _ => defaultSeverity,
        };

        return new ValidationIssue
        {
            Location = string.IsNullOrEmpty(error.Path) ? "Resource" : error.Path,
            Severity = severity,
            Message = error.Message,
            RuleId = $"FHIR-{error.Code}",
            ExpectedValue = error.ExpectedValue,
            ActualValue = error.ActualValue,
            Suggestion = error.Suggestion,
        };
    }

    private static ValidationIssue MapDiagnostic(FHIRDiagnostic diagnostic)
    {
        var severity = diagnostic.Severity switch
        {
            FHIRDiagnosticSeverity.Error => ValidationSeverity.Error,
            FHIRDiagnosticSeverity.Warning => ValidationSeverity.Warning,
            FHIRDiagnosticSeverity.Information => ValidationSeverity.Info,
            _ => ValidationSeverity.Info,
        };

        return new ValidationIssue
        {
            Location = string.IsNullOrEmpty(diagnostic.Path) ? "Resource" : diagnostic.Path,
            Severity = severity,
            Message = diagnostic.Message,
            // Must-support gaps carry a MUSTSUPPORT-{path} rule id so the
            // conform --ci gate can flip the exit code on them; every other
            // profile diagnostic routes to a FHIR-PROFILE-* taxonomy id (falling
            // back to the bare FHIR-PROFILE id) so CI consumers can filter by
            // failure kind.
            RuleId = diagnostic.IsMustSupportGap
                ? ConformanceReport.MustSupportRulePrefix
                    + (string.IsNullOrEmpty(diagnostic.Path) ? "Unknown" : diagnostic.Path)
                : FhirDiagnosticCategorizer.ToProfileRuleId(FhirDiagnosticCategorizer.Categorize(diagnostic)),
            ExpectedValue = diagnostic.Expected,
            ActualValue = diagnostic.Actual,
            Suggestion = diagnostic.Fix,
        };
    }
}
