// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Domain.Reference.Entities;
using Pidgeon.Core.Infrastructure.Standards.HL7;

namespace Pidgeon.Core.Infrastructure.Reference.HL7;

/// <summary>
/// Loads HL7 segment and field-level definitions from the JSON
/// reference data. The "segments" per-concern helper for
/// <see cref="JsonHL7ReferencePlugin"/>.
/// </summary>
public sealed class SegmentDefinitionLoader
{
    private readonly HL7ReferenceJsonLoader _jsonLoader;
    private readonly DataTypeLoader _dataTypeLoader;
    private readonly ILogger<SegmentDefinitionLoader> _logger;

    public SegmentDefinitionLoader(
        HL7ReferenceJsonLoader jsonLoader,
        DataTypeLoader dataTypeLoader,
        ILogger<SegmentDefinitionLoader> logger)
    {
        _jsonLoader = jsonLoader;
        _dataTypeLoader = dataTypeLoader;
        _logger = logger;
    }

    /// <summary>
    /// Loads a segment-level element (e.g. <c>PID</c>, <c>MSH</c>). Returns
    /// null when the segment definition is missing.
    /// </summary>
    public async Task<StandardElement?> LoadSegmentAsync(
        HL7ResourceContext context,
        string segmentName,
        CancellationToken cancellationToken)
    {
        var relative = $"segments/{HL7ReservedResourceName.ResourceStem(segmentName)}.json";
        if (!_jsonLoader.Exists(context, relative))
            return null;

        try
        {
            var json = await _jsonLoader.LoadAsync(context, relative, cancellationToken);
            var segmentData = JsonSerializer.Deserialize<JsonElement>(json, HL7ReferenceJsonLoader.JsonOptions);

            if (!segmentData.TryGetProperty("code", out var codeProp))
                return null;

            return new StandardElement
            {
                Path = codeProp.GetString() ?? segmentName,
                Name = segmentData.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Description = segmentData.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                Standard = context.Config.StandardId,
                Version = context.Config.Version,
                DataType = "Segment",
                Usage = segmentData.TryGetProperty("usage", out var usage) ? usage.GetString() ?? "" : "",
                Examples = HL7JsonExtraction.ExtractExamples(segmentData),
                ChildPaths = HL7JsonExtraction.ExtractChildPaths(segmentData)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading segment {SegmentName}", segmentName);
            return null;
        }
    }

    /// <summary>
    /// Loads a field (e.g. <c>PID.3</c>) or component-level element
    /// (e.g. <c>PID.3.5</c>) by reading the owning segment definition
    /// and delegating component resolution to <see cref="DataTypeLoader"/>.
    /// </summary>
    public async Task<StandardElement?> LoadFieldAsync(
        HL7ResourceContext context,
        string fieldPath,
        CancellationToken cancellationToken)
    {
        var pathParts = fieldPath.Split('.');
        var segmentName = pathParts[0];

        try
        {
            var relative = $"segments/{HL7ReservedResourceName.ResourceStem(segmentName)}.json";
            if (!_jsonLoader.Exists(context, relative))
                return null;

            var json = await _jsonLoader.LoadAsync(context, relative, cancellationToken);
            var segmentData = JsonSerializer.Deserialize<JsonElement>(json, HL7ReferenceJsonLoader.JsonOptions);

            if (!segmentData.TryGetProperty("fields", out var fields))
                return null;

            if (pathParts.Length == 2)
            {
                return ExtractFieldElement(context, fields, fieldPath);
            }
            if (pathParts.Length == 3)
            {
                var fieldName = $"{pathParts[0]}.{pathParts[1]}";
                var componentPath = pathParts[2];
                return await _dataTypeLoader.ExtractComponentAsync(
                    context, fields, fieldName, componentPath, cancellationToken);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading field {FieldPath}", fieldPath);
            return null;
        }
    }

    /// <summary>
    /// Loads every element (segment + fields) from a single segment JSON entry,
    /// addressed by its context-relative path (e.g. <c>"segments/pid.json"</c> as
    /// enumerated by <see cref="HL7ReferenceJsonLoader.EnumerateFiles"/>).
    /// Used by the plugin's search / list-top-level / walk-all paths.
    /// </summary>
    public async Task<List<StandardElement>> LoadAllSegmentElementsAsync(
        HL7ResourceContext context,
        string segmentFileRelativePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await _jsonLoader.LoadAsync(context, segmentFileRelativePath, cancellationToken);

            var segmentData = JsonSerializer.Deserialize<JsonElement>(json);
            var elements = new List<StandardElement>();

            if (!segmentData.TryGetProperty("code", out var segmentName))
                return elements;

            elements.Add(new StandardElement
            {
                Path = segmentName.GetString() ?? "",
                Name = segmentData.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Description = segmentData.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                Standard = context.Config.StandardId
            });

            if (!segmentData.TryGetProperty("fields", out var fields))
                return elements;

            if (fields.ValueKind == JsonValueKind.Array)
            {
                foreach (var fieldData in fields.EnumerateArray())
                {
                    if (!fieldData.TryGetProperty("field_name", out var fieldNameProp)) continue;
                    var fieldPath = fieldNameProp.GetString();
                    if (string.IsNullOrEmpty(fieldPath)) continue;

                    elements.Add(new StandardElement
                    {
                        Path = fieldPath,
                        Name = HL7JsonExtraction.GetFieldProperty(fieldData, "name", "field_description"),
                        Description = HL7JsonExtraction.GetFieldProperty(fieldData, "description", "field_description"),
                        Standard = context.Config.StandardId,
                        Version = context.Config.Version,
                        DataType = HL7JsonExtraction.GetFieldProperty(fieldData, "dataType", "data_type"),
                        Usage = HL7JsonExtraction.GetFieldProperty(fieldData, "usage", "optionality"),
                        MaxLength = HL7JsonExtraction.GetFieldIntProperty(fieldData, "maxLength", "length"),
                        ParentPath = segmentName.GetString() ?? "",
                        Examples = HL7JsonExtraction.ExtractExamples(fieldData),
                        ValidValues = HL7JsonExtraction.ExtractValidValues(fieldData),
                        ChildPaths = HL7JsonExtraction.ExtractFieldComponents(fieldData)
                    });
                }
            }
            else if (fields.ValueKind == JsonValueKind.Object)
            {
                foreach (var fieldProperty in fields.EnumerateObject())
                {
                    var fieldPath = fieldProperty.Name;
                    var fieldData = fieldProperty.Value;

                    var fieldElement = new StandardElement
                    {
                        Path = fieldPath,
                        Name = fieldData.TryGetProperty("name", out var fieldName) ? fieldName.GetString() ?? "" : "",
                        Description = fieldData.TryGetProperty("description", out var fieldDesc) ? fieldDesc.GetString() ?? "" : "",
                        Standard = context.Config.StandardId,
                        ParentPath = segmentName.GetString()
                    };

                    if (fieldData.TryGetProperty("validValues", out var validValues)
                        && validValues.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var value in validValues.EnumerateArray())
                        {
                            if (value.TryGetProperty("code", out var code)
                                && value.TryGetProperty("description", out var valueDesc))
                            {
                                fieldElement.ValidValues.Add(new ValidValue
                                {
                                    Code = code.GetString() ?? "",
                                    Description = valueDesc.GetString() ?? ""
                                });
                            }
                        }
                    }

                    elements.Add(fieldElement);
                }
            }

            return elements;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading segment elements from {SegmentFile}", segmentFileRelativePath);
            return new List<StandardElement>();
        }
    }

    /// <summary>
    /// Enumerates the segment file identifiers (filesystem paths or
    /// embedded-resource names) available in the given context.
    /// </summary>
    public IEnumerable<string> EnumerateSegmentFiles(HL7ResourceContext context)
        => _jsonLoader.EnumerateFiles(context, "segments");

    private StandardElement? ExtractFieldElement(HL7ResourceContext context, JsonElement fields, string fieldPath)
    {
        JsonElement fieldData = default;
        bool fieldFound = false;

        if (fields.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fields.EnumerateArray())
            {
                if (field.TryGetProperty("field_name", out var fieldNameProp)
                    && fieldNameProp.GetString() == fieldPath)
                {
                    fieldData = field;
                    fieldFound = true;
                    break;
                }
            }
        }
        else if (fields.ValueKind == JsonValueKind.Object)
        {
            fieldFound = fields.TryGetProperty(fieldPath, out fieldData);
        }

        if (!fieldFound)
            return null;

        return new StandardElement
        {
            Path = fieldPath,
            Name = HL7JsonExtraction.GetFieldProperty(fieldData, "name", "field_description"),
            Description = HL7JsonExtraction.GetFieldProperty(fieldData, "description", "field_description"),
            Standard = context.Config.StandardId,
            Version = context.Config.Version,
            DataType = HL7JsonExtraction.GetFieldProperty(fieldData, "dataType", "data_type"),
            Usage = HL7JsonExtraction.GetFieldProperty(fieldData, "usage", "optionality"),
            MaxLength = HL7JsonExtraction.GetFieldIntProperty(fieldData, "maxLength", "length"),
            Position = HL7JsonExtraction.GetFieldIntProperty(fieldData, "position", "position"),
            Repeatability = HL7JsonExtraction.GetFieldProperty(fieldData, "repeatability", "repeatability"),
            TableReference = HL7JsonExtraction.GetFieldProperty(fieldData, "table", "table"),
            ParentPath = fieldPath.Split('.')[0],
            Examples = HL7JsonExtraction.ExtractExamples(fieldData),
            ValidValues = HL7JsonExtraction.ExtractValidValues(fieldData),
            ChildPaths = HL7JsonExtraction.ExtractFieldComponents(fieldData)
        };
    }

}
