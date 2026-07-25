// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Reference;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.Reference;

/// <summary>
/// Singleton provider for demographic reference lists. Lazy-loads each
/// table on first request through the registered <see cref="IDataResourceResolver"/>
/// composition and caches in a <see cref="ConcurrentDictionary{TKey,TValue}"/> for the
/// rest of the process's lifetime. Empty lists are cached too — including for
/// tables no installed package supplies — so absent data degrades to empty
/// values (the consuming field falls through) instead of failing.
///
/// Registered as Singleton. Access is read-heavy from many threads in batch
/// generation; writes happen at most once per table (first miss).
/// </summary>
internal sealed partial class EmbeddedDemographicTableProvider : IDemographicTableProvider
{
    private readonly ILogger<EmbeddedDemographicTableProvider> _logger;
    private readonly IDataResourceResolver _resourceResolver;
    private readonly string _dataPathPrefix;
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _cache = new(StringComparer.Ordinal);

    public EmbeddedDemographicTableProvider(
        ILogger<EmbeddedDemographicTableProvider> logger,
        IDataResourceResolver resourceResolver,
        string dataPathPrefix = "standards/hl7/v23/tables")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
        _dataPathPrefix = dataPathPrefix ?? throw new ArgumentNullException(nameof(dataPathPrefix));
    }

    public IReadOnlyList<string> GetValues(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return Array.Empty<string>();

        return _cache.GetOrAdd(tableName, LoadTable);
    }

    private IReadOnlyList<string> LoadTable(string tableName)
    {
        try
        {
            var resourcePath = $"{_dataPathPrefix}/{tableName}.json";

            // One-time load per table inside the cache miss; the registered resolvers
            // complete synchronously, so blocking here cannot deadlock.
            var streamResult = _resourceResolver.OpenReadAsync(resourcePath).AsTask().GetAwaiter().GetResult();
            if (streamResult.IsFailure)
            {
                _logger.LogDebug("Demographic table resource unavailable: {ResourcePath}", resourcePath);
                return Array.Empty<string>();
            }

            string json;
            using (var stream = streamResult.Value)
            using (var reader = new StreamReader(stream))
            {
                json = reader.ReadToEnd();
            }

            using var jsonDoc = JsonDocument.Parse(json);

            if (!jsonDoc.RootElement.TryGetProperty("values", out var valuesArray))
            {
                return Array.Empty<string>();
            }

            var values = new List<string>(valuesArray.GetArrayLength());
            foreach (var valueElement in valuesArray.EnumerateArray())
            {
                if (valueElement.TryGetProperty("value", out var valueProperty))
                {
                    var value = valueProperty.GetString();
                    if (!string.IsNullOrEmpty(value))
                        values.Add(value);
                }
            }

            _logger.LogDebug("Loaded demographic table {TableName} with {ValueCount} values", tableName, values.Count);
            return values;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load demographic table {TableName}; caching empty list", tableName);
            return Array.Empty<string>();
        }
    }
}
