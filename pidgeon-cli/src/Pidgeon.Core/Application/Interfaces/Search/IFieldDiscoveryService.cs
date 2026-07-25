// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Search;

namespace Pidgeon.Core.Application.Interfaces.Search;

/// <summary>
/// Service for discovering and searching healthcare message fields across standards.
/// Provides semantic search, pattern matching, and cross-standard mapping capabilities.
/// </summary>
public interface IFieldDiscoveryService
{
    /// <summary>
    /// Finds fields by semantic path (e.g., "patient.mrn" -> PID.3, Patient.identifier).
    /// </summary>
    Task<FieldSearchResult> FindBySemanticPathAsync(string semanticPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds fields matching a pattern (wildcard or regex).
    /// </summary>
    Task<FieldSearchResult> FindByPatternAsync(string pattern, PatternType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Maps a field across different healthcare standards.
    /// </summary>
    Task<CrossStandardMapping> MapAcrossStandardsAsync(string fieldPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds fields by semantic meaning or clinical concept.
    /// </summary>
    Task<FieldSearchResult> FindBySemanticAsync(string query, string? context = null, CancellationToken cancellationToken = default);
}