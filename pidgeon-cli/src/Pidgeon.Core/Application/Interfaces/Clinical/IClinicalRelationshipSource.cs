// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical;

namespace Pidgeon.Core.Application.Interfaces.Clinical;

/// <summary>
/// Pluggable data source for clinical relationships.
/// Layered by priority: curated YAML (0) -> UMLS packages (10) -> AI enrichment (100).
/// </summary>
public interface IClinicalRelationshipSource
{
    string Name { get; }

    /// <summary>
    /// Priority for merging. Lower = higher priority. Curated=0, UMLS=10, AI=100.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Gets all relationships originating from the given code in the given system.
    /// </summary>
    Task<Result<IReadOnlyList<TerminologyRelationship>>> GetRelationshipsAsync(
        string system, string code);
}
