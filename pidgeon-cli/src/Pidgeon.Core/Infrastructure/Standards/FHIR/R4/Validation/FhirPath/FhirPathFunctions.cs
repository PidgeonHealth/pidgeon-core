// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation.FhirPath;

/// <summary>
/// The whitelisted function library. The parser has already validated names and arity;
/// evaluation aborts (→ NotEvaluatable) on any input shape the subset does not cover,
/// and never converts a surprise into a pass or a fail.
/// </summary>
internal static class FhirPathFunctions
{
    private static readonly IReadOnlyList<FpValue> Empty = Array.Empty<FpValue>();
    private static readonly IReadOnlyList<FpValue> True = new FpValue[] { new FpValue.Bool(true) };
    private static readonly IReadOnlyList<FpValue> False = new FpValue[] { new FpValue.Bool(false) };

    // matches() patterns are literals throughout the constraint corpus; cache the compiled
    // Regex per pattern. Unanchored partial match per the FHIRPath spec (corpus expressions
    // self-anchor with ^...$); the timeout maps pathological patterns to NotEvaluatable
    // rather than a hang.
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);

    public static IReadOnlyList<FpValue> Apply(
        string name, IReadOnlyList<FpValue> source, IReadOnlyList<FpNode> args, FhirPathEvaluator host)
    {
        switch (name)
        {
            case "empty":
                return source.Count == 0 ? True : False;
            case "exists":
                if (args.Count == 0) return source.Count > 0 ? True : False;
                return Where(source, args[0], host).Count > 0 ? True : False;
            case "count":
                return new FpValue[] { new FpValue.Int(source.Count) };
            case "where":
                return Where(source, args[0], host);
            case "all":
                return All(source, args[0], host);
            case "not":
            {
                var value = FhirPathOperators.CoerceToBoolean(source, host.Abort);
                return value is null ? Empty : (value.Value ? False : True);
            }
            case "first":
                return source.Count > 0 ? new[] { source[0] } : Empty;
            case "select":
            {
                var projected = new List<FpValue>();
                foreach (var item in source)
                    projected.AddRange(host.EvalWithItem(args[0], item));
                return projected;
            }
            case "children":
            {
                var children = new List<FpValue>();
                foreach (var item in source)
                    children.AddRange(FhirPathEvaluator.Children(item));
                return children;
            }
            case "hasValue":
                return source.Count == 1 && source[0] is not FpValue.Node ? True : False;
            case "isDistinct":
                return IsDistinct(source);
            case "combine":
            {
                var combined = new List<FpValue>(source);
                combined.AddRange(host.EvalArgument(args[0]));
                return combined;
            }
            case "intersect":
            {
                // Items present in both collections, deduplicated (obs-7's shape:
                // coding.intersect(%resource.code.coding)).
                var other = host.EvalArgument(args[0]);
                var overlap = new List<FpValue>();
                foreach (var item in source)
                {
                    if (other.Any(o => FhirPathOperators.ItemEquals(item, o) == true) &&
                        !overlap.Any(o => FhirPathOperators.ItemEquals(item, o) == true))
                        overlap.Add(item);
                }
                return overlap;
            }
            case "trace":
                return source; // identity; arguments are side-effect-only and not evaluated
            case "iif":
            {
                var criterion = FhirPathOperators.CoerceToBoolean(host.EvalArgument(args[0]), host.Abort);
                if (criterion == true) return host.EvalArgument(args[1]);
                return args.Count == 3 ? host.EvalArgument(args[2]) : Empty;
            }
            case "matches":
                return Matches(source, args[0], host);
            case "contains":
            case "startsWith":
            {
                var input = SingletonString(source, name, host);
                if (input is null) return Empty;
                var arg = StringArgument(args[0], name, host);
                if (arg is null) return Empty;
                var matched = name == "contains"
                    ? input.Contains(arg, StringComparison.Ordinal)
                    : input.StartsWith(arg, StringComparison.Ordinal);
                return matched ? True : False;
            }
            case "length":
            {
                var input = SingletonString(source, name, host);
                return input is null ? Empty : new FpValue[] { new FpValue.Int(input.Length) };
            }
            case "substring":
                return Substring(source, args, host);
            case "toString":
                return ToStringFn(source, host);
            case "toInteger":
                return ToInteger(source, host);
            default:
                host.Abort($"function '{name}()' has no evaluator"); // unreachable: parser whitelists
                return Empty;
        }
    }

    private static IReadOnlyList<FpValue> Where(IReadOnlyList<FpValue> source, FpNode criteria, FhirPathEvaluator host)
    {
        var kept = new List<FpValue>();
        foreach (var item in source)
        {
            var verdict = FhirPathOperators.CoerceToBoolean(host.EvalWithItem(criteria, item), host.Abort);
            if (verdict == true) kept.Add(item);
        }
        return kept;
    }

    // all(): true only when the criteria is true for EVERY item (false or empty both
    // fail the item, per spec); an empty input collection is vacuously true.
    private static IReadOnlyList<FpValue> All(IReadOnlyList<FpValue> source, FpNode criteria, FhirPathEvaluator host)
    {
        foreach (var item in source)
        {
            var verdict = FhirPathOperators.CoerceToBoolean(host.EvalWithItem(criteria, item), host.Abort);
            if (verdict != true) return False;
        }
        return True;
    }

    private static IReadOnlyList<FpValue> IsDistinct(IReadOnlyList<FpValue> source)
    {
        for (int i = 0; i < source.Count; i++)
            for (int j = i + 1; j < source.Count; j++)
                if (FhirPathOperators.ItemEquals(source[i], source[j]) == true)
                    return False;
        return True;
    }

    private static IReadOnlyList<FpValue> Matches(IReadOnlyList<FpValue> source, FpNode patternArg, FhirPathEvaluator host)
    {
        var input = SingletonString(source, "matches", host);
        if (input is null) return Empty;
        var pattern = StringArgument(patternArg, "matches", host);
        if (pattern is null) return Empty;
        try
        {
            var regex = RegexCache.GetOrAdd(pattern, static p =>
                new Regex(p, RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
            return regex.IsMatch(input) ? True : False;
        }
        catch (ArgumentException)
        {
            host.Abort($"invalid regular expression in matches(): '{pattern}'");
            return Empty;
        }
        catch (RegexMatchTimeoutException)
        {
            host.Abort("matches() regular expression timed out");
            return Empty;
        }
    }

    private static IReadOnlyList<FpValue> Substring(IReadOnlyList<FpValue> source, IReadOnlyList<FpNode> args, FhirPathEvaluator host)
    {
        var input = SingletonString(source, "substring", host);
        if (input is null) return Empty;

        var startValues = host.EvalArgument(args[0]);
        if (startValues.Count == 0) return Empty;
        if (startValues.Count > 1 || startValues[0] is not FpValue.Int start)
        {
            host.Abort("substring() requires an integer start");
            return Empty;
        }
        if (start.Value < 0 || start.Value >= input.Length) return Empty;

        int from = (int)start.Value;
        if (args.Count == 1)
            return new FpValue[] { new FpValue.Str(input[from..]) };

        var lengthValues = host.EvalArgument(args[1]);
        if (lengthValues.Count == 0) return Empty;
        if (lengthValues.Count > 1 || lengthValues[0] is not FpValue.Int length)
        {
            host.Abort("substring() requires an integer length");
            return Empty;
        }
        if (length.Value <= 0) return new FpValue[] { new FpValue.Str("") };
        int take = (int)Math.Min(length.Value, input.Length - from);
        return new FpValue[] { new FpValue.Str(input.Substring(from, take)) };
    }

    private static IReadOnlyList<FpValue> ToStringFn(IReadOnlyList<FpValue> source, FhirPathEvaluator host)
    {
        if (source.Count == 0) return Empty;
        if (source.Count > 1)
        {
            host.Abort("toString() requires a singleton input");
            return Empty;
        }
        switch (source[0])
        {
            case FpValue.Str: return new[] { source[0] };
            case FpValue.Int i: return new FpValue[] { new FpValue.Str(i.Value.ToString(CultureInfo.InvariantCulture)) };
            case FpValue.Dec d: return new FpValue[] { new FpValue.Str(d.Value.ToString(CultureInfo.InvariantCulture)) };
            case FpValue.Bool b: return new FpValue[] { new FpValue.Str(b.Value ? "true" : "false") };
            default:
                host.Abort("toString() on a complex value is outside the subset");
                return Empty;
        }
    }

    private static IReadOnlyList<FpValue> ToInteger(IReadOnlyList<FpValue> source, FhirPathEvaluator host)
    {
        if (source.Count == 0) return Empty;
        if (source.Count > 1)
        {
            host.Abort("toInteger() requires a singleton input");
            return Empty;
        }
        switch (source[0])
        {
            case FpValue.Int: return new[] { source[0] };
            case FpValue.Bool b: return new FpValue[] { new FpValue.Int(b.Value ? 1 : 0) };
            case FpValue.Str s:
                return Regex.IsMatch(s.Value, @"^[+-]?\d+$") && long.TryParse(s.Value, out var parsed)
                    ? new FpValue[] { new FpValue.Int(parsed) }
                    : Empty;
            default: // Dec and Node convert to empty / are not convertible
                return Empty;
        }
    }

    /// <summary>Singleton string extraction: empty input → null result with no abort
    /// (empty propagates); multi-item or non-string input aborts.</summary>
    private static string? SingletonString(IReadOnlyList<FpValue> source, string function, FhirPathEvaluator host)
    {
        if (source.Count == 0) return null;
        if (source.Count > 1)
        {
            host.Abort($"{function}() requires a singleton input");
            return null;
        }
        if (source[0] is FpValue.Str s) return s.Value;
        host.Abort($"{function}() requires a string input, got {FhirPathOperators.KindName(source[0])}");
        return null;
    }

    private static string? StringArgument(FpNode arg, string function, FhirPathEvaluator host)
    {
        var values = host.EvalArgument(arg);
        if (values.Count == 0) return null;
        if (values.Count > 1 || values[0] is not FpValue.Str s)
        {
            host.Abort($"{function}() requires a string argument");
            return null;
        }
        return s.Value;
    }
}
