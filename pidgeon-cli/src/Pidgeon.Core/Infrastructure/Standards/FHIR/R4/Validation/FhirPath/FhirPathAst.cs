// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation.FhirPath;

/// <summary>
/// A FHIRPath runtime value. Collections are <c>IReadOnlyList&lt;FpValue&gt;</c>; an empty
/// list is the FHIRPath empty collection <c>{ }</c>. Complex JSON stays a <see cref="Node"/>;
/// JSON primitives are unwrapped eagerly at navigation. <c>DeclaredType</c> carries the FHIR
/// type name where known (the constraint's element type, or the suffix of a matched
/// choice-type key) — the only type information available, since FHIR JSON is untyped on
/// the wire.
/// </summary>
internal abstract record FpValue
{
    internal sealed record Node(JsonElement Json, string? DeclaredType = null) : FpValue;
    internal sealed record Str(string Value, string? DeclaredType = null) : FpValue;
    internal sealed record Int(long Value) : FpValue;
    internal sealed record Dec(decimal Value) : FpValue;
    internal sealed record Bool(bool Value) : FpValue;
}

/// <summary>Immutable AST for a compiled expression; shared freely across threads.</summary>
internal abstract record FpNode
{
    /// <summary>String / number / boolean literal.</summary>
    internal sealed record Literal(FpValue Value) : FpNode;

    /// <summary><c>$this</c> — the current focus (the lambda item inside where/select/all,
    /// otherwise the input element instance).</summary>
    internal sealed record This : FpNode;

    /// <summary>Environment constant: resource | rootResource | context | ucum.</summary>
    internal sealed record EnvConstant(string Name) : FpNode;

    /// <summary>Member navigation. Null <c>Source</c> = navigate the current focus.</summary>
    internal sealed record PathNav(FpNode? Source, string Name) : FpNode;

    /// <summary>Function invocation. Null <c>Source</c> = invoke on the current focus.</summary>
    internal sealed record FunctionCall(FpNode? Source, string Name, IReadOnlyList<FpNode> Args) : FpNode;

    /// <summary>is(T) / as(T) / ofType(T) — functions whose argument is a type specifier.</summary>
    internal sealed record TypeCall(FpNode? Source, string Name, string TypeName) : FpNode;

    /// <summary><c>expr[index]</c> — 0-based; out of range yields empty.</summary>
    internal sealed record Indexer(FpNode Source, FpNode Index) : FpNode;

    /// <summary><c>x is T</c> / <c>x as T</c> operator forms.</summary>
    internal sealed record TypeOp(FpNode Operand, bool IsIs, string TypeName) : FpNode;

    internal sealed record Binary(FpBinaryOp Op, FpNode Left, FpNode Right) : FpNode;
}

internal enum FpBinaryOp
{
    And, Or, Xor, Implies,
    Equal, NotEqual, Less, Greater, LessOrEqual, GreaterOrEqual,
    In, Union, Add, Subtract, Multiply, Mod, Concat
}

/// <summary>A successfully compiled expression; opaque to callers.</summary>
internal sealed record FhirPathCompiledExpression(string Source, FpNode Root);

/// <summary>
/// Compile-time rejection of anything outside the supported subset. Thrown only during
/// Compile and converted to an Unsupported result at the engine boundary — it never
/// crosses into callers, and evaluation never throws it.
/// </summary>
internal sealed class FhirPathUnsupportedException : Exception
{
    public FhirPathUnsupportedException(string reason) : base(reason) { }
}
