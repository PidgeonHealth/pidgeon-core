// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;
using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Serves fallback coded values for HL7 generation from the embedded
/// <c>data.generation.hl7-fallback-values.json</c> resource. Three sections, one per
/// consumer: user-defined-table fallbacks (table missing/empty), keyword fallbacks
/// (coded-field heuristics), and constraint-table fallbacks (table-reference path).
/// </summary>
public interface IHL7FallbackValueSource
{
    /// <summary>
    /// Fallback values for a user-defined or empty HL7 table, keyed by table number.
    /// Returns null when no fallback is defined for the table.
    /// </summary>
    IReadOnlyList<string>? GetUserDefinedTableFallback(int tableId);

    /// <summary>
    /// Keyword-routed coded-value fallbacks, in match order (first keyword hit wins).
    /// </summary>
    IReadOnlyList<KeywordFallback> KeywordFallbacks { get; }

    /// <summary>
    /// Fallback values for a constraint table reference (zero-padded id, e.g. "0001").
    /// Returns null when no fallback is defined for the table.
    /// </summary>
    IReadOnlyList<string>? GetConstraintTableFallback(string tableId);
}

/// <summary>
/// A coded-value fallback selected when a field name contains any of the keywords.
/// </summary>
public sealed record KeywordFallback(
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Values);

/// <summary>
/// Default <see cref="IHL7FallbackValueSource"/> backed by the fallback-values JSON served
/// through the registered <see cref="IDataResourceResolver"/> composition. Loaded once per
/// process (registered as a singleton). When the resource is not installed, every section
/// is empty — consumers fall through to their next resolution strategy rather than failing.
/// </summary>
public partial class HL7FallbackValueSource : IHL7FallbackValueSource
{
    private readonly Lazy<FallbackData> _data;

    public HL7FallbackValueSource(
        ILogger<HL7FallbackValueSource> logger,
        IDataResourceResolver resourceResolver,
        string resourcePath = "generation/hl7-fallback-values.json")
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(resourceResolver);
        ArgumentNullException.ThrowIfNull(resourcePath);
        _data = new Lazy<FallbackData>(() => Load(logger, resourceResolver, resourcePath));
    }

    public IReadOnlyList<string>? GetUserDefinedTableFallback(int tableId)
        => _data.Value.UserDefinedTableFallbacks.TryGetValue(tableId, out var values)
            ? values
            : null;

    public IReadOnlyList<KeywordFallback> KeywordFallbacks => _data.Value.KeywordFallbacks;

    public IReadOnlyList<string>? GetConstraintTableFallback(string tableId)
        => _data.Value.ConstraintTableFallbacks.TryGetValue(tableId, out var values)
            ? values
            : null;

    private static FallbackData Load(ILogger logger, IDataResourceResolver resourceResolver, string resourcePath)
    {
        // One-time process-lifetime load inside the Lazy; the registered resolvers complete
        // synchronously, so blocking here cannot deadlock.
        var streamResult = resourceResolver.OpenReadAsync(resourcePath).AsTask().GetAwaiter().GetResult();
        if (streamResult.IsFailure)
        {
            logger.LogWarning(
                "HL7 fallback-values resource unavailable ({ResourcePath}); coded-value fallbacks are empty",
                resourcePath);
            return FallbackData.Empty;
        }

        using var stream = streamResult.Value;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var tableFallbacks = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var property in root.GetProperty("userDefinedTableFallbacks").EnumerateObject())
        {
            tableFallbacks[int.Parse(property.Name)] = ReadStringArray(property.Value);
        }

        var keywordFallbacks = new List<KeywordFallback>();
        foreach (var entry in root.GetProperty("keywordFallbacks").EnumerateArray())
        {
            keywordFallbacks.Add(new KeywordFallback(
                ReadStringArray(entry.GetProperty("keywords")),
                ReadStringArray(entry.GetProperty("values"))));
        }

        var constraintFallbacks = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var property in root.GetProperty("constraintTableFallbacks").EnumerateObject())
        {
            constraintFallbacks[property.Name] = ReadStringArray(property.Value);
        }

        logger.LogDebug(
            "Loaded HL7 fallback values: {TableCount} table, {KeywordCount} keyword, {ConstraintCount} constraint entries",
            tableFallbacks.Count, keywordFallbacks.Count, constraintFallbacks.Count);

        return new FallbackData(tableFallbacks, keywordFallbacks, constraintFallbacks);
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element)
        => element.EnumerateArray().Select(v => v.GetString() ?? "").ToArray();

    private sealed record FallbackData(
        IReadOnlyDictionary<int, IReadOnlyList<string>> UserDefinedTableFallbacks,
        IReadOnlyList<KeywordFallback> KeywordFallbacks,
        IReadOnlyDictionary<string, IReadOnlyList<string>> ConstraintTableFallbacks)
    {
        public static FallbackData Empty { get; } = new(
            new Dictionary<int, IReadOnlyList<string>>(),
            Array.Empty<KeywordFallback>(),
            new Dictionary<string, IReadOnlyList<string>>());
    }
}
