// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Domain.Reference.Entities;

namespace Pidgeon.Core.Infrastructure.Reference.HL7;

/// <summary>
/// Loads HL7 data type definitions (CE, XPN, CX, ...) and resolves
/// field component lookups (<c>PID.3.5</c>) through the shared
/// resolver-backed JSON loader.
/// </summary>
public sealed class DataTypeLoader
{
    private readonly HL7ReferenceJsonLoader _jsonLoader;
    private readonly ILogger<DataTypeLoader> _logger;

    public DataTypeLoader(HL7ReferenceJsonLoader jsonLoader, ILogger<DataTypeLoader> logger)
    {
        _jsonLoader = jsonLoader;
        _logger = logger;
    }

    public async Task<StandardElement?> LoadDataTypeAsync(
        HL7ResourceContext context,
        string dataTypeName,
        CancellationToken cancellationToken)
    {
        var relative = $"data_types/{dataTypeName.ToLowerInvariant()}.json";
        if (!_jsonLoader.Exists(context, relative))
            return null;

        try
        {
            var json = await _jsonLoader.LoadAsync(context, relative, cancellationToken);
            var dataTypeData = JsonSerializer.Deserialize<JsonElement>(json, HL7ReferenceJsonLoader.JsonOptions);

            var codeProperty = dataTypeData.TryGetProperty("code", out var codeProp)
                ? codeProp.GetString()
                : dataTypeName.ToUpperInvariant();

            return new StandardElement
            {
                Path = codeProperty ?? dataTypeName.ToUpperInvariant(),
                Name = dataTypeData.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Description = dataTypeData.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                Standard = context.Config.StandardId,
                Version = context.Config.Version,
                DataType = "DataType",
                Usage = dataTypeData.TryGetProperty("category", out var category) ? category.GetString() ?? "" : "",
                Examples = HL7JsonExtraction.ExtractExamples(dataTypeData),
                ChildPaths = HL7JsonExtraction.ExtractDataTypeComponents(dataTypeData)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading data type {DataTypeName}", dataTypeName);
            return null;
        }
    }

    /// <summary>
    /// Resolves a data-type component lookup from an already-loaded segment
    /// <paramref name="fields"/> JSON element plus the field and component
    /// path fragments (e.g. <c>PID.3</c> + <c>5</c>). Stateless; reads the
    /// owning data-type definition through the resolver-backed loader.
    /// </summary>
    public async Task<StandardElement?> ExtractComponentAsync(
        HL7ResourceContext context,
        JsonElement fields,
        string fieldName,
        string componentPath,
        CancellationToken cancellationToken)
    {
        JsonElement fieldData = default;
        bool fieldFound = false;

        if (fields.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fields.EnumerateArray())
            {
                if (field.TryGetProperty("field_name", out var fieldNameProp)
                    && fieldNameProp.GetString() == fieldName)
                {
                    fieldData = field;
                    fieldFound = true;
                    break;
                }
            }
        }

        if (!fieldFound)
            return null;

        if (!fieldData.TryGetProperty("data_type", out var dataTypeElement))
            return null;

        var dataTypeName = dataTypeElement.GetString();
        if (string.IsNullOrEmpty(dataTypeName))
            return null;

        var dataTypeRelative = $"data_types/{dataTypeName.ToLowerInvariant()}.json";
        if (!_jsonLoader.Exists(context, dataTypeRelative))
            return null;

        var dataTypeJson = await _jsonLoader.LoadAsync(context, dataTypeRelative, cancellationToken);
        using var dataTypeDoc = JsonDocument.Parse(dataTypeJson);
        var dataTypeRoot = dataTypeDoc.RootElement;

        if (!dataTypeRoot.TryGetProperty("fields", out var dataTypeFields))
            return null;

        if (!int.TryParse(componentPath, out var componentPosition))
            return null;

        JsonElement? componentData = null;
        foreach (var field in dataTypeFields.EnumerateArray())
        {
            if (field.TryGetProperty("position", out var positionElement)
                && positionElement.GetInt32() == componentPosition)
            {
                componentData = field;
                break;
            }
        }

        if (!componentData.HasValue)
            return null;

        var component = componentData.Value;
        var fullPath = $"{fieldName}.{componentPath}";

        var element = new StandardElement
        {
            Path = fullPath,
            Name = component.TryGetProperty("field_description", out var desc) ? desc.GetString() ?? "" : "",
            Description = component.TryGetProperty("field_description", out var desc2) ? desc2.GetString() ?? "" : "",
            Standard = context.Config.StandardId,
            Version = context.Config.Version,
            DataType = component.TryGetProperty("data_type", out var compDataType) ? compDataType.GetString() ?? "" : "",
            Usage = component.TryGetProperty("optionality", out var usage) ? usage.GetString() ?? "" : "",
            MaxLength = component.TryGetProperty("length", out var length)
                ? (int.TryParse(length.GetString(), out var len) ? len : (int?)null)
                : null,
            ParentPath = fieldName,
            Examples = HL7JsonExtraction.ExtractExamples(component),
            ValidValues = HL7JsonExtraction.ExtractValidValues(component)
        };

        if (component.TryGetProperty("table", out var table) && !string.IsNullOrEmpty(table.GetString()))
        {
            var tableRef = table.GetString();
            element = element with
            {
                Description = element.Description + $" (Table {tableRef})"
            };
        }

        return element;
    }

    public IEnumerable<string> EnumerateDataTypeFiles(HL7ResourceContext context)
        => _jsonLoader.EnumerateFiles(context, "data_types");
}
