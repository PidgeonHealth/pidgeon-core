// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Validates a profile's sliced elements within the scope each slice actually owns.
///
/// <para>
/// A slice entry is resolved inside every instance of its parent element: slice
/// min/max are counted within that parent rather than summed across its repeats,
/// an absent parent contributes no min violation, and <c>rules: closed</c> makes an
/// instance that no slice claims an error. Instances a slice does claim then receive
/// that slice's own FHIRPath invariants and its child element definitions — the
/// definitions the main element walk skips because their <c>id</c> carries slice
/// context (<c>Claim.item.extension:nursingHome.url</c>) while their <c>path</c> does
/// not. This is their one legitimate execution path.
/// </para>
///
/// <para>
/// Slice entries nested inside a slice (<c>Claim.supportingInfo:PatientEvent.timing[x]</c>)
/// recurse through the same routine, scoped to the claimed instance. Slice definitions are
/// therefore keyed by element <c>id</c>, not by path: five supportingInfo slices each declare
/// a <c>timing[x]</c> entry at the same path and only the id tells them apart. Profiles built
/// without ids (hand-assembled in tests) fall back to path keying, which is unambiguous there.
/// </para>
/// </summary>
internal static class SliceScopeValidator
{
    private static readonly char[] SliceIdSeparators = { '.', ':' };

    public static async Task ValidateAsync(
        JsonElement root,
        FHIRStructureDefinition profile,
        IValueSetValidator valueSetValidator,
        FhirInvariantEvaluator invariantEvaluator,
        List<FHIRDiagnostic> diagnostics)
    {
        // Nested entries are reached through their owning slice, never from the top.
        foreach (var entry in profile.Elements.Where(e => e.IsSliceEntry && !NamesASlice(e)))
        {
            await ValidateEntryAsync(
                root, root, profile.Type, entry, profile, valueSetValidator, invariantEvaluator, diagnostics);
        }
    }

    /// <summary>True when the definition's id carries slice context.</summary>
    internal static bool NamesASlice(FHIRElementDefinition def) =>
        def.Id != null && def.Id.Contains(':', StringComparison.Ordinal);

    private static async Task ValidateEntryAsync(
        JsonElement root,
        JsonElement context,
        string contextPath,
        FHIRElementDefinition entry,
        FHIRStructureDefinition profile,
        IValueSetValidator valueSetValidator,
        FhirInvariantEvaluator invariantEvaluator,
        List<FHIRDiagnostic> diagnostics)
    {
        var sliceDefs = SliceDefsOf(profile, entry);
        if (sliceDefs.Count == 0) return;
        if (!entry.Path.StartsWith(contextPath + ".", StringComparison.Ordinal)) return;

        var segments = entry.Path[(contextPath.Length + 1)..].Split('.');
        var leaf = segments[^1];
        var closed = string.Equals(entry.Slicing!.Rules, "closed", StringComparison.Ordinal);

        foreach (var parent in ElementCardinalityEvaluator.EnumerateParentInstances(context, segments))
        {
            var instances = InstancesAt(parent, leaf);
            var claimed = new bool[instances.Count];

            foreach (var sliceDef in sliceDefs)
            {
                var matched = new List<(JsonElement Element, string? ChoiceType)>();
                for (var i = 0; i < instances.Count; i++)
                {
                    if (!MatchesSlice(instances[i], sliceDef, entry, profile)) continue;
                    matched.Add(instances[i]);
                    claimed[i] = true;
                }

                ReportSliceCardinality(sliceDef, matched.Count, diagnostics);
                if (matched.Count == 0) continue;

                if (sliceDef.Constraints.Count > 0)
                    invariantEvaluator.Validate(root, sliceDef, matched, diagnostics);

                foreach (var (instance, _) in matched)
                {
                    await ValidateSliceChildrenAsync(
                        root, instance, sliceDef, profile, valueSetValidator, invariantEvaluator, diagnostics);
                }
            }

            if (!closed) continue;

            for (var i = 0; i < instances.Count; i++)
            {
                if (claimed[i]) continue;
                diagnostics.Add(new FHIRDiagnostic(
                    FHIRDiagnosticSeverity.Error,
                    entry.Path,
                    $"Element at '{entry.Path}' matches no slice and the slicing is closed",
                    Expected: $"Slice rules: closed ({string.Join(" | ", sliceDefs.Select(s => s.SliceName))})",
                    Actual: instances[i].Element.GetRawText(),
                    Fix: $"Remove the element, or conform it to one of the slices declared at '{entry.Path}'"
                ));
            }
        }
    }

    // The slice's child definitions, applied to one instance the slice claimed.
    private static async Task ValidateSliceChildrenAsync(
        JsonElement root,
        JsonElement instance,
        FHIRElementDefinition sliceDef,
        FHIRStructureDefinition profile,
        IValueSetValidator valueSetValidator,
        FhirInvariantEvaluator invariantEvaluator,
        List<FHIRDiagnostic> diagnostics)
    {
        foreach (var child in ChildDefsOf(profile, sliceDef))
        {
            if (child.IsSliceEntry)
            {
                await ValidateEntryAsync(
                    root, instance, sliceDef.Path, child, profile, valueSetValidator, invariantEvaluator, diagnostics);
            }

            ElementCardinalityEvaluator.Validate(instance, child, sliceDef.Path, diagnostics);

            var childInstances = ResolveRelative(instance, child.Path[(sliceDef.Path.Length + 1)..]);
            if (childInstances.Count == 0) continue;

            foreach (var (element, _) in childInstances)
            {
                ElementConstraintEvaluator.ValidateDataType(element, child, diagnostics);
                ElementConstraintEvaluator.ValidateFixedValue(element, child, diagnostics);
                ElementConstraintEvaluator.ValidatePatternValue(element, child, diagnostics);

                if (child.Binding == null) continue;
                var binding = await valueSetValidator.ValidateBindingAsync(element, child.Binding, child.Path);
                if (binding != null) diagnostics.Add(binding);
            }

            if (child.Constraints.Count > 0)
                invariantEvaluator.Validate(root, child, childInstances, diagnostics);
        }
    }

    private static void ReportSliceCardinality(
        FHIRElementDefinition sliceDef, int count, List<FHIRDiagnostic> diagnostics)
    {
        // "Slice min:" / "Slice max:" keep slice-sourced cardinality distinguishable from the
        // plain element cardinality ElementCardinalityEvaluator reports at the same paths.
        var path = $"{sliceDef.Path}:{sliceDef.SliceName}";

        if (count < sliceDef.Min)
        {
            diagnostics.Add(new FHIRDiagnostic(
                FHIRDiagnosticSeverity.Error,
                path,
                $"Slice '{sliceDef.SliceName}' requires at least {sliceDef.Min} matching element(s), found {count}",
                Expected: $"Slice min: {sliceDef.Min}",
                Actual: $"found: {count}",
                Fix: $"Add element matching slice '{sliceDef.SliceName}'"
            ));
        }

        if (sliceDef.Max != "*" && int.TryParse(sliceDef.Max, out var maxVal) && count > maxVal)
        {
            diagnostics.Add(new FHIRDiagnostic(
                FHIRDiagnosticSeverity.Error,
                path,
                $"Slice '{sliceDef.SliceName}' has {count} matching element(s) but maximum is {maxVal}",
                Expected: $"Slice max: {sliceDef.Max}",
                Actual: $"found: {count}"
            ));
        }
    }

    internal static bool MatchesSlice(
        (JsonElement Element, string? ChoiceType) instance,
        FHIRElementDefinition sliceDef,
        FHIRElementDefinition sliceEntry,
        FHIRStructureDefinition profile)
    {
        // Extension slices match by the extension's canonical URL, not by
        // pattern or fixed value. IGs like US Core declare slices such as
        // Patient.extension:race with a type[0].profile pointing at the
        // extension's StructureDefinition; on the wire the match is
        // extension.url == slice type.profile.
        if (ExtensionValidator.IsExtensionSlice(sliceDef))
        {
            return ExtensionValidator.MatchesExtensionSlice(instance.Element, sliceDef);
        }

        // Discriminator-driven matching when the slice entry declares one.
        // This is the modern path — US Core and Da Vinci profiles always
        // declare at least one discriminator, so non-extension slices go
        // through SlicingEngine. Older profiles without a discriminator
        // block fall through to the whole-element fallbacks below.
        if (sliceEntry.Slicing?.Discriminator.Count > 0)
        {
            return SlicingEngine.MatchesSliceByDiscriminator(
                instance.Element, sliceDef, sliceEntry, instance.ChoiceType,
                discriminatorPath => DefinitionAt(profile, sliceDef, discriminatorPath));
        }

        if (sliceDef.PatternValue.HasValue)
            return ElementConstraintEvaluator.JsonElementMatchesPattern(instance.Element, sliceDef.PatternValue.Value);

        if (sliceDef.FixedValue.HasValue)
            return ElementConstraintEvaluator.JsonElementEquals(instance.Element, sliceDef.FixedValue.Value);

        return false;
    }

    private static FHIRElementDefinition? DefinitionAt(
        FHIRStructureDefinition profile, FHIRElementDefinition sliceDef, string discriminatorPath)
    {
        if (discriminatorPath == "$this") return sliceDef;

        var target = $"{sliceDef.Path}.{discriminatorPath}";
        return ChildDefsOf(profile, sliceDef).FirstOrDefault(c => c.Path == target);
    }

    // Definitions of the slices declared under one slice entry. Keyed by id
    // (`Claim.supportingInfo:PatientEvent.timing[x]` + `:timingDate`) so sibling
    // entries sharing a path stay apart; path-keyed only for id-less profiles.
    private static List<FHIRElementDefinition> SliceDefsOf(
        FHIRStructureDefinition profile, FHIRElementDefinition entry)
    {
        if (entry.Id is null)
        {
            return profile.Elements
                .Where(e => e.SliceName != null && e.Path == entry.Path)
                .ToList();
        }

        var prefix = entry.Id + ":";
        return profile.Elements
            .Where(e => e.SliceName != null
                && e.Id != null
                && e.Id.StartsWith(prefix, StringComparison.Ordinal)
                && e.Id.IndexOfAny(SliceIdSeparators, prefix.Length) < 0)
            .ToList();
    }

    // Definitions constraining one slice's own instances. Everything under a
    // NESTED slice is excluded — those execute inside that slice's scope.
    private static List<FHIRElementDefinition> ChildDefsOf(
        FHIRStructureDefinition profile, FHIRElementDefinition sliceDef)
    {
        if (sliceDef.Id is null) return new List<FHIRElementDefinition>();

        var prefix = sliceDef.Id + ".";
        var pathPrefix = sliceDef.Path + ".";
        return profile.Elements
            .Where(e => e.SliceName == null
                && e.Id != null
                && e.Id.StartsWith(prefix, StringComparison.Ordinal)
                && e.Id.IndexOf(':', prefix.Length) < 0
                && e.Path.StartsWith(pathPrefix, StringComparison.Ordinal))
            .ToList();
    }

    // The instances of one element name directly under a parent object. A choice
    // element yields the suffix it was resolved through, which the type
    // discriminator and the invariant evaluator both read as the declared type.
    private static List<(JsonElement Element, string? ChoiceType)> InstancesAt(JsonElement parent, string leaf)
    {
        var result = new List<(JsonElement, string?)>();
        if (parent.ValueKind != JsonValueKind.Object) return result;

        if (leaf.EndsWith("[x]", StringComparison.Ordinal))
        {
            var baseName = leaf[..^3];
            foreach (var suffix in ElementCardinalityEvaluator.ChoiceTypeSuffixes)
                Collect(parent, baseName + suffix, suffix, result);
            return result;
        }

        Collect(parent, leaf, null, result);
        return result;
    }

    private static void Collect(
        JsonElement parent, string name, string? choiceType, List<(JsonElement, string?)> into)
    {
        if (!parent.TryGetProperty(name, out var value)) return;

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                into.Add((item, choiceType));
        }
        else if (value.ValueKind != JsonValueKind.Null)
        {
            into.Add((value, choiceType));
        }
    }

    private static List<(JsonElement Element, string? ChoiceType)> ResolveRelative(
        JsonElement instance, string relativePath)
    {
        var frontier = new List<(JsonElement Element, string? ChoiceType)> { (instance, null) };

        foreach (var segment in relativePath.Split('.'))
        {
            var next = new List<(JsonElement, string?)>();
            foreach (var (element, _) in frontier)
                next.AddRange(InstancesAt(element, segment));
            frontier = next;
        }

        return frontier;
    }
}
