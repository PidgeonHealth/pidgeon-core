// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical;

namespace Pidgeon.Core.Application.Interfaces.Clinical;

/// <summary>
/// Single source of truth for clinical relationships across terminology systems.
/// Merges curated YAML rules, UMLS relationships, and other configured sources
/// to produce authoritative bundles for a given condition.
/// </summary>
public interface IClinicalRelationshipGraph
{
    /// <summary>
    /// Resolves a full clinical relationship bundle for the given ICD-10 code.
    /// </summary>
    Task<Result<ClinicalRelationshipBundle>> ResolveAsync(string icd10Code);

    /// <summary>
    /// Resolves a full clinical relationship bundle for the given terminology concept.
    /// </summary>
    Task<Result<ClinicalRelationshipBundle>> ResolveAsync(TerminologyConcept condition);

    /// <summary>
    /// Returns all known relationships for a concept, optionally filtered by relationship type.
    /// </summary>
    Task<Result<IReadOnlyList<TerminologyRelationship>>> GetRelationshipsAsync(
        string system, string code, string? relationshipType = null);
}
