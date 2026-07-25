// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation.FhirPath;

/// <summary>
/// Compiles and evaluates FHIRPath invariant expressions over the measured subset the
/// US Core / Da Vinci PAS / base-R4 constraint corpus actually uses (~20 functions,
/// ~13 operators). Expressions outside the subset are Unsupported at compile time;
/// runtime surprises are NotEvaluatable — both are honest-skip verdicts the caller
/// renders as "invariant not evaluated", never a silent pass and never a guessed fail.
/// Sibling of the owned XPath-1.0 Schematron engine (C-CDA): compile once, cache the
/// immutable AST, run as a thread-safe DI singleton.
/// </summary>
internal interface IFhirPathSubsetEngine
{
    /// <summary>Compiles (cached per expression text). Unsupported is a verdict, not an
    /// error: the reason names the offending token or function.</summary>
    FhirPathCompileResult Compile(string expression);

    /// <summary>Evaluates one compiled expression against one element instance.</summary>
    FhirPathEvaluationOutcome Evaluate(FhirPathCompiledExpression expression, in FhirPathEvalContext context);
}

/// <summary>Compile verdict: a compiled expression, or the reason it is outside the subset.</summary>
internal sealed record FhirPathCompileResult(FhirPathCompiledExpression? Expression, string? UnsupportedReason)
{
    public bool IsSupported => Expression is not null;

    public static FhirPathCompileResult Ok(FhirPathCompiledExpression expression) => new(expression, null);
    public static FhirPathCompileResult NotSupported(string reason) => new(null, reason);
}

internal enum FhirPathVerdict { Pass, Fail, NotEvaluatable }

internal sealed record FhirPathEvaluationOutcome(FhirPathVerdict Verdict, string? Reason = null);

/// <summary>
/// One element instance to evaluate against: <paramref name="Focus"/> is $this (the JSON
/// of the element the constraint sits on), <paramref name="Resource"/> is %resource, and
/// <paramref name="FocusDeclaredType"/> is the element's FHIR type name where known —
/// what makes <c>$this is DateTime</c> decidable against untyped JSON.
/// </summary>
internal readonly record struct FhirPathEvalContext(
    JsonElement Focus, JsonElement Resource, string? FocusDeclaredType);

internal sealed class FhirPathSubsetEngine : IFhirPathSubsetEngine
{
    private readonly ILogger<FhirPathSubsetEngine> _logger;
    private readonly ConcurrentDictionary<string, FhirPathCompileResult> _cache = new(StringComparer.Ordinal);

    public FhirPathSubsetEngine(ILogger<FhirPathSubsetEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FhirPathCompileResult Compile(string expression)
        => _cache.GetOrAdd(expression, CompileUncached);

    private FhirPathCompileResult CompileUncached(string expression)
    {
        try
        {
            return FhirPathCompileResult.Ok(new FhirPathCompiledExpression(expression, FhirPathParser.Parse(expression)));
        }
        catch (FhirPathUnsupportedException ex)
        {
            _logger.LogDebug("FHIRPath expression outside the subset: {Reason} — '{Expression}'", ex.Message, expression);
            return FhirPathCompileResult.NotSupported(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "FHIRPath compile failed for '{Expression}'", expression);
            return FhirPathCompileResult.NotSupported("parse error: " + ex.Message);
        }
    }

    public FhirPathEvaluationOutcome Evaluate(FhirPathCompiledExpression expression, in FhirPathEvalContext context)
    {
        var (result, abort) = EvaluateRaw(expression, context);
        if (abort != null)
            return new FhirPathEvaluationOutcome(FhirPathVerdict.NotEvaluatable, abort);
        if (result == null)
            return new FhirPathEvaluationOutcome(FhirPathVerdict.NotEvaluatable, "internal evaluation error");

        // Invariant verdict rule: FAIL only on an explicit boolean false. An empty result
        // and a non-boolean singleton both pass (official-validator constraint behavior).
        if (result.Count == 1 && result[0] is FpValue.Bool b)
            return new FhirPathEvaluationOutcome(b.Value ? FhirPathVerdict.Pass : FhirPathVerdict.Fail);
        if (result.Count > 1)
            return new FhirPathEvaluationOutcome(
                FhirPathVerdict.NotEvaluatable, "expression returned a multi-item collection where a boolean was expected");
        return new FhirPathEvaluationOutcome(FhirPathVerdict.Pass);
    }

    /// <summary>Raw evaluation surface for the semantics tests, which must distinguish
    /// pass-as-true from pass-as-empty. Null result = an unexpected internal failure,
    /// already logged; degradation is skip-shaped, never a crash and never a verdict.</summary>
    internal (IReadOnlyList<FpValue>? Result, string? Abort) EvaluateRaw(
        FhirPathCompiledExpression expression, in FhirPathEvalContext context)
    {
        try
        {
            var focus = new List<FpValue>();
            FhirPathEvaluator.Unwrap(context.Focus, context.FocusDeclaredType, focus);
            var (result, abort) = FhirPathEvaluator.Run(expression.Root, focus, context.Resource);
            return (result, abort);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "FHIRPath evaluation failed for '{Expression}'", expression.Source);
            return (null, null);
        }
    }
}
