// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Per-instance element constraint checks: declared data type, FHIR date format,
/// <c>fixed[x]</c> equality, and <c>pattern[x]</c> subset matching, plus the deep
/// JSON comparisons those rest on. Shared by the main element walk in
/// <see cref="ProfileValidator"/>, by slice-scoped child validation in
/// <see cref="SliceScopeValidator"/>, and by <see cref="SlicingEngine"/>'s
/// discriminator matching, so one element is graded the same way wherever the
/// walk reaches it. Pure functions, no I/O.
/// </summary>
internal static class ElementConstraintEvaluator
{
    // FHIR primitive type codes that map to JSON string
    private static readonly HashSet<string> StringTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string", "uri", "url", "canonical", "id", "code", "markdown", "oid", "uuid",
        "date", "dateTime", "time", "instant", "base64Binary", "xhtml"
    };

    // FHIR type codes for date/time formats
    private static readonly HashSet<string> DateTimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "date", "dateTime", "instant", "time"
    };

    public static void ValidateDataType(JsonElement element, FHIRElementDefinition elementDef, List<FHIRDiagnostic> diagnostics)
    {
        if (elementDef.Types.Count == 0) return;

        var expectedTypes = elementDef.Types.Select(t => t.Code).ToList();
        var actualKind = element.ValueKind;

        var isValid = expectedTypes.Any(typeCode => IsTypeMatch(actualKind, element, typeCode));

        if (!isValid)
        {
            diagnostics.Add(new FHIRDiagnostic(
                FHIRDiagnosticSeverity.Error,
                elementDef.Path,
                $"Element value has wrong type for '{elementDef.Path}'",
                Expected: $"Type: {string.Join(" | ", expectedTypes)}",
                Actual: $"JSON kind: {actualKind}",
                Fix: $"Provide a value matching one of: {string.Join(", ", expectedTypes)}"
            ));
        }

        // Additional date/time format validation
        if (actualKind == JsonValueKind.String && expectedTypes.Any(t => DateTimeTypes.Contains(t)))
        {
            var value = element.GetString();
            if (!string.IsNullOrEmpty(value))
            {
                ValidateDateTimeFormat(value, expectedTypes, elementDef.Path, diagnostics);
            }
        }
    }

    public static void ValidateFixedValue(JsonElement element, FHIRElementDefinition elementDef, List<FHIRDiagnostic> diagnostics)
    {
        if (!elementDef.FixedValue.HasValue) return;

        if (!JsonElementEquals(element, elementDef.FixedValue.Value))
        {
            diagnostics.Add(new FHIRDiagnostic(
                FHIRDiagnosticSeverity.Error,
                elementDef.Path,
                $"Element value does not match fixed value for '{elementDef.Path}'",
                Expected: elementDef.FixedValue.Value.GetRawText(),
                Actual: element.GetRawText(),
                Fix: $"Set value to exactly: {elementDef.FixedValue.Value.GetRawText()}"
            ));
        }
    }

    public static void ValidatePatternValue(JsonElement element, FHIRElementDefinition elementDef, List<FHIRDiagnostic> diagnostics)
    {
        if (!elementDef.PatternValue.HasValue) return;

        if (!JsonElementMatchesPattern(element, elementDef.PatternValue.Value))
        {
            diagnostics.Add(new FHIRDiagnostic(
                FHIRDiagnosticSeverity.Error,
                elementDef.Path,
                $"Element does not match pattern value for '{elementDef.Path}'",
                Expected: $"Contains: {elementDef.PatternValue.Value.GetRawText()}",
                Actual: element.GetRawText(),
                Fix: $"Ensure element includes: {elementDef.PatternValue.Value.GetRawText()}"
            ));
        }
    }

    private static bool IsTypeMatch(JsonValueKind kind, JsonElement element, string typeCode)
    {
        if (StringTypes.Contains(typeCode))
            return kind == JsonValueKind.String;

        return typeCode.ToLowerInvariant() switch
        {
            "boolean" => kind == JsonValueKind.True || kind == JsonValueKind.False,
            "integer" or "positiveint" or "unsignedint" =>
                kind == JsonValueKind.Number && element.TryGetInt64(out _),
            "decimal" => kind == JsonValueKind.Number,
            _ => kind == JsonValueKind.Object || kind == JsonValueKind.Array
        };
    }

    private static void ValidateDateTimeFormat(
        string value, List<string> expectedTypes, string path, List<FHIRDiagnostic> diagnostics)
    {
        // Basic format validation for FHIR date/dateTime types
        if (expectedTypes.Contains("date") && !expectedTypes.Contains("dateTime"))
        {
            // date: YYYY, YYYY-MM, or YYYY-MM-DD
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{4}(-\d{2}(-\d{2})?)?$"))
            {
                diagnostics.Add(new FHIRDiagnostic(
                    FHIRDiagnosticSeverity.Error,
                    path,
                    $"Invalid date format: '{value}'",
                    Expected: "Format: YYYY, YYYY-MM, or YYYY-MM-DD",
                    Actual: value,
                    Fix: "Use a valid FHIR date format (e.g., 2024-01-15)"
                ));
            }
        }
    }

    internal static bool JsonElementEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;

        return a.ValueKind switch
        {
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Number => a.GetDecimal() == b.GetDecimal(),
            JsonValueKind.True or JsonValueKind.False => a.GetBoolean() == b.GetBoolean(),
            JsonValueKind.Null => true,
            JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count() &&
                a.EnumerateObject().All(pa =>
                    b.TryGetProperty(pa.Name, out var bv) && JsonElementEquals(pa.Value, bv)),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength() &&
                a.EnumerateArray().Zip(b.EnumerateArray()).All(p => JsonElementEquals(p.First, p.Second)),
            _ => false
        };
    }

    internal static bool JsonElementMatchesPattern(JsonElement actual, JsonElement pattern)
    {
        if (pattern.ValueKind != actual.ValueKind)
        {
            // Allow pattern object to match against actual that may contain it differently
            return false;
        }

        return pattern.ValueKind switch
        {
            JsonValueKind.Object => pattern.EnumerateObject().All(pp =>
                actual.TryGetProperty(pp.Name, out var av) &&
                JsonElementMatchesPattern(av, pp.Value)),
            JsonValueKind.Array => pattern.EnumerateArray().All(pi =>
                actual.EnumerateArray().Any(ai => JsonElementMatchesPattern(ai, pi))),
            JsonValueKind.String => pattern.GetString() == actual.GetString(),
            JsonValueKind.Number => pattern.GetDecimal() == actual.GetDecimal(),
            JsonValueKind.True or JsonValueKind.False => pattern.GetBoolean() == actual.GetBoolean(),
            _ => true
        };
    }
}
