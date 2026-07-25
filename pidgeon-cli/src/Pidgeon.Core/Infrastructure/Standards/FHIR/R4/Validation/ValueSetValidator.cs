// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Validates codes against FHIR ValueSets with binding strength support.
/// </summary>
public interface IValueSetValidator
{
    Task<FHIRDiagnostic?> ValidateBindingAsync(
        JsonElement element, FHIRElementBinding binding, string elementPath);
}

/// <summary>
/// Validates element bindings against loaded ValueSets with tri-state
/// membership: a violation is reported only when it is PROVABLE (the ValueSet
/// is fully enumerable offline and every coding is decidably absent). A
/// ValueSet that is not loaded or not expandable offline yields an explicit
/// "binding not validated" diagnostic — never a silent pass and never a
/// blanket rejection. CodeableConcept semantics are any-coding: one member
/// coding satisfies the binding.
/// </summary>
public class ValueSetValidator : IValueSetValidator
{
    private enum Membership { Member, NotMember, NotValidatable }

    private readonly IStructureDefinitionLoader _loader;
    private readonly ILogger<ValueSetValidator> _logger;

    public ValueSetValidator(
        IStructureDefinitionLoader loader,
        ILogger<ValueSetValidator> logger)
    {
        _loader = loader;
        _logger = logger;
    }

    public async Task<FHIRDiagnostic?> ValidateBindingAsync(
        JsonElement element, FHIRElementBinding binding, string elementPath)
    {
        if (string.Equals(binding.Strength, "example", StringComparison.OrdinalIgnoreCase))
            return null;

        var codings = ExtractCodings(element);
        if (codings.Count == 0)
        {
            // No code to validate — not an error for optional elements
            return null;
        }

        // No canonical URL to resolve — nothing checkable and nothing to name
        // in a "not validated" signal (rare; base-spec bindings always carry one).
        if (string.IsNullOrEmpty(binding.ValueSet))
            return null;

        IReadOnlyDictionary<string, FHIRValueSet> valueSets;
        if (_loader is StructureDefinitionLoader sdLoader)
        {
            await sdLoader.EnsureInitializedAsync();
            valueSets = sdLoader.GetValueSets();
        }
        else
        {
            valueSets = new Dictionary<string, FHIRValueSet>();
        }

        var sawNotValidatable = false;
        foreach (var (system, code) in codings)
        {
            var verdict = Evaluate(system, code, binding.ValueSet, valueSets,
                new HashSet<string>(StringComparer.Ordinal));
            if (verdict == Membership.Member)
                return null;
            if (verdict == Membership.NotValidatable)
                sawNotValidatable = true;
        }

        return sawNotValidatable
            ? NotValidatedDiagnostic(binding, elementPath, codings[0], valueSets)
            : ViolationDiagnostic(binding, elementPath, codings[0]);
    }

    /// <summary>
    /// Tri-state membership of one coding in one ValueSet, following
    /// resolvable imports recursively (cycle-guarded via <paramref name="visited"/>).
    /// </summary>
    private Membership Evaluate(
        string? system, string code, string valueSetUrl,
        IReadOnlyDictionary<string, FHIRValueSet> valueSets, HashSet<string> visited)
    {
        var url = StripVersion(valueSetUrl);
        if (!visited.Add(url))
            return Membership.NotValidatable;

        if (!valueSets.TryGetValue(url, out var vs))
            return Membership.NotValidatable;

        if (vs.Codes.Any(c =>
            string.Equals(c.Code, code, StringComparison.Ordinal) &&
            (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(c.System) ||
             string.Equals(c.System, system, StringComparison.Ordinal))))
        {
            return Membership.Member;
        }

        // ponytail: a whole-system include accepts ANY code claiming that
        // system — the ceiling is a garbage code in the right system passing
        // (the same blind spot the official validator has offline). Upgrade
        // path: enumerate concepts from loaded CodeSystem resources, which the
        // loader currently ignores at every load site.
        if (!string.IsNullOrEmpty(system) &&
            vs.WholeSystemIncludes.Contains(system, StringComparer.Ordinal))
        {
            return Membership.Member;
        }

        var importUndecidable = false;
        foreach (var import in vs.ImportedValueSets)
        {
            var verdict = Evaluate(system, code, import, valueSets, visited);
            if (verdict == Membership.Member)
                return Membership.Member;
            if (verdict == Membership.NotValidatable)
                importUndecidable = true;
        }

        // A bare code (no system) cannot be matched against whole-system
        // includes, so its absence is undecidable when any exist.
        if (vs.HasUnexpandableContent
            || importUndecidable
            || (string.IsNullOrEmpty(system) && vs.WholeSystemIncludes.Count > 0))
        {
            return Membership.NotValidatable;
        }

        return Membership.NotMember;
    }

    private FHIRDiagnostic NotValidatedDiagnostic(
        FHIRElementBinding binding, string elementPath, (string? System, string Code) first,
        IReadOnlyDictionary<string, FHIRValueSet> valueSets)
    {
        var required = string.Equals(binding.Strength, "required", StringComparison.OrdinalIgnoreCase);
        var reason = valueSets.ContainsKey(StripVersion(binding.ValueSet!))
            ? "ValueSet is not fully expandable offline (filters, imports, or excludes)"
            : "ValueSet is not loaded from any installed package";

        _logger.LogDebug(
            "Binding at {Path} not validated ({Reason}): {ValueSet}", elementPath, reason, binding.ValueSet);

        return new FHIRDiagnostic(
            required ? FHIRDiagnosticSeverity.Warning : FHIRDiagnosticSeverity.Information,
            elementPath,
            $"Binding for ValueSet '{binding.ValueSet}' was not validated: {reason}",
            Expected: $"Binding not validated: {binding.ValueSet}",
            Actual: $"code='{first.Code}', system='{first.System ?? "none"}'",
            Fix: required
                ? $"Install the package providing '{binding.ValueSet}' to enable terminology validation"
                : null
        );
    }

    private static FHIRDiagnostic ViolationDiagnostic(
        FHIRElementBinding binding, string elementPath, (string? System, string Code) first)
    {
        var severity = binding.Strength.ToLowerInvariant() switch
        {
            "required" => FHIRDiagnosticSeverity.Error,
            "extensible" => FHIRDiagnosticSeverity.Warning,
            "preferred" => FHIRDiagnosticSeverity.Information,
            _ => FHIRDiagnosticSeverity.Information,
        };

        return new FHIRDiagnostic(
            severity,
            elementPath,
            $"Code '{first.Code}' (system: {first.System ?? "none"}) is not in the bound ValueSet",
            Expected: $"A code from ValueSet: {binding.ValueSet ?? "unknown"}",
            Actual: $"code='{first.Code}', system='{first.System ?? "none"}'",
            Fix: severity == FHIRDiagnosticSeverity.Error
                ? $"Use a code from the required ValueSet: {binding.ValueSet}"
                : null
        );
    }

    /// <summary>
    /// R4 snapshots bind version-suffixed canonicals (`…/observation-status|4.0.1`);
    /// the index is keyed by the version-less ValueSet.url.
    /// </summary>
    private static string StripVersion(string valueSetUrl)
    {
        var pipe = valueSetUrl.IndexOf('|');
        return pipe < 0 ? valueSetUrl : valueSetUrl[..pipe];
    }

    /// <summary>
    /// Every coding carried by the element: all entries of a CodeableConcept's
    /// coding array (any-coding binding semantics), a bare Coding, or a plain
    /// code string.
    /// </summary>
    private static List<(string? System, string Code)> ExtractCodings(JsonElement element)
    {
        var codings = new List<(string?, string)>();

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrEmpty(value))
                    codings.Add((null, value));
                break;

            case JsonValueKind.Object:
                if (element.TryGetProperty("coding", out var codingArray) &&
                    codingArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var coding in codingArray.EnumerateArray())
                    {
                        var sys = coding.TryGetProperty("system", out var s) ? s.GetString() : null;
                        var cd = coding.TryGetProperty("code", out var c) ? c.GetString() : null;
                        if (!string.IsNullOrEmpty(cd))
                            codings.Add((sys, cd));
                    }
                    break;
                }

                // Coding directly
                {
                    var sys = element.TryGetProperty("system", out var s) ? s.GetString() : null;
                    var cd = element.TryGetProperty("code", out var c) ? c.GetString() : null;
                    if (!string.IsNullOrEmpty(cd))
                        codings.Add((sys, cd));
                }
                break;
        }

        return codings;
    }
}
