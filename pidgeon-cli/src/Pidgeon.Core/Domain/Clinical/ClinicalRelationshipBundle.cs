// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Clinical;

/// <summary>
/// A resolved bundle of clinical relationships for a given condition,
/// combining terminology mappings, expected labs, medications, and procedures.
/// </summary>
public record ClinicalRelationshipBundle
{
    public required TerminologyConcept PrimaryCondition { get; init; }
    public IReadOnlyList<TerminologyConcept> SnomedMappings { get; init; } = Array.Empty<TerminologyConcept>();
    public IReadOnlyList<TerminologyConcept> ExpectedLabs { get; init; } = Array.Empty<TerminologyConcept>();
    public IReadOnlyList<TerminologyConcept> ExpectedMedications { get; init; } = Array.Empty<TerminologyConcept>();
    public IReadOnlyList<TerminologyConcept> ExpectedProcedures { get; init; } = Array.Empty<TerminologyConcept>();
    public IReadOnlyList<TerminologyConcept> Comorbidities { get; init; } = Array.Empty<TerminologyConcept>();
}

/// <summary>
/// A single concept in a clinical terminology system (ICD-10, LOINC, SNOMED, RxNorm, CPT).
///
/// <see cref="Form"/> / <see cref="Route"/> are optional formulation hints carried only by
/// medication concepts sourced from the curated relationship data (a condition→drug edge that
/// names the drug's dosage form and administration route). They let the pharmacy order composer
/// emit a coherent RXE-6/RXE-7 for a drug whose form is not inferable from its display alone —
/// an inhaler must not render as an oral tablet. Null for every non-medication concept and for
/// medication sources (UMLS, AI) that do not supply them; the composer falls back to its route/form
/// heuristics when absent.
/// </summary>
public record TerminologyConcept(string System, string Code, string Display)
{
    public string? Form { get; init; }
    public string? Route { get; init; }
}

/// <summary>
/// A directed relationship between two terminology concepts, with confidence scoring.
/// </summary>
public record TerminologyRelationship(
    TerminologyConcept Source,
    TerminologyConcept Target,
    string RelationshipType,
    double Confidence);
