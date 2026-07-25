// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Provides access to HL7 v2.3 table definitions from embedded JSON resources.
/// Loads coded value tables for field validation and value generation.
/// </summary>
public partial class HL7TableProvider : IHL7TableProvider
{
    private readonly ILogger<HL7TableProvider> _logger;
    private readonly IDataResourceResolver _resourceResolver;
    private readonly string _dataPathPrefix;
    // Singleton-safe cache.
    private readonly ConcurrentDictionary<int, TableDefinition> _cache = new();

    public HL7TableProvider(
        ILogger<HL7TableProvider> logger,
        IDataResourceResolver resourceResolver,
        string dataPathPrefix = "standards/hl7/v23")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
        _dataPathPrefix = dataPathPrefix ?? throw new ArgumentNullException(nameof(dataPathPrefix));
    }

    /// <summary>
    /// Gets a table definition by ID (e.g., 1, 104, 155).
    /// Loads from the registered data-resource composition.
    /// </summary>
    public async Task<Result<TableDefinition>> GetTableAsync(int tableId)
    {
        try
        {
            // Check cache first
            if (_cache.TryGetValue(tableId, out var cachedTable))
            {
                return Result<TableDefinition>.Success(cachedTable);
            }

            // Build resource name for embedded JSON file - pad with zeros
            var tableIdString = tableId.ToString().PadLeft(4, '0');
            var resourcePath = $"{_dataPathPrefix}/tables/{tableIdString}.json";

            _logger.LogDebug("Loading table {TableId} from resource {ResourcePath}",
                tableId, resourcePath);

            var streamResult = await _resourceResolver.OpenReadAsync(resourcePath);
            if (streamResult.IsFailure)
            {
                _logger.LogDebug("Table resource unavailable: {ResourcePath}", resourcePath);
                return Result<TableDefinition>.Failure(streamResult.Error);
            }

            using var stream = streamResult.Value;
            // Parse JSON to table definition
            var json = await new StreamReader(stream).ReadToEndAsync();
            var jsonDoc = JsonDocument.Parse(json);
            var tableDefinition = ParseTableDefinitionFromJson(jsonDoc.RootElement, tableId);

            // Cache for future requests
            _cache[tableId] = tableDefinition;

            _logger.LogDebug("Successfully loaded table {TableId} ({TableName}) with {ValueCount} values",
                tableId, tableDefinition.Name, tableDefinition.Values.Count);

            return Result<TableDefinition>.Success(tableDefinition);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Malformed table resource {TableId}", tableId);
            return Result<TableDefinition>.Failure(Error.Create(
                "MALFORMED_DATA_RESOURCE", $"Table resource is not valid JSON: {ex.Message}", tableId.ToString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading table {TableId}", tableId);
            return Result<TableDefinition>.Failure($"Failed to load table: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses table JSON structure into strongly-typed definition object.
    /// </summary>
    private TableDefinition ParseTableDefinitionFromJson(JsonElement root, int tableId)
    {
        var name = root.GetProperty("name").GetString() ?? "";
        var type = root.TryGetProperty("type", out var typeElement)
            ? typeElement.GetString() ?? ""
            : "";

        var values = new List<TableValue>();
        if (root.TryGetProperty("values", out var valuesArray))
        {
            foreach (var valueElement in valuesArray.EnumerateArray())
            {
                var tableValue = ParseTableValueFromJson(valueElement);
                values.Add(tableValue);
            }
        }

        return new TableDefinition(
            Id: tableId,
            Name: name,
            Type: type,
            Values: values.AsReadOnly()
        );
    }

    /// <summary>
    /// Parses table value JSON structure into strongly-typed value object.
    /// </summary>
    private TableValue ParseTableValueFromJson(JsonElement valueElement)
    {
        var code = valueElement.GetProperty("value").GetString() ?? "";
        var description = valueElement.GetProperty("description").GetString() ?? "";

        return new TableValue(
            Code: code,
            Description: description
        );
    }
}

/// <summary>
/// Interface for accessing HL7 table definitions.
/// </summary>
public interface IHL7TableProvider
{
    /// <summary>
    /// Gets a table definition by ID.
    /// </summary>
    Task<Result<TableDefinition>> GetTableAsync(int tableId);
}

/// <summary>
/// Represents a complete HL7 table definition with coded values.
/// </summary>
public record TableDefinition(
    int Id,
    string Name,
    string Type,
    IReadOnlyList<TableValue> Values);

/// <summary>
/// Represents a coded value within a table.
/// </summary>
public record TableValue(
    string Code,
    string Description);
