// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Reference.Entities;

namespace Pidgeon.Core.Infrastructure.Reference.HL7;

/// <summary>
/// Pure JSON-to-domain projection helpers shared across the HL7 reference
/// loaders. Kept static so callers don't need to plumb an instance around.
/// </summary>
internal static class HL7JsonExtraction
{
    public static List<string> ExtractExamples(JsonElement data)
    {
        var examples = new List<string>();
        if (data.TryGetProperty("examples", out var examplesArray) && examplesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var example in examplesArray.EnumerateArray())
            {
                if (example.ValueKind == JsonValueKind.String)
                {
                    var exampleValue = example.GetString();
                    if (!string.IsNullOrEmpty(exampleValue))
                        examples.Add(exampleValue);
                }
            }
        }
        return examples;
    }

    public static List<string> ExtractChildPaths(JsonElement segmentData)
    {
        var childPaths = new List<string>();
        if (segmentData.TryGetProperty("fields", out var fields))
        {
            if (fields.ValueKind == JsonValueKind.Array)
            {
                foreach (var field in fields.EnumerateArray())
                {
                    if (field.TryGetProperty("field_name", out var fieldNameProp))
                    {
                        var fieldName = fieldNameProp.GetString();
                        if (!string.IsNullOrEmpty(fieldName))
                            childPaths.Add(fieldName);
                    }
                }
            }
            else if (fields.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in fields.EnumerateObject())
                    childPaths.Add(field.Name);
            }
        }
        return childPaths;
    }

    public static List<string> ExtractDataTypeComponents(JsonElement dataTypeData)
    {
        var components = new List<string>();
        if (dataTypeData.TryGetProperty("components", out var componentsObj))
        {
            foreach (var component in componentsObj.EnumerateObject())
                components.Add(component.Name);
        }
        return components;
    }

    public static List<string> ExtractFieldComponents(JsonElement fieldData)
    {
        var components = new List<string>();
        if (fieldData.TryGetProperty("components", out var componentsObj))
        {
            foreach (var component in componentsObj.EnumerateObject())
                components.Add(component.Name);
        }
        return components;
    }

    public static List<ValidValue> ExtractValidValues(JsonElement data)
    {
        var validValues = new List<ValidValue>();
        if (data.TryGetProperty("validValues", out var valuesArray) && valuesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in valuesArray.EnumerateArray())
            {
                if (value.TryGetProperty("code", out var code) && value.TryGetProperty("description", out var desc))
                {
                    validValues.Add(new ValidValue
                    {
                        Code = code.GetString() ?? "",
                        Description = desc.GetString() ?? "",
                        IsDeprecated = value.TryGetProperty("deprecated", out var deprecated) && deprecated.GetBoolean()
                    });
                }
            }
        }
        else if (data.TryGetProperty("values", out var tableValuesArray) && tableValuesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in tableValuesArray.EnumerateArray())
            {
                if (value.TryGetProperty("code", out var code) && value.TryGetProperty("description", out var desc))
                {
                    validValues.Add(new ValidValue
                    {
                        Code = code.GetString() ?? "",
                        Description = desc.GetString() ?? "",
                        IsDeprecated = value.TryGetProperty("deprecated", out var deprecated) && deprecated.GetBoolean()
                    });
                }
            }
        }
        return validValues;
    }

    public static List<string> ExtractTableExamples(JsonElement tableData)
    {
        var examples = new List<string>();
        if (tableData.TryGetProperty("values", out var valuesArray) && valuesArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in valuesArray.EnumerateArray())
            {
                if (value.TryGetProperty("code", out var code))
                {
                    var codeValue = code.GetString();
                    if (!string.IsNullOrEmpty(codeValue))
                        examples.Add(codeValue);
                }
                if (examples.Count >= 5) break;
            }
        }
        return examples;
    }

    public static string GetFieldProperty(JsonElement fieldData, string primaryName, string fallbackName)
    {
        if (fieldData.TryGetProperty(primaryName, out var primary))
            return primary.GetString() ?? "";
        if (fieldData.TryGetProperty(fallbackName, out var fallback))
            return fallback.GetString() ?? "";
        return "";
    }

    public static int? GetFieldIntProperty(JsonElement fieldData, string primaryName, string fallbackName)
    {
        if (fieldData.TryGetProperty(primaryName, out var primary) && primary.ValueKind == JsonValueKind.Number)
            return primary.GetInt32();
        if (fieldData.TryGetProperty(fallbackName, out var fallback))
        {
            if (fallback.ValueKind == JsonValueKind.Number)
                return fallback.GetInt32();
            var lengthStr = fallback.GetString();
            if (int.TryParse(lengthStr, out var parsed))
                return parsed;
        }
        return null;
    }
}
