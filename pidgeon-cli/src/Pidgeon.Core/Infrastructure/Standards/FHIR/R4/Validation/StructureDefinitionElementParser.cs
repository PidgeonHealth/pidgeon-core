// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Pure JSON → model mapping for the leaf structures of a StructureDefinition:
/// a single snapshot <c>element</c> constraint and an embedded <c>ValueSet</c>.
/// Extracted from <see cref="StructureDefinitionLoader"/> so the loader is left
/// with load-and-index orchestration; these are deterministic, dependency-free
/// mappers with no I/O or logging.
/// </summary>
internal static class StructureDefinitionElementParser
{
    /// <summary>
    /// Map one StructureDefinition snapshot <c>element</c> object into an
    /// <see cref="FHIRElementDefinition"/> (path, cardinality, types, binding,
    /// fixed/pattern values, slicing, must-support).
    /// </summary>
    public static FHIRElementDefinition ParseElement(JsonElement elem)
    {
        var ed = new FHIRElementDefinition
        {
            Id = elem.TryGetProperty("id", out var id) ? id.GetString() : null,
            Path = elem.TryGetProperty("path", out var path) ? path.GetString() ?? "" : "",
            Min = elem.TryGetProperty("min", out var min) ? min.GetInt32() : 0,
            Max = elem.TryGetProperty("max", out var max) ? max.GetString() ?? "*" : "*",
            SliceName = elem.TryGetProperty("sliceName", out var sn) ? sn.GetString() : null,
            MustSupport = elem.TryGetProperty("mustSupport", out var ms)
                && ms.ValueKind == JsonValueKind.True,
        };

        // Parse types
        if (elem.TryGetProperty("type", out var types) && types.ValueKind == JsonValueKind.Array)
        {
            ed.Types = types.EnumerateArray().Select(t =>
            {
                var et = new FHIRElementType
                {
                    Code = ResolveTypeCode(t),
                };
                if (t.TryGetProperty("profile", out var pr) && pr.ValueKind == JsonValueKind.Array)
                {
                    et.Profile = pr.EnumerateArray()
                        .Select(p => p.GetString() ?? "")
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToList();
                }
                if (t.TryGetProperty("targetProfile", out var tp) && tp.ValueKind == JsonValueKind.Array)
                {
                    et.TargetProfile = tp.EnumerateArray()
                        .Select(p => p.GetString() ?? "")
                        .Where(p => !string.IsNullOrEmpty(p))
                        .ToList();
                }
                return et;
            }).ToList();
        }

        // Parse binding
        if (elem.TryGetProperty("binding", out var binding))
        {
            ed.Binding = new FHIRElementBinding
            {
                Strength = binding.TryGetProperty("strength", out var strength) ? strength.GetString() ?? "" : "",
                ValueSet = binding.TryGetProperty("valueSet", out var vs) ? vs.GetString() : null,
            };
        }

        // Parse constraint invariants; xpath-only entries (no expression) are
        // skipped — the evaluator runs FHIRPath expressions only, as the
        // official validator does.
        if (elem.TryGetProperty("constraint", out var constraints) && constraints.ValueKind == JsonValueKind.Array)
        {
            ed.Constraints = constraints.EnumerateArray()
                .Where(c => c.TryGetProperty("expression", out var expr) && expr.ValueKind == JsonValueKind.String)
                .Select(c => new FHIRConstraint(
                    c.TryGetProperty("key", out var key) ? key.GetString() ?? "" : "",
                    c.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "error" : "error",
                    c.TryGetProperty("human", out var human) ? human.GetString() ?? "" : "",
                    c.GetProperty("expression").GetString() ?? ""))
                .ToList();
        }

        // Parse fixed[x] - scan for any property starting with "fixed"
        foreach (var prop in elem.EnumerateObject())
        {
            if (prop.Name.StartsWith("fixed", StringComparison.Ordinal) && prop.Name.Length > 5)
            {
                ed.FixedValue = prop.Value.Clone();
                break;
            }
        }

        // Parse pattern[x] - scan for any property starting with "pattern"
        foreach (var prop in elem.EnumerateObject())
        {
            if (prop.Name.StartsWith("pattern", StringComparison.Ordinal) && prop.Name.Length > 7)
            {
                ed.PatternValue = prop.Value.Clone();
                break;
            }
        }

        // Parse slicing
        if (elem.TryGetProperty("slicing", out var slicing))
        {
            ed.Slicing = new FHIRSlicingDefinition
            {
                Rules = slicing.TryGetProperty("rules", out var rules) ? rules.GetString() ?? "open" : "open",
            };

            if (slicing.TryGetProperty("discriminator", out var discriminators) &&
                discriminators.ValueKind == JsonValueKind.Array)
            {
                ed.Slicing.Discriminator = discriminators.EnumerateArray().Select(d =>
                    new FHIRSlicingDiscriminator
                    {
                        Type = d.TryGetProperty("type", out var dt) ? dt.GetString() ?? "" : "",
                        Path = d.TryGetProperty("path", out var dp) ? dp.GetString() ?? "" : "",
                    }).ToList();
            }
        }

        return ed;
    }

    private const string FhirTypeExtensionUrl =
        "http://hl7.org/fhir/StructureDefinition/structuredefinition-fhir-type";

    // FHIRPath system-type URLs -> FHIR primitive codes. R4 snapshots type
    // Resource.id/Element.id and primitive .value elements this way instead of
    // a literal FHIR code.
    private static readonly Dictionary<string, string> FhirPathPrimitiveMap = new(StringComparer.Ordinal)
    {
        ["http://hl7.org/fhirpath/System.String"] = "string",
        ["http://hl7.org/fhirpath/System.Boolean"] = "boolean",
        ["http://hl7.org/fhirpath/System.Integer"] = "integer",
        ["http://hl7.org/fhirpath/System.Decimal"] = "decimal",
        ["http://hl7.org/fhirpath/System.Date"] = "date",
        ["http://hl7.org/fhirpath/System.DateTime"] = "dateTime",
        ["http://hl7.org/fhirpath/System.Time"] = "time",
    };

    /// <summary>
    /// Resolve a type entry's FHIR code. R4 snapshots express FHIRPath-typed
    /// primitives in two renditions: <c>code</c> holds the fhirpath URL with the
    /// plain name in a structuredefinition-fhir-type extension on the type object
    /// (the spec-bundle rendition), or <c>code</c> is absent and the extension
    /// sits under <c>_code</c> (package renditions). Both normalize to the plain
    /// primitive code; ordinary literal codes pass through untouched.
    /// </summary>
    private static string ResolveTypeCode(JsonElement t)
    {
        var code = t.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
        if (code.Length > 0 && !code.StartsWith("http://hl7.org/fhirpath/", StringComparison.Ordinal))
            return code;

        var extensionValue = ReadFhirTypeExtension(t, "extension") ?? ReadFhirTypeExtension(t, "_code");
        var resolved = extensionValue ?? code;
        return FhirPathPrimitiveMap.TryGetValue(resolved, out var mapped) ? mapped : resolved;
    }

    private static string? ReadFhirTypeExtension(JsonElement t, string propertyName)
    {
        if (!t.TryGetProperty(propertyName, out var container)) return null;

        // "extension" is the array itself; "_code" wraps one under an object.
        var extensions = container;
        if (container.ValueKind == JsonValueKind.Object &&
            container.TryGetProperty("extension", out var nested))
        {
            extensions = nested;
        }
        if (extensions.ValueKind != JsonValueKind.Array) return null;

        foreach (var ext in extensions.EnumerateArray())
        {
            if (ext.ValueKind != JsonValueKind.Object) continue;
            if (!ext.TryGetProperty("url", out var url) ||
                url.ValueKind != JsonValueKind.String ||
                url.GetString() != FhirTypeExtensionUrl)
            {
                continue;
            }

            if (ext.TryGetProperty("valueUrl", out var valueUrl) &&
                valueUrl.ValueKind == JsonValueKind.String)
            {
                return valueUrl.GetString();
            }
            if (ext.TryGetProperty("valueUri", out var valueUri) &&
                valueUri.ValueKind == JsonValueKind.String)
            {
                return valueUri.GetString();
            }
        }
        return null;
    }

    /// <summary>
    /// Map a <c>ValueSet</c> resource JSON into an <see cref="FHIRValueSet"/>:
    /// enumerated codes from <c>compose.include[].concept[]</c> and (winning
    /// over compose, as the computed answer) <c>expansion.contains[]</c>;
    /// whole-system includes; imported ValueSet URLs; and an unexpandable flag
    /// for filters/excludes so membership stays honest about what it cannot
    /// decide offline. Returns null when the ValueSet has no canonical URL.
    /// </summary>
    public static FHIRValueSet? ParseValueSet(JsonElement root)
    {
        var vs = new FHIRValueSet
        {
            Url = root.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
            Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
        };

        if (string.IsNullOrEmpty(vs.Url)) return null;

        if (root.TryGetProperty("expansion", out var expansion) &&
            expansion.TryGetProperty("contains", out var contains) &&
            contains.ValueKind == JsonValueKind.Array)
        {
            // A pre-computed expansion is the ValueSet's authoritative content;
            // the compose it was computed from adds nothing. A partial
            // expansion (total beyond the flattened entries) stays undecidable
            // in the negative.
            var codes = new List<FHIRValueSetCode>();
            FlattenExpansionContains(contains, codes);
            vs.Codes = codes;
            vs.HasUnexpandableContent =
                expansion.TryGetProperty("total", out var total) &&
                total.ValueKind == JsonValueKind.Number &&
                total.GetInt64() != codes.Count;
            return vs;
        }

        if (root.TryGetProperty("compose", out var compose))
        {
            if (compose.TryGetProperty("include", out var includes) &&
                includes.ValueKind == JsonValueKind.Array)
            {
                ParseComposeIncludes(includes, vs);
            }

            // Excludes are not applied (they would require subtracting from
            // content we may not have enumerated), so their presence makes
            // negative verdicts unsafe.
            if (compose.TryGetProperty("exclude", out var excludes) &&
                excludes.ValueKind == JsonValueKind.Array &&
                excludes.GetArrayLength() > 0)
            {
                vs.HasUnexpandableContent = true;
            }
        }

        return vs;
    }

    private static void ParseComposeIncludes(JsonElement includes, FHIRValueSet vs)
    {
        var codes = new List<FHIRValueSetCode>();
        var wholeSystems = new List<string>();
        var imports = new List<string>();

        foreach (var include in includes.EnumerateArray())
        {
            var system = include.TryGetProperty("system", out var sys) ? sys.GetString() ?? "" : "";
            if (system.Length > 0)
                vs.Systems.Add(system);

            var hasConcepts = include.TryGetProperty("concept", out var concepts) &&
                concepts.ValueKind == JsonValueKind.Array;
            var hasFilter = include.TryGetProperty("filter", out var filter) &&
                filter.ValueKind == JsonValueKind.Array && filter.GetArrayLength() > 0;
            var hasImports = include.TryGetProperty("valueSet", out var imported) &&
                imported.ValueKind == JsonValueKind.Array && imported.GetArrayLength() > 0;

            if (hasConcepts)
            {
                foreach (var concept in concepts.EnumerateArray())
                {
                    codes.Add(new FHIRValueSetCode
                    {
                        System = system,
                        Code = concept.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "",
                        Display = concept.TryGetProperty("display", out var d) ? d.GetString() : null,
                    });
                }
            }
            else if (hasFilter)
            {
                // Intensional: needs a terminology server or CodeSystem
                // enumeration to expand — undecidable offline.
                vs.HasUnexpandableContent = true;
            }
            else if (hasImports)
            {
                foreach (var import in imported.EnumerateArray())
                {
                    var importUrl = import.GetString();
                    if (!string.IsNullOrEmpty(importUrl))
                        imports.Add(importUrl);
                }
                // system + valueSet together means "codes of this system that
                // are ALSO in those ValueSets" — an intersection we don't
                // implement; treat the include as whole-system + import union,
                // flagged undecidable in the negative.
                if (system.Length > 0)
                    vs.HasUnexpandableContent = true;
            }
            else if (system.Length > 0)
            {
                wholeSystems.Add(system);
            }
        }

        vs.Codes = codes;
        vs.WholeSystemIncludes = wholeSystems;
        vs.ImportedValueSets = imports;
    }

    /// <summary>
    /// Map a <c>CodeSystem</c> resource JSON into an <see cref="FHIRCodeSystem"/>:
    /// canonical URL plus the flattened (recursive) concept code list. Returns null
    /// when the CodeSystem has no canonical URL. Concept-less CodeSystems (e.g.
    /// <c>content: not-present</c>) parse to an empty code list, which callers treat
    /// as non-enumerable.
    /// </summary>
    public static FHIRCodeSystem? ParseCodeSystem(JsonElement root)
    {
        var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
        if (string.IsNullOrEmpty(url)) return null;

        var codes = new List<string>();
        if (root.TryGetProperty("concept", out var concepts) && concepts.ValueKind == JsonValueKind.Array)
            FlattenCodeSystemConcepts(concepts, codes);

        return new FHIRCodeSystem { Url = url, Codes = codes };
    }

    private static void FlattenCodeSystemConcepts(JsonElement concepts, List<string> codes)
    {
        foreach (var concept in concepts.EnumerateArray())
        {
            if (concept.TryGetProperty("code", out var c) && c.GetString() is { Length: > 0 } code)
                codes.Add(code);
            if (concept.TryGetProperty("concept", out var nested) && nested.ValueKind == JsonValueKind.Array)
                FlattenCodeSystemConcepts(nested, codes);
        }
    }

    private static void FlattenExpansionContains(JsonElement contains, List<FHIRValueSetCode> codes)
    {
        foreach (var entry in contains.EnumerateArray())
        {
            var code = entry.TryGetProperty("code", out var c) ? c.GetString() : null;
            if (!string.IsNullOrEmpty(code))
            {
                codes.Add(new FHIRValueSetCode
                {
                    System = entry.TryGetProperty("system", out var s) ? s.GetString() ?? "" : "",
                    Code = code,
                    Display = entry.TryGetProperty("display", out var d) ? d.GetString() : null,
                });
            }
            if (entry.TryGetProperty("contains", out var nested) && nested.ValueKind == JsonValueKind.Array)
                FlattenExpansionContains(nested, codes);
        }
    }
}
