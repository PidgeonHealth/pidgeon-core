// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Domain.Reference.Entities;

namespace Pidgeon.Core.Infrastructure.Reference.HL7;

/// <summary>
/// Loads HL7 numeric tables (<c>0001</c>, <c>0203</c>) and named tables
/// (<c>FirstName</c>, <c>ZipCode</c>) through the shared resolver-backed
/// JSON loader. The numeric and named-table code paths stay distinct because
/// the identifiers differ in casing conventions.
/// </summary>
public sealed class TableLoader
{
    private readonly HL7ReferenceJsonLoader _jsonLoader;
    private readonly ILogger<TableLoader> _logger;

    public TableLoader(HL7ReferenceJsonLoader jsonLoader, ILogger<TableLoader> logger)
    {
        _jsonLoader = jsonLoader;
        _logger = logger;
    }

    public async Task<StandardElement?> LoadNumericTableAsync(
        HL7ResourceContext context,
        string tableNumber,
        CancellationToken cancellationToken)
    {
        var relative = $"tables/{tableNumber}.json";
        if (!_jsonLoader.Exists(context, relative))
            return null;

        try
        {
            var json = await _jsonLoader.LoadAsync(context, relative, cancellationToken);
            var tableData = JsonSerializer.Deserialize<JsonElement>(json, HL7ReferenceJsonLoader.JsonOptions);

            return new StandardElement
            {
                Path = tableNumber,
                Name = tableData.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Description = tableData.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                Standard = context.Config.StandardId,
                Version = context.Config.Version,
                DataType = "Table",
                Usage = "Reference",
                ValidValues = HL7JsonExtraction.ExtractValidValues(tableData),
                Examples = HL7JsonExtraction.ExtractTableExamples(tableData)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading table {TableNumber}", tableNumber);
            return null;
        }
    }

    public async Task<StandardElement?> LoadNamedTableAsync(
        HL7ResourceContext context,
        string tableName,
        CancellationToken cancellationToken)
    {
        var relative = $"tables/{tableName}.json";
        if (!_jsonLoader.Exists(context, relative))
            return null;

        try
        {
            var json = await _jsonLoader.LoadAsync(context, relative, cancellationToken);
            var tableData = JsonSerializer.Deserialize<JsonElement>(json, HL7ReferenceJsonLoader.JsonOptions);

            return new StandardElement
            {
                Path = tableName,
                Name = tableData.TryGetProperty("name", out var name) ? name.GetString() ?? tableName : tableName,
                Description = tableData.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                Standard = context.Config.StandardId,
                Version = context.Config.Version,
                DataType = "Table",
                Usage = "Reference Dataset",
                ValidValues = HL7JsonExtraction.ExtractValidValues(tableData),
                Examples = HL7JsonExtraction.ExtractTableExamples(tableData)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading named table {TableName}", tableName);
            return null;
        }
    }

    public IEnumerable<string> EnumerateTableFiles(HL7ResourceContext context)
        => _jsonLoader.EnumerateFiles(context, "tables");
}
