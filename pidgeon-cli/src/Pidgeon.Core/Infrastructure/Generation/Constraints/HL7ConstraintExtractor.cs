// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Domain.Configuration;
using Pidgeon.Core.Domain.Reference.Entities;

namespace Pidgeon.Core.Infrastructure.Generation.Constraints;

/// <summary>
/// Parses HL7 v2.3 segment JSON and coded-table values into constraint
/// structures. JSON parsing for field definitions and table lookups lives
/// here, separate from data loading and value generation.
/// </summary>
public sealed class HL7ConstraintExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    private readonly HL7ConstraintDataLoader _dataLoader;
    private readonly HL7PatternEnhancer _patternEnhancer;
    private readonly ILogger<HL7ConstraintExtractor> _logger;

    public HL7ConstraintExtractor(
        HL7ConstraintDataLoader dataLoader,
        HL7PatternEnhancer patternEnhancer,
        ILogger<HL7ConstraintExtractor> logger)
    {
        _dataLoader = dataLoader ?? throw new ArgumentNullException(nameof(dataLoader));
        _patternEnhancer = patternEnhancer ?? throw new ArgumentNullException(nameof(patternEnhancer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Extracts a <see cref="FieldConstraints"/> record for the given field
    /// position from the provided segment element. Falls back to a minimal
    /// constraint set when the segment JSON is unavailable or the field is
    /// not found. The result is always enhanced with known HL7 data-type
    /// patterns before return.
    /// </summary>
    public async Task<FieldConstraints> ExtractFieldConstraintsAsync(
        StandardElement segmentElement,
        int fieldPosition)
    {
        try
        {
            var segmentName = segmentElement.Path.ToLowerInvariant();
            var json = await _dataLoader.LoadSegmentDataAsync(segmentName);

            if (!string.IsNullOrEmpty(json))
            {
                var jsonDoc = JsonDocument.Parse(json);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("fields", out var fieldsArray) && fieldsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in fieldsArray.EnumerateArray())
                    {
                        if (field.TryGetProperty("position", out var positionProp) &&
                            positionProp.ValueKind == JsonValueKind.Number &&
                            positionProp.GetInt32() == fieldPosition)
                        {
                            var constraints = new FieldConstraints
                            {
                                DataType = GetPropertyValue(field, "data_type"),
                                MaxLength = GetIntProperty(field, "length"),
                                Required = GetPropertyValue(field, "optionality") == "R",
                                Repeating = GetPropertyValue(field, "repeatability") == "∞",
                                TableReference = GetTableReference(field)
                            };

                            return _patternEnhancer.Enhance(constraints, fieldPosition);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing segment JSON for field {Position}", fieldPosition);
        }

        var fallbackConstraints = new FieldConstraints
        {
            Required = false,
            MaxLength = 100
        };

        return _patternEnhancer.Enhance(fallbackConstraints, fieldPosition);
    }

    /// <summary>
    /// Loads and parses a coded-table JSON body, returning the list of values
    /// (<c>values[].value</c>). Returns an empty list when the table cannot
    /// be loaded or parsed.
    /// </summary>
    public async Task<List<string>> ExtractTableValuesAsync(string tableId)
    {
        var values = new List<string>();

        try
        {
            var json = await _dataLoader.LoadTableDataAsync(tableId);
            if (!string.IsNullOrEmpty(json))
            {
                var jsonDoc = JsonDocument.Parse(json);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("values", out var valuesArray) && valuesArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var valueItem in valuesArray.EnumerateArray())
                    {
                        var value = GetPropertyValue(valueItem, "value");
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            values.Add(value);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting table values for {TableId}", tableId);
        }

        return values;
    }

    /// <summary>
    /// Extracts table values from a previously-loaded
    /// <see cref="StandardElement"/>'s <c>ValidValues</c> collection (the path
    /// used when the reference service provides a pre-parsed element).
    /// </summary>
    public List<string> ExtractTableValues(StandardElement tableElement)
    {
        var values = new List<string>();

        try
        {
            foreach (var validValue in tableElement.ValidValues)
            {
                if (!string.IsNullOrWhiteSpace(validValue.Code))
                {
                    values.Add(validValue.Code);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting table values");
        }

        return values;
    }

    private static string? GetTableReference(JsonElement field)
    {
        var tableValue = GetPropertyValue(field, "table");
        return string.IsNullOrEmpty(tableValue) ? null : tableValue;
    }

    private static string? GetPropertyValue(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int? GetIntProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : null;
    }
}
