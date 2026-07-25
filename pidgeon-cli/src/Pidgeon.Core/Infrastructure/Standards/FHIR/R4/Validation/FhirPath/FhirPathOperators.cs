// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation.FhirPath;

/// <summary>
/// Pure value/collection operations for the FHIRPath subset. Three-valued logic is
/// written as literal truth tables (bool? null = the empty collection) so each cell is
/// diffable against the FHIRPath spec — no clever short-circuit reformulations; laziness
/// lives in the evaluator and only where the table already fixes the result.
/// </summary>
internal static class FhirPathOperators
{
    public static bool? And(bool? l, bool? r) => (l, r) switch
    {
        (true, true) => true,
        (true, false) => false,
        (true, null) => null,
        (false, true) => false,
        (false, false) => false,
        (false, null) => false,
        (null, true) => null,
        (null, false) => false,
        (null, null) => null,
    };

    public static bool? Or(bool? l, bool? r) => (l, r) switch
    {
        (true, true) => true,
        (true, false) => true,
        (true, null) => true,
        (false, true) => true,
        (false, false) => false,
        (false, null) => null,
        (null, true) => true,
        (null, false) => null,
        (null, null) => null,
    };

    public static bool? Xor(bool? l, bool? r) => (l, r) switch
    {
        (true, true) => false,
        (true, false) => true,
        (true, null) => null,
        (false, true) => true,
        (false, false) => false,
        (false, null) => null,
        (null, _) => null,
    };

    public static bool? Implies(bool? l, bool? r) => (l, r) switch
    {
        (true, true) => true,
        (true, false) => false,
        (true, null) => null,
        (false, true) => true,
        (false, false) => true,
        (false, null) => true,   // false implies { } = TRUE (spec; classic bug site)
        (null, true) => true,    // { } implies true = TRUE (spec; classic bug site)
        (null, false) => null,
        (null, null) => null,
    };

    /// <summary>
    /// Boolean coercion for logic positions ONLY (and/or/xor/implies/not, iif and lambda
    /// criteria, the final verdict): empty → null, single Boolean → its value, single
    /// non-Boolean → true, multi-item → abort (spec error; reported honestly).
    /// Never applied inside '=', 'in', or comparisons.
    /// </summary>
    public static bool? CoerceToBoolean(IReadOnlyList<FpValue> collection, Action<string> abort)
    {
        if (collection.Count == 0) return null;
        if (collection.Count > 1)
        {
            abort("a multi-item collection was used where a single boolean is required");
            return null;
        }
        return collection[0] is FpValue.Bool b ? b.Value : true;
    }

    /// <summary>
    /// FHIRPath collection equality: either operand empty → empty (never true); different
    /// counts → false; else pairwise in-order item equality (a decidably-unequal pair wins
    /// over an undecidable one).
    /// </summary>
    public static bool? CollectionEquals(IReadOnlyList<FpValue> l, IReadOnlyList<FpValue> r)
    {
        if (l.Count == 0 || r.Count == 0) return null;
        if (l.Count != r.Count) return false;
        bool sawUndecidable = false;
        for (int i = 0; i < l.Count; i++)
        {
            var eq = ItemEquals(l[i], r[i]);
            if (eq == false) return false;
            if (eq == null) sawUndecidable = true;
        }
        return sawUndecidable ? null : true;
    }

    /// <summary>Item equality: strings ordinal, numerics cross-type, booleans, complex
    /// nodes by the shared deep structural JSON comparison
    /// (<see cref="ElementConstraintEvaluator.JsonElementEquals"/>); mixed kinds are decidably unequal.</summary>
    public static bool? ItemEquals(FpValue a, FpValue b) => (a, b) switch
    {
        (FpValue.Str sa, FpValue.Str sb) => string.Equals(sa.Value, sb.Value, StringComparison.Ordinal),
        (FpValue.Int ia, FpValue.Int ib) => ia.Value == ib.Value,
        (FpValue.Dec da, FpValue.Dec db) => da.Value == db.Value,
        (FpValue.Int ia, FpValue.Dec db) => ia.Value == db.Value,
        (FpValue.Dec da, FpValue.Int ib) => da.Value == ib.Value,
        (FpValue.Bool ba, FpValue.Bool bb) => ba.Value == bb.Value,
        (FpValue.Node na, FpValue.Node nb) => ElementConstraintEvaluator.JsonElementEquals(na.Json, nb.Json),
        _ => false,
    };

    // Date/dateTime-shaped strings: 2024, 2024-01, 2024-01-15, 2024-01-15T10:00:00Z ...
    private static readonly Regex DateShape = new(@"^\d{4}(-\d{2}(-\d{2})?)?($|T)", RegexOptions.Compiled);

    /// <summary>
    /// Ordering comparison. Singleton operands required (multi aborts); empty propagates.
    /// Numerics compare numerically; strings by Unicode codepoint — EXCEPT two date-shaped
    /// strings of different precision, which are undecidable (empty), never guessed lexically.
    /// </summary>
    public static bool? Compare(FpBinaryOp op, IReadOnlyList<FpValue> l, IReadOnlyList<FpValue> r, Action<string> abort)
    {
        if (l.Count == 0 || r.Count == 0) return null;
        if (l.Count > 1 || r.Count > 1)
        {
            abort("comparison requires singleton operands");
            return null;
        }

        int cmp;
        switch (l[0], r[0])
        {
            case (FpValue.Int a, FpValue.Int b): cmp = a.Value.CompareTo(b.Value); break;
            case (FpValue.Dec a, FpValue.Dec b): cmp = a.Value.CompareTo(b.Value); break;
            case (FpValue.Int a, FpValue.Dec b): cmp = ((decimal)a.Value).CompareTo(b.Value); break;
            case (FpValue.Dec a, FpValue.Int b): cmp = a.Value.CompareTo((decimal)b.Value); break;
            case (FpValue.Str a, FpValue.Str b):
                if (DateShape.IsMatch(a.Value) && DateShape.IsMatch(b.Value) && a.Value.Length != b.Value.Length)
                    return null; // mixed-precision date comparison is undecidable
                cmp = string.CompareOrdinal(a.Value, b.Value);
                break;
            default:
                abort($"cannot order-compare {KindName(l[0])} and {KindName(r[0])}");
                return null;
        }

        return op switch
        {
            FpBinaryOp.Less => cmp < 0,
            FpBinaryOp.Greater => cmp > 0,
            FpBinaryOp.LessOrEqual => cmp <= 0,
            _ => cmp >= 0,
        };
    }

    /// <summary>+ - * mod on numerics; '&amp;' string concatenation (empty operand = '',
    /// THE difference vs '+', which propagates empty).</summary>
    public static IReadOnlyList<FpValue> Arithmetic(FpBinaryOp op, IReadOnlyList<FpValue> l, IReadOnlyList<FpValue> r, Action<string> abort)
    {
        if (op == FpBinaryOp.Concat)
        {
            var ls = ConcatOperand(l, abort);
            var rs = ConcatOperand(r, abort);
            if (ls == null || rs == null) return Array.Empty<FpValue>();
            return new FpValue[] { new FpValue.Str(ls + rs) };
        }

        if (l.Count == 0 || r.Count == 0) return Array.Empty<FpValue>();
        if (l.Count > 1 || r.Count > 1)
        {
            abort("arithmetic requires singleton operands");
            return Array.Empty<FpValue>();
        }

        if (l[0] is FpValue.Int a && r[0] is FpValue.Int b)
        {
            if (op == FpBinaryOp.Mod)
                return b.Value == 0 ? Array.Empty<FpValue>() : new FpValue[] { new FpValue.Int(a.Value % b.Value) };
            checked
            {
                var value = op switch
                {
                    FpBinaryOp.Add => a.Value + b.Value,
                    FpBinaryOp.Subtract => a.Value - b.Value,
                    _ => a.Value * b.Value,
                };
                return new FpValue[] { new FpValue.Int(value) };
            }
        }

        if (TryDecimal(l[0], out var da) && TryDecimal(r[0], out var db))
        {
            if (op == FpBinaryOp.Mod)
            {
                abort("'mod' on non-integer operands is outside the subset");
                return Array.Empty<FpValue>();
            }
            var value = op switch
            {
                FpBinaryOp.Add => da + db,
                FpBinaryOp.Subtract => da - db,
                _ => da * db,
            };
            return new FpValue[] { new FpValue.Dec(value) };
        }

        abort(l[0] is FpValue.Str || r[0] is FpValue.Str
            ? "'+' on strings is outside the subset (use '&' for concatenation)"
            : $"cannot apply arithmetic to {KindName(l[0])} and {KindName(r[0])}");
        return Array.Empty<FpValue>();
    }

    /// <summary>'in': empty LHS → empty; multi LHS aborts; empty RHS → false; else true
    /// iff the LHS item equals any RHS item.</summary>
    public static bool? InOp(IReadOnlyList<FpValue> l, IReadOnlyList<FpValue> r, Action<string> abort)
    {
        if (l.Count == 0) return null;
        if (l.Count > 1)
        {
            abort("'in' requires a singleton left operand");
            return null;
        }
        if (r.Count == 0) return false;
        foreach (var item in r)
            if (ItemEquals(l[0], item) == true) return true;
        return false;
    }

    /// <summary>'|' union: concatenation deduplicated by item equality, first-occurrence order.</summary>
    public static IReadOnlyList<FpValue> Union(IReadOnlyList<FpValue> l, IReadOnlyList<FpValue> r)
    {
        var result = new List<FpValue>(l.Count + r.Count);
        foreach (var item in l) AddDistinct(result, item);
        foreach (var item in r) AddDistinct(result, item);
        return result;
    }

    private static void AddDistinct(List<FpValue> list, FpValue item)
    {
        foreach (var existing in list)
            if (ItemEquals(existing, item) == true) return;
        list.Add(item);
    }

    private static string? ConcatOperand(IReadOnlyList<FpValue> operand, Action<string> abort)
    {
        if (operand.Count == 0) return "";
        if (operand.Count > 1)
        {
            abort("'&' requires singleton operands");
            return null;
        }
        if (operand[0] is FpValue.Str s) return s.Value;
        abort($"'&' requires string operands, got {KindName(operand[0])}");
        return null;
    }

    private static bool TryDecimal(FpValue value, out decimal result)
    {
        switch (value)
        {
            case FpValue.Int i: result = i.Value; return true;
            case FpValue.Dec d: result = d.Value; return true;
            default: result = 0; return false;
        }
    }

    public static string KindName(FpValue value) => value switch
    {
        FpValue.Node => "a complex value",
        FpValue.Str => "a string",
        FpValue.Int => "an integer",
        FpValue.Dec => "a decimal",
        _ => "a boolean",
    };
}
