// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation.FhirPath;

/// <summary>
/// Recursive-descent parser for the FHIRPath subset. Precedence (tightest to loosest,
/// matching the FHIRPath grammar): postfix ('.', '[]') → '*'/'mod' → '+'/'-'/'&amp;' →
/// 'is'/'as' → '|' → comparisons → '='/'!=' → 'in' → 'and' → 'or'/'xor' → 'implies'.
/// Every construct outside the subset throws <see cref="FhirPathUnsupportedException"/>
/// with a reason naming the offender — the compile-time tripwire that keeps the engine
/// from ever guessing at semantics it does not implement.
/// </summary>
internal sealed class FhirPathParser
{
    /// <summary>Supported function arity: name → (min, max) argument count.</summary>
    private static readonly Dictionary<string, (int Min, int Max)> SupportedFunctions = new(StringComparer.Ordinal)
    {
        ["exists"] = (0, 1), ["empty"] = (0, 0), ["where"] = (1, 1), ["count"] = (0, 0),
        ["not"] = (0, 0), ["all"] = (1, 1), ["matches"] = (1, 1), ["first"] = (0, 0),
        ["hasValue"] = (0, 0), ["isDistinct"] = (0, 0), ["contains"] = (1, 1),
        ["startsWith"] = (1, 1), ["toString"] = (0, 0), ["length"] = (0, 0),
        ["select"] = (1, 1), ["iif"] = (2, 3), ["substring"] = (1, 2), ["toInteger"] = (0, 0),
        ["children"] = (0, 0), ["combine"] = (1, 1), ["intersect"] = (1, 1), ["trace"] = (1, 2),
    };

    /// <summary>Functions deliberately outside the subset, with the reason they are skipped
    /// rather than implemented. An expression using one degrades to "invariant not
    /// evaluated" — never a guessed verdict.</summary>
    private static readonly Dictionary<string, string> KnownUnsupportedFunctions = new(StringComparer.Ordinal)
    {
        ["memberOf"] = "requires terminology expansion",
        ["resolve"] = "requires reference resolution",
        ["htmlChecks"] = "requires XHTML validation",
        ["conformsTo"] = "requires recursive profile validation",
        ["descendants"] = "unbounded tree walk",
        ["repeat"] = "unbounded iteration",
        ["allTrue"] = "outside the subset",
    };

    private static readonly HashSet<string> EnvConstants = new(StringComparer.Ordinal)
    {
        "resource", "rootResource", "context", "ucum"
    };

    private readonly List<FpToken> _tokens;
    private int _pos;

    private FhirPathParser(List<FpToken> tokens) => _tokens = tokens;

    public static FpNode Parse(string expression)
    {
        var parser = new FhirPathParser(FhirPathTokenizer.Tokenize(expression));
        var root = parser.ParseImplies();
        var current = parser.Current;
        if (current.Kind != FpTokenKind.End)
            throw new FhirPathUnsupportedException($"unexpected '{current.Text}' at position {current.Pos}");
        return root;
    }

    private FpToken Current => _tokens[_pos];

    private FpToken Advance() => _tokens[_pos++];

    private bool TryConsume(FpTokenKind kind)
    {
        if (Current.Kind != kind) return false;
        _pos++;
        return true;
    }

    private void Expect(FpTokenKind kind, string what)
    {
        if (!TryConsume(kind))
            throw new FhirPathUnsupportedException($"expected {what} at position {Current.Pos}, found '{Current.Text}'");
    }

    private bool TryConsumeWord(string word)
    {
        if (Current.Kind != FpTokenKind.Identifier || !string.Equals(Current.Text, word, StringComparison.Ordinal))
            return false;
        _pos++;
        return true;
    }

    private bool PeekWord(string word)
        => Current.Kind == FpTokenKind.Identifier && string.Equals(Current.Text, word, StringComparison.Ordinal);

    private FpNode ParseImplies()
    {
        var left = ParseOr();
        while (TryConsumeWord("implies"))
            left = new FpNode.Binary(FpBinaryOp.Implies, left, ParseOr());
        return left;
    }

    private FpNode ParseOr()
    {
        var left = ParseAnd();
        while (true)
        {
            if (TryConsumeWord("or")) left = new FpNode.Binary(FpBinaryOp.Or, left, ParseAnd());
            else if (TryConsumeWord("xor")) left = new FpNode.Binary(FpBinaryOp.Xor, left, ParseAnd());
            else return left;
        }
    }

    private FpNode ParseAnd()
    {
        var left = ParseMembership();
        while (TryConsumeWord("and"))
            left = new FpNode.Binary(FpBinaryOp.And, left, ParseMembership());
        return left;
    }

    private FpNode ParseMembership()
    {
        var left = ParseEquality();
        if (PeekWord("contains"))
            throw new FhirPathUnsupportedException(
                $"'contains' infix operator at position {Current.Pos} is outside the FHIRPath subset; only the contains() function form is supported");
        if (TryConsumeWord("in"))
            return new FpNode.Binary(FpBinaryOp.In, left, ParseEquality());
        return left;
    }

    private FpNode ParseEquality()
    {
        var left = ParseInequality();
        FpBinaryOp op;
        if (TryConsume(FpTokenKind.Eq)) op = FpBinaryOp.Equal;
        else if (TryConsume(FpTokenKind.NotEq)) op = FpBinaryOp.NotEqual;
        else return left;

        var result = new FpNode.Binary(op, left, ParseInequality());
        if (Current.Kind is FpTokenKind.Eq or FpTokenKind.NotEq)
            throw new FhirPathUnsupportedException($"chained comparison at position {Current.Pos} is outside the FHIRPath subset");
        return result;
    }

    private FpNode ParseInequality()
    {
        var left = ParseUnion();
        FpBinaryOp op;
        if (TryConsume(FpTokenKind.Lt)) op = FpBinaryOp.Less;
        else if (TryConsume(FpTokenKind.Gt)) op = FpBinaryOp.Greater;
        else if (TryConsume(FpTokenKind.Le)) op = FpBinaryOp.LessOrEqual;
        else if (TryConsume(FpTokenKind.Ge)) op = FpBinaryOp.GreaterOrEqual;
        else return left;
        return new FpNode.Binary(op, left, ParseUnion());
    }

    private FpNode ParseUnion()
    {
        var left = ParseTypeExpr();
        while (TryConsume(FpTokenKind.Pipe))
            left = new FpNode.Binary(FpBinaryOp.Union, left, ParseTypeExpr());
        return left;
    }

    private FpNode ParseTypeExpr()
    {
        var left = ParseAdditive();
        if (TryConsumeWord("is")) return new FpNode.TypeOp(left, IsIs: true, ParseTypeSpecifier());
        if (TryConsumeWord("as")) return new FpNode.TypeOp(left, IsIs: false, ParseTypeSpecifier());
        return left;
    }

    private FpNode ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (true)
        {
            if (TryConsume(FpTokenKind.Plus)) left = new FpNode.Binary(FpBinaryOp.Add, left, ParseMultiplicative());
            else if (TryConsume(FpTokenKind.Minus)) left = new FpNode.Binary(FpBinaryOp.Subtract, left, ParseMultiplicative());
            else if (TryConsume(FpTokenKind.Amp)) left = new FpNode.Binary(FpBinaryOp.Concat, left, ParseMultiplicative());
            else return left;
        }
    }

    private FpNode ParseMultiplicative()
    {
        var left = ParsePostfix();
        while (true)
        {
            if (TryConsume(FpTokenKind.Star)) left = new FpNode.Binary(FpBinaryOp.Multiply, left, ParsePostfix());
            else if (TryConsumeWord("mod")) left = new FpNode.Binary(FpBinaryOp.Mod, left, ParsePostfix());
            else if (PeekWord("div"))
                throw new FhirPathUnsupportedException($"'div' operator at position {Current.Pos} is outside the FHIRPath subset");
            else return left;
        }
    }

    private FpNode ParsePostfix()
    {
        var node = ParsePrimary();
        while (true)
        {
            if (TryConsume(FpTokenKind.Dot))
            {
                node = ParseInvocation(node);
            }
            else if (TryConsume(FpTokenKind.LBracket))
            {
                var index = ParseImplies();
                Expect(FpTokenKind.RBracket, "']'");
                node = new FpNode.Indexer(node, index);
            }
            else
            {
                return node;
            }
        }
    }

    private FpNode ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case FpTokenKind.String:
                Advance();
                return new FpNode.Literal(new FpValue.Str(token.Text));
            case FpTokenKind.Integer:
                Advance();
                return new FpNode.Literal(new FpValue.Int(long.Parse(token.Text)));
            case FpTokenKind.Decimal:
                Advance();
                return new FpNode.Literal(new FpValue.Dec(decimal.Parse(token.Text, System.Globalization.CultureInfo.InvariantCulture)));
            case FpTokenKind.This:
                Advance();
                return new FpNode.This();
            case FpTokenKind.EnvConstant:
                Advance();
                if (!EnvConstants.Contains(token.Text))
                    throw new FhirPathUnsupportedException($"environment constant '%{token.Text}' at position {token.Pos} is outside the FHIRPath subset");
                return new FpNode.EnvConstant(token.Text);
            case FpTokenKind.LParen:
                Advance();
                var inner = ParseImplies();
                Expect(FpTokenKind.RParen, "')'");
                return inner;
            case FpTokenKind.Identifier:
                if ((token.Text == "true" || token.Text == "false") && _tokens[_pos + 1].Kind != FpTokenKind.LParen)
                {
                    Advance();
                    return new FpNode.Literal(new FpValue.Bool(token.Text == "true"));
                }
                return ParseInvocation(source: null);
            case FpTokenKind.Minus:
                throw new FhirPathUnsupportedException($"unary minus at position {token.Pos} is outside the FHIRPath subset");
            default:
                throw new FhirPathUnsupportedException($"unexpected '{token.Text}' at position {token.Pos}");
        }
    }

    /// <summary>Member access or function call following a '.' (or at expression start,
    /// with null <paramref name="source"/> = the current focus).</summary>
    private FpNode ParseInvocation(FpNode? source)
    {
        if (Current.Kind != FpTokenKind.Identifier)
            throw new FhirPathUnsupportedException($"expected member name at position {Current.Pos}, found '{Current.Text}'");
        var name = Advance().Text;

        if (Current.Kind != FpTokenKind.LParen)
            return new FpNode.PathNav(source, name);

        Advance(); // '('
        if (name is "is" or "as" or "ofType")
        {
            var typeName = ParseTypeSpecifier();
            Expect(FpTokenKind.RParen, "')'");
            return new FpNode.TypeCall(source, name, typeName);
        }

        if (KnownUnsupportedFunctions.TryGetValue(name, out var reason))
            throw new FhirPathUnsupportedException($"function '{name}()' is outside the FHIRPath subset ({reason})");
        if (!SupportedFunctions.TryGetValue(name, out var arity))
            throw new FhirPathUnsupportedException($"function '{name}()' is outside the FHIRPath subset");

        var args = new List<FpNode>();
        if (Current.Kind != FpTokenKind.RParen)
        {
            args.Add(ParseImplies());
            while (TryConsume(FpTokenKind.Comma))
                args.Add(ParseImplies());
        }
        Expect(FpTokenKind.RParen, "')'");

        if (args.Count < arity.Min || args.Count > arity.Max)
            throw new FhirPathUnsupportedException($"function '{name}()' expects {arity.Min}-{arity.Max} argument(s), got {args.Count}");
        return new FpNode.FunctionCall(source, name, args);
    }

    /// <summary>Type specifier: a bare name, or namespace-qualified (FHIR.dateTime /
    /// System.String) — the namespace is dropped; type matching is case-insensitive on
    /// the simple name (documented conflation, nothing in the corpus distinguishes them).</summary>
    private string ParseTypeSpecifier()
    {
        if (Current.Kind != FpTokenKind.Identifier)
            throw new FhirPathUnsupportedException($"expected type name at position {Current.Pos}, found '{Current.Text}'");
        var name = Advance().Text;
        if (TryConsume(FpTokenKind.Dot))
        {
            if (Current.Kind != FpTokenKind.Identifier)
                throw new FhirPathUnsupportedException($"expected type name at position {Current.Pos}, found '{Current.Text}'");
            name = Advance().Text;
        }
        return name;
    }
}
