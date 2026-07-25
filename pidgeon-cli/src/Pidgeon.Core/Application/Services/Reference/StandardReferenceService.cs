// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Domain.Reference.Entities;

namespace Pidgeon.Core.Application.Services.Reference;

/// <summary>
/// Core service for healthcare standards reference lookup functionality.
/// Orchestrates multiple standard plugins to provide unified lookup, search, and browse capabilities.
/// </summary>
public class StandardReferenceService : IStandardReferenceService
{
    private readonly IEnumerable<IStandardReferencePlugin> _plugins;
    private readonly ILogger<StandardReferenceService> _logger;

    public StandardReferenceService(
        IEnumerable<IStandardReferencePlugin> plugins,
        ILogger<StandardReferenceService> logger)
    {
        _plugins = plugins;
        _logger = logger;
    }

    public async Task<Result<StandardElement>> LookupAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<StandardElement>.Failure("Path cannot be empty");
        }

        var standardResult = InferStandard(path);
        if (standardResult.IsFailure)
        {
            return Result<StandardElement>.Failure($"Could not infer standard from path '{path}': {standardResult.Error.Message}");
        }

        return await LookupAsync(path, standardResult.Value, cancellationToken);
    }

    public async Task<Result<StandardElement>> LookupAsync(string path, string standard, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<StandardElement>.Failure("Path cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(standard))
        {
            return Result<StandardElement>.Failure("Standard cannot be empty");
        }

        var plugin = GetPluginForStandard(standard);
        if (plugin == null)
        {
            var supportedStandards = string.Join(", ", _plugins.Select(p => p.StandardIdentifier));
            return Result<StandardElement>.Failure($"Unsupported standard '{standard}'. Supported standards: {supportedStandards}");
        }

        try
        {
            var result = await plugin.LookupAsync(path, cancellationToken);
            if (result.IsFailure)
            {
                // Try to provide helpful suggestions
                var suggestionsResult = await plugin.GetSuggestionsAsync(path, cancellationToken);
                if (suggestionsResult.IsSuccess && suggestionsResult.Value.Any())
                {
                    var suggestions = string.Join(", ", suggestionsResult.Value.Take(3));
                    return Result<StandardElement>.Failure($"Element '{path}' not found in {plugin.StandardName}. Did you mean: {suggestions}?");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up element {Path} in standard {Standard}", path, standard);
            return Result<StandardElement>.Failure($"Lookup failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<StandardElement>>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<IReadOnlyList<StandardElement>>.Failure("Query cannot be empty");
        }

        var allResults = new List<StandardElement>();

        foreach (var plugin in _plugins)
        {
            try
            {
                var result = await plugin.SearchAsync(query, cancellationToken);
                if (result.IsSuccess)
                {
                    allResults.AddRange(result.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching in plugin {Standard}", plugin.StandardIdentifier);
                // Continue with other plugins
            }
        }

        // Sort by relevance (simple scoring for now)
        var sortedResults = allResults
            .OrderByDescending(e => CalculateRelevanceScore(e, query))
            .ThenBy(e => e.Standard)
            .ThenBy(e => e.Path)
            .ToList();

        return Result<IReadOnlyList<StandardElement>>.Success(sortedResults);
    }

    public async Task<Result<IReadOnlyList<StandardElement>>> SearchAsync(string query, string standard, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<IReadOnlyList<StandardElement>>.Failure("Query cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(standard))
        {
            return Result<IReadOnlyList<StandardElement>>.Failure("Standard cannot be empty");
        }

        var plugin = GetPluginForStandard(standard);
        if (plugin == null)
        {
            var supportedStandards = string.Join(", ", _plugins.Select(p => p.StandardIdentifier));
            return Result<IReadOnlyList<StandardElement>>.Failure($"Unsupported standard '{standard}'. Supported standards: {supportedStandards}");
        }

        try
        {
            return await plugin.SearchAsync(query, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for '{Query}' in standard {Standard}", query, standard);
            return Result<IReadOnlyList<StandardElement>>.Failure($"Search failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<StandardElement>>> ListChildrenAsync(string parentPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return Result<IReadOnlyList<StandardElement>>.Failure("Parent path cannot be empty");
        }

        var standardResult = InferStandard(parentPath);
        if (standardResult.IsFailure)
        {
            return Result<IReadOnlyList<StandardElement>>.Failure($"Could not infer standard from path '{parentPath}': {standardResult.Error.Message}");
        }

        var plugin = GetPluginForStandard(standardResult.Value);
        if (plugin == null)
        {
            return Result<IReadOnlyList<StandardElement>>.Failure($"No plugin found for standard '{standardResult.Value}'");
        }

        try
        {
            return await plugin.ListChildrenAsync(parentPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing children for {ParentPath}", parentPath);
            return Result<IReadOnlyList<StandardElement>>.Failure($"List children failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<StandardElement>>> ListTopLevelElementsAsync(string standard, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(standard))
        {
            return Result<IReadOnlyList<StandardElement>>.Failure("Standard cannot be empty");
        }

        var plugin = GetPluginForStandard(standard);
        if (plugin == null)
        {
            var supportedStandards = string.Join(", ", _plugins.Select(p => p.StandardIdentifier));
            return Result<IReadOnlyList<StandardElement>>.Failure($"Unsupported standard '{standard}'. Supported standards: {supportedStandards}");
        }

        try
        {
            return await plugin.ListTopLevelElementsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing top-level elements for standard {Standard}", standard);
            return Result<IReadOnlyList<StandardElement>>.Failure($"List top-level elements failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> GetSupportedStandardsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        
        var standards = _plugins.ToDictionary(
            plugin => plugin.StandardIdentifier,
            plugin => $"{plugin.StandardName} ({plugin.Version})"
        );

        return Result<IReadOnlyDictionary<string, string>>.Success(standards);
    }

    public Result<string> InferStandard(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<string>.Failure("Path cannot be empty");
        }

        var candidates = new List<(IStandardReferencePlugin Plugin, double Confidence)>();

        foreach (var plugin in _plugins)
        {
            if (plugin.CanHandle(path))
            {
                var confidence = plugin.GetConfidence(path);
                candidates.Add((plugin, confidence));
            }
        }

        if (!candidates.Any())
        {
            return Result<string>.Failure($"No plugin can handle path format '{path}'. Supported formats: HL7 (PID.3.5), FHIR (Patient.identifier), NCPDP (NewRx.Patient)");
        }

        var bestMatch = candidates.OrderByDescending(c => c.Confidence).First();
        
        // If confidence is too low, we're not sure
        if (bestMatch.Confidence < 0.5)
        {
            var possibleStandards = string.Join(", ", candidates.Select(c => c.Plugin.StandardIdentifier));
            return Result<string>.Failure($"Ambiguous path format '{path}'. Could be: {possibleStandards}. Please specify standard explicitly.");
        }

        return Result<string>.Success(bestMatch.Plugin.StandardIdentifier);
    }

    public Result ValidatePath(string path, string? standard = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result.Failure("Path cannot be empty");
        }

        if (standard != null)
        {
            var plugin = GetPluginForStandard(standard);
            if (plugin == null)
            {
                return Result.Failure($"Unsupported standard '{standard}'");
            }

            return plugin.ValidatePath(path);
        }

        // If no standard specified, try to infer and validate
        var standardResult = InferStandard(path);
        if (standardResult.IsFailure)
        {
            return Result.Failure($"Could not validate path: {standardResult.Error.Message}");
        }

        return ValidatePath(path, standardResult.Value);
    }

    private IStandardReferencePlugin? GetPluginForStandard(string standard)
    {
        return _plugins.FirstOrDefault(p => 
            string.Equals(p.StandardIdentifier, standard, StringComparison.OrdinalIgnoreCase));
    }

    private static double CalculateRelevanceScore(StandardElement element, string query)
    {
        var score = 0.0;
        var queryLower = query.ToLowerInvariant();

        // Exact path match gets highest score
        if (string.Equals(element.Path, query, StringComparison.OrdinalIgnoreCase))
        {
            return 100.0;
        }

        // Name exact match
        if (string.Equals(element.Name, query, StringComparison.OrdinalIgnoreCase))
        {
            score += 50.0;
        }

        // Path contains query
        if (element.Path.ToLowerInvariant().Contains(queryLower))
        {
            score += 20.0;
        }

        // Name contains query
        if (element.Name.ToLowerInvariant().Contains(queryLower))
        {
            score += 15.0;
        }

        // Description contains query
        if (element.Description.ToLowerInvariant().Contains(queryLower))
        {
            score += 10.0;
        }

        // Examples contain query
        if (element.Examples.Any(ex => ex.ToLowerInvariant().Contains(queryLower)))
        {
            score += 5.0;
        }

        return score;
    }
}