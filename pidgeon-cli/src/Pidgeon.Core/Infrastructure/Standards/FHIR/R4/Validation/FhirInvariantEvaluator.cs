// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation.FhirPath;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Evaluates an element definition's FHIRPath invariants against each element instance
/// via the owned subset engine, mapping verdicts to profile diagnostics: a failed
/// error-severity invariant is an Error, a failed warning-severity invariant is a
/// Warning, and an invariant the subset cannot evaluate emits a visible
/// "not evaluated" diagnostic (error→Warning, warning→Information) — never a silent
/// pass, never a guessed failure. Warnings never flip a resource's validity and do
/// not trip <c>conform --ci</c> (which keys on MUSTSUPPORT-* rule ids only).
/// </summary>
public sealed class FhirInvariantEvaluator
{
    // ponytail: silent skip-list for base boilerplate the subset engine cannot evaluate —
    // dom-3 (descendants()+trace()), txt-1/txt-2 (htmlChecks()). These are stamped on every
    // DomainResource/Narrative, so a per-resource diagnostic would drown every scorecard;
    // the ceiling is a resource whose ONLY defect is an unreferenced contained resource
    // (dom-3) grading valid where the official validator fails it. Upgrade path: teach the
    // engine descendants()/htmlChecks() and delete the entry. Anything NOT listed that fails
    // to compile or evaluate emits the visible not-evaluated diagnostic — the
    // unknown-unknowns tripwire (the census test pins the expected skip set).
    private static readonly HashSet<string> SkippedBaseKeys = new(StringComparer.Ordinal)
    {
        "dom-3", "txt-1", "txt-2",
    };

    private readonly IFhirPathSubsetEngine _engine;
    private readonly ILogger<FhirInvariantEvaluator> _logger;

    internal FhirInvariantEvaluator(IFhirPathSubsetEngine engine, ILogger<FhirInvariantEvaluator> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs every constraint on <paramref name="elementDef"/> against each instance.
    /// <paramref name="instances"/> carries the choice-type suffix for elements resolved
    /// through a choice key (valueQuantity → "Quantity") — the declared type that makes
    /// <c>$this is DateTime</c> / <c>value.as(Quantity)</c> decidable against untyped JSON.
    /// </summary>
    public void Validate(
        JsonElement resourceRoot,
        FHIRElementDefinition elementDef,
        IReadOnlyList<(JsonElement Element, string? ChoiceType)> instances,
        List<FHIRDiagnostic> diagnostics)
    {
        foreach (var constraint in elementDef.Constraints)
        {
            if (SkippedBaseKeys.Contains(constraint.Key)) continue;

            var compiled = _engine.Compile(constraint.Expression);
            if (!compiled.IsSupported)
            {
                diagnostics.Add(NotEvaluatedDiagnostic(constraint, elementDef.Path, compiled.UnsupportedReason!));
                continue;
            }

            // A NotEvaluatable outcome collapses to ONE diagnostic per (path, key) so a
            // repeating element cannot spam the report with identical honest-skips.
            string? notEvaluatableReason = null;
            foreach (var (instance, choiceType) in instances)
            {
                var declaredType = choiceType ?? (elementDef.Types.Count == 1 ? elementDef.Types[0].Code : null);
                var outcome = _engine.Evaluate(
                    compiled.Expression!, new FhirPathEvalContext(instance, resourceRoot, declaredType));

                if (outcome.Verdict == FhirPathVerdict.Fail)
                    diagnostics.Add(FailureDiagnostic(constraint, elementDef.Path));
                else if (outcome.Verdict == FhirPathVerdict.NotEvaluatable)
                    notEvaluatableReason ??= outcome.Reason ?? "evaluation outside the subset";
            }

            if (notEvaluatableReason != null)
            {
                _logger.LogDebug(
                    "Invariant {Key} at {Path} not evaluated: {Reason}", constraint.Key, elementDef.Path, notEvaluatableReason);
                diagnostics.Add(NotEvaluatedDiagnostic(constraint, elementDef.Path, notEvaluatableReason));
            }
        }
    }

    private static FHIRDiagnostic FailureDiagnostic(FHIRConstraint constraint, string path)
        => new(
            constraint.Severity == "warning" ? FHIRDiagnosticSeverity.Warning : FHIRDiagnosticSeverity.Error,
            path,
            $"Invariant '{constraint.Key}' failed at '{path}': {constraint.Human}",
            Expected: $"Invariant: {constraint.Key}",
            Actual: "expression evaluated to false",
            Fix: constraint.Human);

    private static FHIRDiagnostic NotEvaluatedDiagnostic(FHIRConstraint constraint, string path, string reason)
        => new(
            constraint.Severity == "warning" ? FHIRDiagnosticSeverity.Information : FHIRDiagnosticSeverity.Warning,
            path,
            $"Invariant '{constraint.Key}' at '{path}' was not evaluated: {reason}",
            Expected: $"Invariant not evaluated: {constraint.Key}",
            Actual: Truncate(constraint.Expression),
            Fix: null);

    private static string Truncate(string expression)
        => expression.Length <= 120 ? expression : expression[..117] + "...";
}
