// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Validates FHIR resources against StructureDefinition profiles.
/// </summary>
public interface IProfileValidator
{
    Task<Result<FHIRConformanceResult>> ValidateAsync(string resourceJson, string profileUrl);
    Task<Result<FHIRConformanceResult>> ValidateAsync(string resourceJson, FHIRStructureDefinition profile);
}

/// <summary>
/// Walks a FHIR resource against its StructureDefinition checking cardinality,
/// data types, fixed/pattern values, and bindings. Sliced elements are handed to
/// <see cref="SliceScopeValidator"/>, which owns every definition whose element id
/// names a slice.
/// </summary>
public class ProfileValidator : IProfileValidator
{
    private readonly IStructureDefinitionLoader _loader;
    private readonly IValueSetValidator _valueSetValidator;
    private readonly FhirInvariantEvaluator _invariantEvaluator;
    private readonly ILogger<ProfileValidator> _logger;

    public ProfileValidator(
        IStructureDefinitionLoader loader,
        IValueSetValidator valueSetValidator,
        FhirInvariantEvaluator invariantEvaluator,
        ILogger<ProfileValidator> logger)
    {
        _loader = loader;
        _valueSetValidator = valueSetValidator;
        _invariantEvaluator = invariantEvaluator;
        _logger = logger;
    }

    public async Task<Result<FHIRConformanceResult>> ValidateAsync(string resourceJson, string profileUrl)
    {
        var profileResult = await _loader.LoadAsync(profileUrl);
        if (profileResult.IsFailure)
        {
            return Result<FHIRConformanceResult>.Failure(
                Error.Create("PROFILE_NOT_FOUND", $"Could not load profile: {profileUrl}"));
        }

        return await ValidateAsync(resourceJson, profileResult.Value);
    }

    public async Task<Result<FHIRConformanceResult>> ValidateAsync(string resourceJson, FHIRStructureDefinition profile)
    {
        try
        {
            using var doc = JsonDocument.Parse(resourceJson);
            var root = doc.RootElement;
            var diagnostics = new List<FHIRDiagnostic>();

            // Build element map from the resource JSON
            var elementMap = BuildElementMap(root, profile.Type);

            // Root-element invariants (dom-*, resource-level rules like obs-6/us-core-2)
            // evaluate against the resource itself. The root stays skipped for the other
            // passes, where cardinality/type/fixed checks are meaningless.
            var rootDef = profile.Elements.FirstOrDefault(e => e.Path == profile.Type);
            if (rootDef != null && rootDef.Constraints.Count > 0)
                _invariantEvaluator.Validate(root, rootDef, new[] { (root, (string?)null) }, diagnostics);

            // Validate each element definition
            foreach (var elementDef in profile.Elements)
            {
                // Skip the root element
                if (elementDef.Path == profile.Type) continue;

                // Skip slice definitions and every definition beneath one. Their paths are
                // the plain unsliced paths, so applying them here would constrain every
                // instance at that path instead of the slice's own. SliceScopeValidator
                // runs them against exactly the instances their slice claims.
                if (elementDef.SliceName != null) continue;
                if (SliceScopeValidator.NamesASlice(elementDef)) continue;

                // Resolve the element path, handling choice types
                var elements = GetElementsAtPath(elementMap, elementDef);

                // Pass 1: Check cardinality per parent element instance
                ElementCardinalityEvaluator.Validate(root, elementDef, profile.Type, diagnostics);

                // Pass 1b: Must-support warning. Only fires when cardinality
                // didn't already flag an error (min=0 case). An absent
                // must-support element is a compliance concern, not a
                // conformance failure — emit a Warning so IsValid stays true
                // while still surfacing the gap to operators. Presence anywhere
                // in the resource satisfies must-support, so the aggregate
                // count is the right question here.
                if (elementDef.MustSupport && elements.Count == 0 && elementDef.Min == 0)
                {
                    diagnostics.Add(new FHIRDiagnostic(
                        FHIRDiagnosticSeverity.Warning,
                        elementDef.Path,
                        $"Must-support element '{elementDef.Path}' is not populated",
                        Expected: "must-support: true",
                        Actual: "not populated",
                        Fix: $"A conformant implementation should populate '{elementDef.Path}' when source data is available"
                    ) { IsMustSupportGap = true });
                }

                // Only validate further if elements exist
                if (elements.Count == 0) continue;

                // Pass 2: Check data types
                foreach (var (elem, _) in elements)
                {
                    ElementConstraintEvaluator.ValidateDataType(elem, elementDef, diagnostics);
                }

                // Pass 3: Check fixed/pattern values
                if (elementDef.FixedValue.HasValue)
                {
                    foreach (var (elem, _) in elements)
                    {
                        ElementConstraintEvaluator.ValidateFixedValue(elem, elementDef, diagnostics);
                    }
                }

                if (elementDef.PatternValue.HasValue)
                {
                    foreach (var (elem, _) in elements)
                    {
                        ElementConstraintEvaluator.ValidatePatternValue(elem, elementDef, diagnostics);
                    }
                }

                // Pass 4: Check bindings
                if (elementDef.Binding != null)
                {
                    foreach (var (elem, _) in elements)
                    {
                        var diagnostic = await _valueSetValidator.ValidateBindingAsync(
                            elem, elementDef.Binding, elementDef.Path);
                        if (diagnostic != null)
                            diagnostics.Add(diagnostic);
                    }
                }

                // Pass 5: constraint invariants per element instance.
                if (elementDef.Constraints.Count > 0)
                    _invariantEvaluator.Validate(root, elementDef, elements, diagnostics);
            }

            // Validate sliced elements, scoped per parent instance
            await SliceScopeValidator.ValidateAsync(
                root, profile, _valueSetValidator, _invariantEvaluator, diagnostics);

            return Result<FHIRConformanceResult>.Success(
                FHIRConformanceResult.FromDiagnostics(diagnostics));
        }
        catch (JsonException ex)
        {
            return Result<FHIRConformanceResult>.Failure(
                Error.Create("INVALID_JSON", $"Invalid FHIR JSON: {ex.Message}"));
        }
    }

    private Dictionary<string, List<JsonElement>> BuildElementMap(JsonElement root, string resourceType)
    {
        var map = new Dictionary<string, List<JsonElement>>();
        TraverseElement(root, resourceType, map);
        return map;
    }

    private void TraverseElement(JsonElement element, string currentPath, Dictionary<string, List<JsonElement>> map)
    {
        if (!map.TryGetValue(currentPath, out var list))
        {
            list = new List<JsonElement>();
            map[currentPath] = list;
        }
        list.Add(element);

        if (element.ValueKind != JsonValueKind.Object) return;

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name == "resourceType") continue;

            var childPath = $"{currentPath}.{prop.Name}";

            if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.Value.EnumerateArray())
                {
                    TraverseElement(item, childPath, map);
                }
                // Also store the array itself at the path for cardinality counting
                if (!map.ContainsKey(childPath))
                    map[childPath] = new List<JsonElement>();
            }
            else
            {
                TraverseElement(prop.Value, childPath, map);
            }
        }
    }

    // Choice-type instances carry the matched key suffix (valueQuantity → "Quantity") —
    // the declared type the invariant evaluator threads into FHIRPath type checks.
    private static List<(JsonElement Element, string? ChoiceType)> GetElementsAtPath(
        Dictionary<string, List<JsonElement>> map,
        FHIRElementDefinition elementDef)
    {
        if (map.TryGetValue(elementDef.Path, out var elements))
            return elements.Select(e => (e, (string?)null)).ToList();

        if (elementDef.Path.EndsWith("[x]"))
        {
            var basePath = elementDef.Path[..^3];
            var result = new List<(JsonElement, string?)>();
            foreach (var suffix in ElementCardinalityEvaluator.ChoiceTypeSuffixes)
            {
                var path = $"{basePath}{suffix}";
                if (map.TryGetValue(path, out var typed))
                    result.AddRange(typed.Select(e => (e, (string?)suffix)));
            }
            return result;
        }

        return new List<(JsonElement, string?)>();
    }
}
