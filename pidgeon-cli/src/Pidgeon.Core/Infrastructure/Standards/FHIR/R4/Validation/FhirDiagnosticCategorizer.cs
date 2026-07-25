// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// The kind of profile / StructureDefinition conformance failure a
/// <see cref="FHIRDiagnostic"/> represents. Derived from the structured
/// Expected/Message shapes the R4 element, binding, invariant, and slice
/// evaluators emit.
/// </summary>
public enum FhirDiagnosticCategory
{
    CardinalityMin,
    CardinalityMax,
    InvalidType,
    Binding,
    BindingNotValidated,
    FixedValue,
    Pattern,
    Format,
    Invariant,
    InvariantNotEvaluated,
    Slicing,
    StructureDefinition,
}

/// <summary>
/// Classifies a FHIR conformance <see cref="FHIRDiagnostic"/> into a stable
/// <see cref="FhirDiagnosticCategory"/> off its structured Expected/Message
/// fields, so both validation paths route by failure kind from one classifier
/// instead of two copies that could drift:
/// <list type="bullet">
///   <item>the base-SD path (<see cref="BaseStructureDefinitionValidator"/>)
///   maps the category to its <c>CARDINALITY_MIN</c>-style machine codes;</item>
///   <item>the profile path (FHIRResultTranslator) maps it to the
///   <c>FHIR-PROFILE-*</c> rule ids that <c>conform --ci</c> routes on.</item>
/// </list>
/// Must-support gaps are NOT classified here — the translator stamps their
/// <c>MUSTSUPPORT-</c> rule id off <see cref="FHIRDiagnostic.IsMustSupportGap"/>
/// before reaching this classifier.
/// </summary>
public static class FhirDiagnosticCategorizer
{
    /// <summary>
    /// Classifies a diagnostic by the Expected prefix it carries, falling back
    /// to Message substrings for fixed/pattern/format (whose Expected value is
    /// the raw offending value rather than a routable prefix), then to
    /// <see cref="FhirDiagnosticCategory.StructureDefinition"/>.
    /// </summary>
    public static FhirDiagnosticCategory Categorize(FHIRDiagnostic diagnostic)
    {
        if (diagnostic.Expected is { } expected)
        {
            if (expected.StartsWith("min:", StringComparison.Ordinal)) return FhirDiagnosticCategory.CardinalityMin;
            if (expected.StartsWith("max:", StringComparison.Ordinal)) return FhirDiagnosticCategory.CardinalityMax;
            if (expected.StartsWith("Type:", StringComparison.Ordinal)) return FhirDiagnosticCategory.InvalidType;
            if (expected.StartsWith("A code from", StringComparison.Ordinal)) return FhirDiagnosticCategory.Binding;
            if (expected.StartsWith("Binding not validated", StringComparison.Ordinal)) return FhirDiagnosticCategory.BindingNotValidated;
            if (expected.StartsWith("Invariant not evaluated:", StringComparison.Ordinal)) return FhirDiagnosticCategory.InvariantNotEvaluated;
            if (expected.StartsWith("Invariant:", StringComparison.Ordinal)) return FhirDiagnosticCategory.Invariant;
            if (expected.StartsWith("Slice ", StringComparison.Ordinal)) return FhirDiagnosticCategory.Slicing;
        }

        if (diagnostic.Message.Contains("fixed value", StringComparison.OrdinalIgnoreCase)) return FhirDiagnosticCategory.FixedValue;
        if (diagnostic.Message.Contains("pattern value", StringComparison.OrdinalIgnoreCase)) return FhirDiagnosticCategory.Pattern;
        if (diagnostic.Message.Contains("date format", StringComparison.OrdinalIgnoreCase)) return FhirDiagnosticCategory.Format;

        return FhirDiagnosticCategory.StructureDefinition;
    }

    /// <summary>
    /// The <c>FHIR-PROFILE-*</c> rule id the profile-validation path stamps for a
    /// category. Cardinality min and max collapse to one <c>FHIR-PROFILE-CARDINALITY</c>
    /// id; the fallback keeps the bare <c>FHIR-PROFILE</c> id that predates the taxonomy.
    /// </summary>
    public static string ToProfileRuleId(FhirDiagnosticCategory category) => category switch
    {
        FhirDiagnosticCategory.CardinalityMin => "FHIR-PROFILE-CARDINALITY",
        FhirDiagnosticCategory.CardinalityMax => "FHIR-PROFILE-CARDINALITY",
        FhirDiagnosticCategory.InvalidType => "FHIR-PROFILE-TYPE",
        FhirDiagnosticCategory.Binding => "FHIR-PROFILE-BINDING",
        FhirDiagnosticCategory.BindingNotValidated => "FHIR-PROFILE-BINDING-NOT-VALIDATED",
        FhirDiagnosticCategory.FixedValue => "FHIR-PROFILE-FIXED",
        FhirDiagnosticCategory.Pattern => "FHIR-PROFILE-PATTERN",
        FhirDiagnosticCategory.Format => "FHIR-PROFILE-FORMAT",
        FhirDiagnosticCategory.Invariant => "FHIR-PROFILE-INVARIANT",
        FhirDiagnosticCategory.InvariantNotEvaluated => "FHIR-PROFILE-INVARIANT-NOT-EVALUATED",
        FhirDiagnosticCategory.Slicing => "FHIR-PROFILE-SLICING",
        _ => "FHIR-PROFILE",
    };

    /// <summary>
    /// The base-StructureDefinition machine code the base-SD validation path
    /// emits for a category. Preserved byte-for-byte from the prior inline
    /// mapping so its existing consumers keep routing unchanged; slicing (which
    /// the base path never produces) resolves to the same fallback code.
    /// </summary>
    public static string ToBaseCode(FhirDiagnosticCategory category) => category switch
    {
        FhirDiagnosticCategory.CardinalityMin => "CARDINALITY_MIN",
        FhirDiagnosticCategory.CardinalityMax => "CARDINALITY_MAX",
        FhirDiagnosticCategory.InvalidType => "INVALID_TYPE",
        FhirDiagnosticCategory.Binding => "INVALID_CODE",
        FhirDiagnosticCategory.BindingNotValidated => "BINDING_NOT_VALIDATED",
        FhirDiagnosticCategory.FixedValue => "FIXED_VALUE_MISMATCH",
        FhirDiagnosticCategory.Pattern => "PATTERN_MISMATCH",
        FhirDiagnosticCategory.Format => "INVALID_FORMAT",
        FhirDiagnosticCategory.Invariant => "INVARIANT",
        FhirDiagnosticCategory.InvariantNotEvaluated => "INVARIANT_NOT_EVALUATED",
        FhirDiagnosticCategory.Slicing => "STRUCTURE_DEFINITION",
        _ => "STRUCTURE_DEFINITION",
    };
}
