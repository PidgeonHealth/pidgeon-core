// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core;
using Pidgeon.Core.Domain.Reference.Entities;

namespace Pidgeon.Core.Application.Interfaces.Reference;

/// <summary>
/// Service interface for healthcare standards reference lookup functionality.
/// Provides smart inference, search, and browsing capabilities across all supported standards.
/// </summary>
public interface IStandardReferenceService
{
    /// <summary>
    /// Looks up a specific element by path with smart standard inference.
    /// Automatically detects standard from path format (PID.3.5 → HL7, Patient.identifier → FHIR).
    /// </summary>
    /// <param name="path">Element path to lookup</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Standard element if found, or failure result with suggestions</returns>
    Task<Result<StandardElement>> LookupAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a specific element by path and explicit standard.
    /// Used when smart inference needs to be overridden or for disambiguation.
    /// </summary>
    /// <param name="path">Element path to lookup</param>
    /// <param name="standard">Explicit standard identifier (hl7v23, fhir-r4, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Standard element if found, or failure result</returns>
    Task<Result<StandardElement>> LookupAsync(string path, string standard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches across all standards for elements matching the query.
    /// Supports full-text search on names, descriptions, and examples.
    /// </summary>
    /// <param name="query">Search query string</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching standard elements ranked by relevance</returns>
    Task<Result<IReadOnlyList<StandardElement>>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches within a specific standard for elements matching the query.
    /// </summary>
    /// <param name="query">Search query string</param>
    /// <param name="standard">Standard to search within</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching elements within the specified standard</returns>
    Task<Result<IReadOnlyList<StandardElement>>> SearchAsync(string query, string standard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all child elements for a given parent element.
    /// For example, listing all PID fields or all Patient resource properties.
    /// </summary>
    /// <param name="parentPath">Parent element path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of child elements under the specified parent</returns>
    Task<Result<IReadOnlyList<StandardElement>>> ListChildrenAsync(string parentPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all top-level elements for a standard (segments for HL7, resources for FHIR, etc.).
    /// </summary>
    /// <param name="standard">Standard identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of top-level elements in the standard</returns>
    Task<Result<IReadOnlyList<StandardElement>>> ListTopLevelElementsAsync(string standard, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all supported standards and their versions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary of standard identifiers to version information</returns>
    Task<Result<IReadOnlyDictionary<string, string>>> GetSupportedStandardsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to infer the standard from an element path.
    /// Uses heuristics to determine if a path is HL7, FHIR, NCPDP, etc.
    /// </summary>
    /// <param name="path">Element path to analyze</param>
    /// <returns>Inferred standard identifier, or failure if ambiguous</returns>
    Result<string> InferStandard(string path);

    /// <summary>
    /// Validates that an element path is well-formed for its inferred or specified standard.
    /// </summary>
    /// <param name="path">Element path to validate</param>
    /// <param name="standard">Optional explicit standard</param>
    /// <returns>Success if valid, failure with error details if malformed</returns>
    Result ValidatePath(string path, string? standard = null);
}