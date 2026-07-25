// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation.FhirPath;

/// <summary>
/// Walks a compiled AST against one element instance. Per-call state only — the AST is
/// shared and immutable, so the engine stays a thread-safe singleton. No exceptions for
/// control flow: any runtime departure from the subset sets an abort reason, every
/// subsequent visit returns empty, and the engine maps the abort to NotEvaluatable —
/// a surprise can degrade to an honest skip but never to a Pass or a Fail.
/// </summary>
internal sealed class FhirPathEvaluator
{
    private static readonly IReadOnlyList<FpValue> Empty = Array.Empty<FpValue>();

    private readonly IReadOnlyList<FpValue> _inputFocus;
    private readonly JsonElement _resource;
    private readonly Stack<IReadOnlyList<FpValue>> _focus = new();
    private string? _abortReason;

    private FhirPathEvaluator(IReadOnlyList<FpValue> inputFocus, JsonElement resource)
    {
        _inputFocus = inputFocus;
        _resource = resource;
        _focus.Push(inputFocus);
    }

    /// <summary>Evaluates the expression; a non-null abort reason means the result is
    /// meaningless and the constraint must be reported as not evaluated.</summary>
    public static (IReadOnlyList<FpValue> Result, string? Abort) Run(
        FpNode root, IReadOnlyList<FpValue> inputFocus, JsonElement resource)
    {
        var evaluator = new FhirPathEvaluator(inputFocus, resource);
        var result = evaluator.Eval(root);
        return (result, evaluator._abortReason);
    }

    internal void Abort(string reason) => _abortReason ??= reason;

    private bool Aborted => _abortReason != null;

    private IReadOnlyList<FpValue> CurrentFocus => _focus.Peek();

    internal IReadOnlyList<FpValue> Eval(FpNode node)
    {
        if (Aborted) return Empty;
        switch (node)
        {
            case FpNode.Literal lit:
                return new[] { lit.Value };
            case FpNode.This:
                return CurrentFocus;
            case FpNode.EnvConstant env:
                return EvalEnvConstant(env.Name);
            case FpNode.PathNav nav:
                return Navigate(nav.Source is null ? CurrentFocus : Eval(nav.Source), nav.Name);
            case FpNode.FunctionCall call:
            {
                var source = call.Source is null ? CurrentFocus : Eval(call.Source);
                if (Aborted) return Empty;
                return FhirPathFunctions.Apply(call.Name, source, call.Args, this);
            }
            case FpNode.TypeCall typeCall:
            {
                var source = typeCall.Source is null ? CurrentFocus : Eval(typeCall.Source);
                if (Aborted) return Empty;
                return TypeCheck(source, typeCall.TypeName, typeCall.Name);
            }
            case FpNode.TypeOp typeOp:
                return TypeCheck(Eval(typeOp.Operand), typeOp.TypeName, typeOp.IsIs ? "is" : "as");
            case FpNode.Indexer indexer:
                return EvalIndexer(indexer);
            case FpNode.Binary binary:
                return EvalBinary(binary);
            default:
                Abort($"unhandled expression node {node.GetType().Name}");
                return Empty;
        }
    }

    /// <summary>Evaluates a function argument against the current (unchanged) focus.</summary>
    internal IReadOnlyList<FpValue> EvalArgument(FpNode arg) => Eval(arg);

    /// <summary>Evaluates a lambda body (where/select/all/exists criteria) with one item
    /// as the focus, so bare identifiers and $this resolve to that item.</summary>
    internal IReadOnlyList<FpValue> EvalWithItem(FpNode body, FpValue item)
    {
        _focus.Push(new[] { item });
        try
        {
            return Eval(body);
        }
        finally
        {
            _focus.Pop();
        }
    }

    private IReadOnlyList<FpValue> EvalEnvConstant(string name)
    {
        switch (name)
        {
            case "resource":
            case "rootResource":
                return new FpValue[] { new FpValue.Node(_resource) };
            case "context":
                return _inputFocus;
            default: // "ucum" — the parser rejects anything else
                return new FpValue[] { new FpValue.Str("http://unitsofmeasure.org") };
        }
    }

    private IReadOnlyList<FpValue> EvalIndexer(FpNode.Indexer indexer)
    {
        var source = Eval(indexer.Source);
        var index = Eval(indexer.Index);
        if (Aborted) return Empty;
        if (index.Count != 1 || index[0] is not FpValue.Int i)
        {
            Abort("indexer requires a single integer index");
            return Empty;
        }
        return i.Value >= 0 && i.Value < source.Count ? new[] { source[(int)i.Value] } : Empty;
    }

    private IReadOnlyList<FpValue> EvalBinary(FpNode.Binary binary)
    {
        switch (binary.Op)
        {
            // Logic: literal truth tables in FhirPathOperators; short-circuit only where
            // the table already fixes the result regardless of the right operand.
            case FpBinaryOp.And:
            {
                var l = FhirPathOperators.CoerceToBoolean(Eval(binary.Left), Abort);
                if (Aborted) return Empty;
                if (l == false) return new FpValue[] { new FpValue.Bool(false) };
                return Tribool(FhirPathOperators.And(l, FhirPathOperators.CoerceToBoolean(Eval(binary.Right), Abort)));
            }
            case FpBinaryOp.Or:
            {
                var l = FhirPathOperators.CoerceToBoolean(Eval(binary.Left), Abort);
                if (Aborted) return Empty;
                if (l == true) return new FpValue[] { new FpValue.Bool(true) };
                return Tribool(FhirPathOperators.Or(l, FhirPathOperators.CoerceToBoolean(Eval(binary.Right), Abort)));
            }
            case FpBinaryOp.Xor:
            {
                var l = FhirPathOperators.CoerceToBoolean(Eval(binary.Left), Abort);
                var r = FhirPathOperators.CoerceToBoolean(Eval(binary.Right), Abort);
                return Tribool(FhirPathOperators.Xor(l, r));
            }
            case FpBinaryOp.Implies:
            {
                var l = FhirPathOperators.CoerceToBoolean(Eval(binary.Left), Abort);
                if (Aborted) return Empty;
                if (l == false) return new FpValue[] { new FpValue.Bool(true) };
                return Tribool(FhirPathOperators.Implies(l, FhirPathOperators.CoerceToBoolean(Eval(binary.Right), Abort)));
            }
            case FpBinaryOp.Equal:
                return Tribool(FhirPathOperators.CollectionEquals(Eval(binary.Left), Eval(binary.Right)));
            case FpBinaryOp.NotEqual:
                return Tribool(Negate(FhirPathOperators.CollectionEquals(Eval(binary.Left), Eval(binary.Right))));
            case FpBinaryOp.Less:
            case FpBinaryOp.Greater:
            case FpBinaryOp.LessOrEqual:
            case FpBinaryOp.GreaterOrEqual:
                return Tribool(FhirPathOperators.Compare(binary.Op, Eval(binary.Left), Eval(binary.Right), Abort));
            case FpBinaryOp.In:
                return Tribool(FhirPathOperators.InOp(Eval(binary.Left), Eval(binary.Right), Abort));
            case FpBinaryOp.Union:
                return FhirPathOperators.Union(Eval(binary.Left), Eval(binary.Right));
            default:
                return FhirPathOperators.Arithmetic(binary.Op, Eval(binary.Left), Eval(binary.Right), Abort);
        }
    }

    private static bool? Negate(bool? value) => value is null ? null : !value.Value;

    private IReadOnlyList<FpValue> Tribool(bool? value)
        => Aborted || value is null ? Empty : new FpValue[] { new FpValue.Bool(value.Value) };

    /// <summary>
    /// Member navigation over each item. FHIR property names are lowerCamelCase, so an
    /// uppercase identifier can only be a type filter (a path rooted at the resource type,
    /// e.g. <c>Patient.name</c>). Properties on primitives yield empty.
    /// </summary>
    private IReadOnlyList<FpValue> Navigate(IReadOnlyList<FpValue> source, string name)
    {
        if (Aborted || source.Count == 0) return Empty;
        var result = new List<FpValue>();
        foreach (var item in source)
        {
            if (item is not FpValue.Node node || node.Json.ValueKind != JsonValueKind.Object) continue;
            if (char.IsUpper(name[0]))
            {
                var typeName = TypeNameOf(node);
                if (typeName != null && TypeNameMatches(typeName, name)) result.Add(item);
                continue;
            }
            AppendProperty(node.Json, name, result);
        }
        return result;
    }

    /// <summary>
    /// Property navigation, including choice-type keys: FHIRPath navigates the logical
    /// name (<c>value</c>) but FHIR JSON stores the suffixed key (<c>valueString</c>).
    /// When the plain property is absent, each known type suffix is tried and the matched
    /// suffix becomes the value's DeclaredType — which is what makes <c>value.as(Quantity)</c>
    /// and ext-1's <c>value.exists()</c> work against real JSON.
    /// </summary>
    private static void AppendProperty(JsonElement json, string name, List<FpValue> result)
    {
        if (json.TryGetProperty(name, out var plain))
        {
            Unwrap(plain, null, result);
            return;
        }
        foreach (var suffix in ElementCardinalityEvaluator.ChoiceTypeSuffixes)
        {
            if (json.TryGetProperty(name + suffix, out var choice))
                Unwrap(choice, suffix, result);
        }
    }

    /// <summary>JSON → FpValue: objects stay nodes, arrays flatten one level, primitives
    /// unwrap eagerly (number → Int when the raw text has no '.' or exponent), nulls
    /// contribute nothing.</summary>
    internal static void Unwrap(JsonElement value, string? declaredType, List<FpValue> result)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray()) Unwrap(item, declaredType, result);
                break;
            case JsonValueKind.Object:
                result.Add(new FpValue.Node(value, declaredType));
                break;
            case JsonValueKind.String:
                result.Add(new FpValue.Str(value.GetString()!, declaredType));
                break;
            case JsonValueKind.Number:
                var raw = value.GetRawText();
                if (raw.Contains('.') || raw.Contains('e') || raw.Contains('E'))
                    result.Add(new FpValue.Dec(value.GetDecimal()));
                else
                    result.Add(new FpValue.Int(value.GetInt64()));
                break;
            case JsonValueKind.True:
                result.Add(new FpValue.Bool(true));
                break;
            case JsonValueKind.False:
                result.Add(new FpValue.Bool(false));
                break;
        }
    }

    /// <summary>
    /// children(): one child per logical element. Underscore-grouped primitive-extension
    /// carriers (<c>_family</c>) count as the element only when the plain property is
    /// absent — an extension-only primitive IS an element and must count for ele-1.
    /// resourceType is metadata, not a child.
    /// </summary>
    internal static IReadOnlyList<FpValue> Children(FpValue item)
    {
        if (item is not FpValue.Node node || node.Json.ValueKind != JsonValueKind.Object) return Empty;
        var result = new List<FpValue>();
        foreach (var prop in node.Json.EnumerateObject())
        {
            if (prop.Name == "resourceType") continue;
            if (prop.Name.StartsWith('_'))
            {
                if (!node.Json.TryGetProperty(prop.Name[1..], out _))
                    Unwrap(prop.Value, null, result);
                continue;
            }
            Unwrap(prop.Value, null, result);
        }
        return result;
    }

    /// <summary>
    /// is/as/ofType. Type identity comes from resourceType, then DeclaredType, then the
    /// primitive kind. An undecidable 'is' yields empty (spec-truthful "unknown"; empty can
    /// never manufacture a Fail). An undecidable 'as'/'ofType' ABORTS instead: 'as' returning
    /// empty would make a downstream '.exists()' false — a manufactured failure — so the
    /// honest outcome is "not evaluated".
    /// </summary>
    private IReadOnlyList<FpValue> TypeCheck(IReadOnlyList<FpValue> source, string typeName, string mode)
    {
        if (Aborted) return Empty;
        if (mode == "ofType")
        {
            var filtered = new List<FpValue>();
            foreach (var item in source)
            {
                var decided = IsOfType(item, typeName);
                if (decided == null)
                {
                    Abort($"cannot determine the type of a value for ofType({typeName})");
                    return Empty;
                }
                if (decided == true) filtered.Add(item);
            }
            return filtered;
        }

        if (source.Count == 0) return Empty;
        if (source.Count > 1)
        {
            Abort($"'{mode}' requires a singleton operand");
            return Empty;
        }

        var match = IsOfType(source[0], typeName);
        if (mode == "is")
            return match is null ? Empty : new FpValue[] { new FpValue.Bool(match.Value) };

        // as
        if (match == null)
        {
            Abort($"cannot determine the type of a value for 'as {typeName}'");
            return Empty;
        }
        return match == true ? new[] { source[0] } : Empty;
    }

    /// <summary>Null = undecidable (FHIR JSON carries no types; only resourceType, the
    /// threaded DeclaredType, and primitive kinds decide).</summary>
    private static bool? IsOfType(FpValue item, string typeName)
    {
        switch (item)
        {
            case FpValue.Node node:
                var actual = TypeNameOf(node);
                return actual is null ? null : TypeNameMatches(actual, typeName);
            case FpValue.Str str:
                if (str.DeclaredType != null) return TypeNameMatches(str.DeclaredType, typeName);
                return TypeNameMatches("String", typeName) ? true : null;
            case FpValue.Int:
                return TypeNameMatches("Integer", typeName);
            case FpValue.Dec:
                return TypeNameMatches("Decimal", typeName);
            default:
                return TypeNameMatches("Boolean", typeName);
        }
    }

    private static string? TypeNameOf(FpValue.Node node)
    {
        if (node.Json.ValueKind == JsonValueKind.Object &&
            node.Json.TryGetProperty("resourceType", out var rt) &&
            rt.ValueKind == JsonValueKind.String)
            return rt.GetString();
        return node.DeclaredType;
    }

    private static bool TypeNameMatches(string actual, string specifier)
        => string.Equals(actual, specifier, StringComparison.OrdinalIgnoreCase);
}
