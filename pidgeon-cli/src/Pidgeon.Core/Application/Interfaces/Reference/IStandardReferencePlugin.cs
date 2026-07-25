// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core;
using Pidgeon.Core.Domain.Reference.Entities;

namespace Pidgeon.Core.Application.Interfaces.Reference;

/// <summary>
/// Plugin interface for standard-specific reference implementations.
/// Allows each healthcare standard to provide its own lookup logic and data sources.
/// </summary>
public interface IStandardReferencePlugin
{
    /// <summary>
    /// Gets the standard identifier this plugin handles (e.g., "hl7v23", "fhir-r4").
    /// </summary>
    string StandardIdentifier { get; }

    /// <summary>
    /// Gets the human-readable standard name (e.g., "HL7 v2.3", "FHIR R4").
    /// </summary>
    string StandardName { get; }

    /// <summary>
    /// Gets the version of the standard this plugin supports.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Determines if this plugin can handle the given path format.
    /// Used for smart inference of standard from element paths.
    /// </summary>
    /// <param name="path">Element path to evaluate</param>
    /// <returns>True if this plugin can handle the path format</returns>
    bool CanHandle(string path);

    /// <summary>
    /// Gets the confidence score for handling a given path.
    /// Higher scores indicate better fit (0.0 - 1.0).
    /// </summary>
    /// <param name="path">Element path to evaluate</param>
    /// <returns>Confidence score from 0.0 (no match) to 1.0 (perfect match)</returns>
    double GetConfidence(string path);

    /// <summary>
    /// Looks up a specific element by path within this standard.
    /// </summary>
    /// <param name="path">Element path to lookup</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Standard element if found</returns>
    Task<Result<StandardElement>> LookupAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for elements within this standard matching the query.
    /// </summary>
    /// <param name="query">Search query string</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching elements ranked by relevance</returns>
    Task<Result<IReadOnlyList<StandardElement>>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all child elements for a given parent path within this standard.
    /// </summary>
    /// <param name="parentPath">Parent element path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of child elements</returns>
    Task<Result<IReadOnlyList<StandardElement>>> ListChildrenAsync(string parentPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all top-level elements in this standard (segments, resources, etc.).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of top-level elements</returns>
    Task<Result<IReadOnlyList<StandardElement>>> ListTopLevelElementsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that an element path is well-formed for this standard.
    /// </summary>
    /// <param name="path">Element path to validate</param>
    /// <returns>Success if valid, failure with error details</returns>
    Result ValidatePath(string path);

    /// <summary>
    /// Gets suggestions for similar or related paths when a lookup fails.
    /// Helps with typos and provides alternative options.
    /// </summary>
    /// <param name="path">Original path that failed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of suggested alternative paths</returns>
    Task<Result<IReadOnlyList<string>>> GetSuggestionsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets vendor-specific variations for an element.
    /// This is where our competitive advantage lies - practical implementation knowledge.
    /// </summary>
    /// <param name="path">Element path</param>
    /// <param name="vendor">Vendor identifier (epic, cerner, etc.)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Vendor-specific element information</returns>
    Task<Result<StandardElement>> GetVendorVariationAsync(string path, string vendor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Extended interface for plugins that support caching and data loading strategies.
/// Allows plugins to implement efficient data loading and memory management.
/// </summary>
public interface IAdvancedStandardReferencePlugin : IStandardReferencePlugin
{
    /// <summary>
    /// Initializes the plugin and loads any required data.
    /// Called during service startup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success if initialization completed</returns>
    Task<Result> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Preloads frequently accessed elements into memory for faster lookup.
    /// Called with commonly used paths based on usage analytics.
    /// </summary>
    /// <param name="paths">Paths to preload</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success if preloading completed</returns>
    Task<Result> PreloadElementsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets statistics about the plugin's data coverage and performance.
    /// Used for monitoring and optimization.
    /// </summary>
    /// <returns>Plugin statistics</returns>
    ReferencePluginStatistics GetStatistics();

    /// <summary>
    /// Clears any cached data to free memory.
    /// Called during memory pressure or explicit cache management.
    /// </summary>
    void ClearCache();
}

/// <summary>
/// Statistics about a reference plugin's data and performance.
/// </summary>
public record ReferencePluginStatistics
{
    /// <summary>
    /// Total number of elements available in this plugin.
    /// </summary>
    public int TotalElements { get; init; }

    /// <summary>
    /// Number of elements currently loaded in memory.
    /// </summary>
    public int LoadedElements { get; init; }

    /// <summary>
    /// Memory usage in bytes for cached elements.
    /// </summary>
    public long MemoryUsage { get; init; }

    /// <summary>
    /// Average lookup time in milliseconds.
    /// </summary>
    public double AverageLookupTime { get; init; }

    /// <summary>
    /// Cache hit rate as a percentage (0-100).
    /// </summary>
    public double CacheHitRate { get; init; }

    /// <summary>
    /// Number of successful lookups performed.
    /// </summary>
    public long SuccessfulLookups { get; init; }

    /// <summary>
    /// Number of failed lookups (element not found).
    /// </summary>
    public long FailedLookups { get; init; }
}