// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Evaluates ElementDefinition min/max cardinality per parent element instance,
/// as the FHIR spec requires: each repeat of a parent carries its own count for
/// a child constraint (two Patient.name entries may each hold one family;
/// name.family max=1 is judged within each name, never summed across both, and
/// identifier.system min=1 must hold inside every identifier, not merely once
/// somewhere). Extracted from <see cref="ProfileValidator"/>, which retains the
/// flat element map for presence lookups; pure functions, no I/O.
/// </summary>
internal static class ElementCardinalityEvaluator
{
    // Known FHIR type name suffixes for choice types [x]
    internal static readonly string[] ChoiceTypeSuffixes = new[]
    {
        "String", "Boolean", "Integer", "Decimal", "DateTime", "Date", "Time", "Instant",
        "Uri", "Url", "Canonical", "Code", "Markdown", "Id", "Oid", "Uuid",
        "Quantity", "Range", "Ratio", "Period", "Timing",
        "CodeableConcept", "Coding", "Reference", "Identifier",
        "HumanName", "Address", "ContactPoint", "Attachment", "Annotation",
        "Age", "Money", "Duration", "Count", "Distance",
        "SampledData", "Signature", "Base64Binary"
    };

    /// <summary>
    /// Emit min/max cardinality diagnostics for one element definition, counting
    /// occurrences within each parent element instance. An absent parent yields
    /// no min violation (official-validator semantics: a min=1 child binds only
    /// where its parent exists); each violating parent instance emits its own
    /// diagnostic.
    /// </summary>
    public static void Validate(
        JsonElement root,
        FHIRElementDefinition elementDef,
        string resourceType,
        List<FHIRDiagnostic> diagnostics)
    {
        if (!elementDef.Path.StartsWith(resourceType + ".", StringComparison.Ordinal))
            return;

        var segments = elementDef.Path[(resourceType.Length + 1)..].Split('.');
        var leaf = segments[^1];

        foreach (var parent in EnumerateParentInstances(root, segments))
        {
            var count = CountLeafOccurrences(parent, leaf);
            CheckMinMax(elementDef, count, diagnostics);
        }
    }

    /// <summary>
    /// Walk the parent chain (all segments but the last) from <paramref name="root"/>,
    /// fanning out across array repeats. Any absent link yields zero instances — which
    /// is what makes a min constraint under an absent parent a non-violation. Shared
    /// with <see cref="SliceScopeValidator"/>, whose slice entries fan out the same way.
    /// </summary>
    internal static List<JsonElement> EnumerateParentInstances(JsonElement root, string[] segments)
    {
        var frontier = new List<JsonElement> { root };
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var next = new List<JsonElement>();
            foreach (var elem in frontier)
            {
                if (elem.ValueKind != JsonValueKind.Object) continue;
                if (!elem.TryGetProperty(segments[i], out var child)) continue;

                if (child.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in child.EnumerateArray())
                        next.Add(item);
                }
                else
                {
                    next.Add(child);
                }
            }
            frontier = next;
        }
        return frontier;
    }

    // Count leaf occurrences on one parent instance. A choice leaf (value[x])
    // sums across all typed variants, so two differently-typed values still
    // violate max=1.
    private static int CountLeafOccurrences(JsonElement parent, string leaf)
    {
        if (parent.ValueKind != JsonValueKind.Object) return 0;

        if (leaf.EndsWith("[x]", StringComparison.Ordinal))
        {
            var baseName = leaf[..^3];
            return ChoiceTypeSuffixes.Sum(suffix => CountProperty(parent, baseName + suffix));
        }

        return CountProperty(parent, leaf);
    }

    private static int CountProperty(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.GetArrayLength(),
            JsonValueKind.Null => 0,
            _ => 1
        };
    }

    // ponytail: N violating parent instances emit N identical diagnostics (no
    // instance index in the path); suffix indexes if scorecard noise matters.
    private static void CheckMinMax(FHIRElementDefinition elementDef, int count, List<FHIRDiagnostic> diagnostics)
    {
        if (count < elementDef.Min)
        {
            var elementName = elementDef.Path.Split('.').Last();
            diagnostics.Add(new FHIRDiagnostic(
                FHIRDiagnosticSeverity.Error,
                elementDef.Path,
                $"Element '{elementDef.Path}' has cardinality {count} but minimum is {elementDef.Min}",
                Expected: $"min: {elementDef.Min}",
                Actual: $"found: {count}",
                Fix: $"Add the required element '{elementName}'"
            ));
        }

        if (elementDef.Max != "*" && int.TryParse(elementDef.Max, out var maxInt) && count > maxInt)
        {
            var elementName = elementDef.Path.Split('.').Last();
            diagnostics.Add(new FHIRDiagnostic(
                FHIRDiagnosticSeverity.Error,
                elementDef.Path,
                $"Element '{elementDef.Path}' has {count} occurrences but maximum is {maxInt}",
                Expected: $"max: {elementDef.Max}",
                Actual: $"found: {count}",
                Fix: $"Remove excess '{elementName}' elements"
            ));
        }
    }
}
