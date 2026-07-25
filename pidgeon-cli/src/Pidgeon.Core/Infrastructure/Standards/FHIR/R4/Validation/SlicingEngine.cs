// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Decides whether a given resource element matches a profile slice definition
/// by walking the slice entry's <see cref="FHIRSlicingDefinition.Discriminator"/>
/// list and evaluating each against the instance.
///
/// <para>
/// The FHIR R4 slicing model supports five discriminator types:
/// </para>
///
/// <list type="table">
///   <listheader>
///     <term>Type</term>
///     <description>Semantics and support</description>
///   </listheader>
///   <item>
///     <term><c>value</c></term>
///     <description>Strict equality between the instance value at the
///     discriminator path and the slice's fixed/pattern constraint at the
///     same path.</description>
///   </item>
///   <item>
///     <term><c>pattern</c></term>
///     <description>Subset match: instance must contain every property the
///     slice's pattern specifies at the discriminator path.</description>
///   </item>
///   <item>
///     <term><c>exists</c></term>
///     <description>Slice matches based on the presence or absence of the
///     discriminator path on the instance.</description>
///   </item>
///   <item>
///     <term><c>type</c></term>
///     <description>Slice identified by the instance's own type tag at the
///     discriminator path — the choice-key suffix of a choice element, or a
///     resource's <c>resourceType</c>.</description>
///   </item>
///   <item>
///     <term><c>profile</c></term>
///     <description>Slice identified by declared conformance to a profile
///     the slice references (<c>meta.profile</c>).</description>
///   </item>
/// </list>
///
/// <para>
/// The <c>value</c>, <c>pattern</c>, <c>type</c> and <c>profile</c>
/// discriminators name a path whose constraint lives on the slice's child
/// element definition, not on the slice element itself. Callers supply a
/// <c>definitionAt</c> resolver for that lookup rather than the engine reaching
/// for a StructureDefinition loader; without one it falls back to the slice's
/// own whole-element pattern/fixed value and refuses anything it cannot decide.
/// </para>
///
/// <para>
/// Multi-discriminator slices: FHIR mandates AND semantics — all
/// discriminators must match for the slice to claim the element. An
/// undecidable discriminator is treated as a non-match (safest failure mode;
/// never over-match).
/// </para>
/// </summary>
internal static class SlicingEngine
{
    /// <summary>
    /// True when the element matches the slice definition under every
    /// discriminator declared by the slice entry. Falls through to the
    /// caller's pattern / fixed-value fallbacks when the slice entry
    /// declares no discriminators.
    /// </summary>
    /// <param name="choiceType">
    /// The choice-key suffix the instance was resolved through (<c>timingDate</c>
    /// → <c>"Date"</c>), or null for a non-choice element. The <c>type</c>
    /// discriminator on <c>$this</c> reads it.
    /// </param>
    /// <param name="definitionAt">
    /// Resolves a discriminator path (relative to the slice element, or
    /// <c>$this</c>) to the slice's element definition at that path.
    /// </param>
    public static bool MatchesSliceByDiscriminator(
        JsonElement element,
        FHIRElementDefinition sliceDef,
        FHIRElementDefinition sliceEntry,
        string? choiceType = null,
        Func<string, FHIRElementDefinition?>? definitionAt = null)
    {
        if (sliceEntry.Slicing is null || sliceEntry.Slicing.Discriminator.Count == 0)
            return false;

        foreach (var disc in sliceEntry.Slicing.Discriminator)
        {
            if (!MatchesDiscriminator(element, sliceDef, disc, choiceType, definitionAt))
                return false;
        }

        // All discriminators matched.
        return true;
    }

    private static bool MatchesDiscriminator(
        JsonElement element,
        FHIRElementDefinition sliceDef,
        FHIRSlicingDiscriminator disc,
        string? choiceType,
        Func<string, FHIRElementDefinition?>? definitionAt)
    {
        var declared = disc.Path == "$this" ? sliceDef : definitionAt?.Invoke(disc.Path);

        return disc.Type switch
        {
            "value" => MatchValueOrPattern(element, sliceDef, declared, disc.Path, strict: true),
            "pattern" => MatchValueOrPattern(element, sliceDef, declared, disc.Path, strict: false),
            "exists" => MatchExists(element, disc.Path),
            "type" => MatchType(element, choiceType, declared, disc.Path),
            "profile" => MatchProfile(element, declared, disc.Path),
            _ => false,
        };
    }

    private static bool MatchValueOrPattern(
        JsonElement element,
        FHIRElementDefinition sliceDef,
        FHIRElementDefinition? declaredAtPath,
        string discriminatorPath,
        bool strict)
    {
        // $this means "compare the element itself against the slice constraint."
        // For non-$this, navigate both sides to the same sub-path.
        var elementAtPath = discriminatorPath == "$this"
            ? (JsonElement?)element
            : NavigateToPath(element, discriminatorPath);
        if (elementAtPath is null)
            return false;

        // The slice's constraint is expressed either as a whole-element pattern
        // on the slice itself (patternIdentifier on Patient.identifier:MR), which
        // navigates down to the discriminator path, or on the child element
        // definition the discriminator names (patternCodeableConcept on
        // Claim.supportingInfo:PatientEvent.category). Whole-element wins.
        var sliceConstraint = NavigatedConstraint(sliceDef, discriminatorPath)
            ?? (discriminatorPath == "$this" ? null : ConstraintOf(declaredAtPath));

        if (sliceConstraint is null)
            return false;

        return strict
            ? ElementConstraintEvaluator.JsonElementEquals(elementAtPath.Value, sliceConstraint.Value)
            : ElementConstraintEvaluator.JsonElementMatchesPattern(elementAtPath.Value, sliceConstraint.Value);
    }

    private static JsonElement? NavigatedConstraint(FHIRElementDefinition sliceDef, string discriminatorPath)
    {
        var whole = ConstraintOf(sliceDef);
        if (whole is null) return null;
        return discriminatorPath == "$this" ? whole : NavigateToPath(whole.Value, discriminatorPath);
    }

    private static JsonElement? ConstraintOf(FHIRElementDefinition? def) =>
        def is null ? null : def.PatternValue ?? def.FixedValue;

    private static bool MatchExists(JsonElement element, string discriminatorPath)
    {
        // FHIR's exists discriminator is defined on slice elements themselves
        // as min=0 / min=1. For the common case where a slice declares the
        // discriminator path with min≥1, the semantic collapses to "element
        // has this sub-path populated." Pidgeon's current model doesn't
        // surface per-slice min/max on child paths yet, so we treat exists
        // as "the path resolves to something non-null on the instance."
        if (discriminatorPath == "$this")
            return element.ValueKind != JsonValueKind.Null
                && element.ValueKind != JsonValueKind.Undefined;

        return NavigateToPath(element, discriminatorPath) is not null;
    }

    // ponytail: the type discriminator reads the instance's own type tag — the choice-key
    // suffix a choice element was resolved through ($this on timing[x] -> timingDate ->
    // "Date"), or `resourceType` for a resource-valued path (Bundle.entry.resource) — and
    // compares it to the type code the slice declares at that path. Ceiling: two slices
    // declaring the SAME type code under different profiles are indistinguishable here, and
    // a profile-only subtype (Bundle.entry sliced on a constrained Claim profile) resolves
    // to the base resource name. Upgrade path: thread the loaded StructureDefinition's Type
    // in from ProfileValidator and prefer it over the declared code.
    private static bool MatchType(
        JsonElement element,
        string? choiceType,
        FHIRElementDefinition? declaredAtPath,
        string discriminatorPath)
    {
        if (declaredAtPath is null || declaredAtPath.Types.Count == 0)
            return false;

        var actual = discriminatorPath == "$this"
            ? choiceType ?? ResourceTypeOf(element)
            : ResourceTypeOf(NavigateToPath(element, discriminatorPath));

        if (actual is null)
            return false;

        return declaredAtPath.Types.Any(t => string.Equals(t.Code, actual, StringComparison.OrdinalIgnoreCase));
    }

    // ponytail: honest-minimal profile discriminator — an instance is claimed only when it
    // DECLARES conformance (meta.profile carries the slice's referenced profile URL, version
    // suffix ignored). Ceiling: an instance that conforms to the profile without declaring it
    // is left unclaimed, so the operator sees "slice X requires min N, found 0" rather than a
    // silent over-match — the same safe-failure default the engine has always taken. Recursive
    // validation of the candidate against the referenced StructureDefinition, which is what the
    // official validator does, is deliberately out of scope. Upgrade path: pass a resolved
    // profile-url -> type map in from ProfileValidator, then recurse through IProfileValidator.
    private static bool MatchProfile(
        JsonElement element,
        FHIRElementDefinition? declaredAtPath,
        string discriminatorPath)
    {
        var profiles = declaredAtPath?.Types.SelectMany(t => t.Profile).ToList();
        if (profiles is null || profiles.Count == 0)
            return false;

        var node = discriminatorPath == "$this" ? element : NavigateToPath(element, discriminatorPath);
        if (node is null || node.Value.ValueKind != JsonValueKind.Object)
            return false;

        if (!node.Value.TryGetProperty("meta", out var meta) ||
            !meta.TryGetProperty("profile", out var claimed) ||
            claimed.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return claimed.EnumerateArray().Any(p =>
            p.ValueKind == JsonValueKind.String &&
            profiles.Contains(p.GetString()!.Split('|')[0], StringComparer.Ordinal));
    }

    private static string? ResourceTypeOf(JsonElement? node) =>
        node is { ValueKind: JsonValueKind.Object } obj &&
        obj.TryGetProperty("resourceType", out var rt) &&
        rt.ValueKind == JsonValueKind.String
            ? rt.GetString()
            : null;

    /// <summary>
    /// Navigate a JSON element along a dotted path, descending through
    /// objects and taking the first entry of any array encountered. Returns
    /// null when any segment is missing or resolves to a non-object/non-array
    /// intermediate. Shared with <see cref="SliceScopeValidator"/>'s path walks
    /// so the two stay in sync on how paths are interpreted.
    /// </summary>
    internal static JsonElement? NavigateToPath(JsonElement element, string path)
    {
        if (string.IsNullOrEmpty(path))
            return element;

        var segments = path.Split('.');
        var current = element;

        foreach (var segment in segments)
        {
            if (current.ValueKind == JsonValueKind.Array)
            {
                // Take the first element of the array and continue. FHIR
                // discriminator semantics: the discriminator must be unique
                // per slice, so any single entry is representative.
                using var enumerator = current.EnumerateArray().GetEnumerator();
                if (!enumerator.MoveNext())
                    return null;
                current = enumerator.Current;
            }

            if (current.ValueKind != JsonValueKind.Object)
                return null;

            if (!current.TryGetProperty(segment, out var child))
                return null;

            current = child;
        }

        return current;
    }
}
