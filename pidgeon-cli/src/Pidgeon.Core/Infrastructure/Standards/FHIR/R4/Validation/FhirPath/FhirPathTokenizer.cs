// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation.FhirPath;

internal enum FpTokenKind
{
    Identifier, String, Integer, Decimal, This, EnvConstant,
    Dot, LParen, RParen, LBracket, RBracket, Comma,
    Eq, NotEq, Lt, Gt, Le, Ge, Plus, Minus, Star, Amp, Pipe,
    End
}

internal readonly record struct FpToken(FpTokenKind Kind, string Text, int Pos);

/// <summary>
/// Scanner for the FHIRPath subset. Keywords (and/or/xor/implies/in/is/as/mod/true/false)
/// are emitted as plain identifiers — the parser interprets them positionally, which is
/// what makes them legal as member names (<c>.contains()</c>, <c>.not()</c>). Anything
/// outside the subset throws <see cref="FhirPathUnsupportedException"/> with a reason
/// naming the offending construct; the engine converts that to an Unsupported verdict.
/// </summary>
internal static class FhirPathTokenizer
{
    public static List<FpToken> Tokenize(string expr)
    {
        var tokens = new List<FpToken>();
        int i = 0;
        while (i < expr.Length)
        {
            char c = expr[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '\'') { tokens.Add(ReadString(expr, ref i)); continue; }
            if (char.IsDigit(c)) { tokens.Add(ReadNumber(expr, ref i)); continue; }
            if (char.IsLetter(c) || c == '_') { tokens.Add(ReadIdentifier(expr, ref i)); continue; }

            switch (c)
            {
                case '`': tokens.Add(ReadBacktick(expr, ref i)); continue;
                case '$': tokens.Add(ReadDollar(expr, ref i)); continue;
                case '%': tokens.Add(ReadEnvConstant(expr, ref i)); continue;
                case '.': tokens.Add(new(FpTokenKind.Dot, ".", i)); i++; continue;
                case '(': tokens.Add(new(FpTokenKind.LParen, "(", i)); i++; continue;
                case ')': tokens.Add(new(FpTokenKind.RParen, ")", i)); i++; continue;
                case '[': tokens.Add(new(FpTokenKind.LBracket, "[", i)); i++; continue;
                case ']': tokens.Add(new(FpTokenKind.RBracket, "]", i)); i++; continue;
                case ',': tokens.Add(new(FpTokenKind.Comma, ",", i)); i++; continue;
                case '=': tokens.Add(new(FpTokenKind.Eq, "=", i)); i++; continue;
                case '+': tokens.Add(new(FpTokenKind.Plus, "+", i)); i++; continue;
                case '-': tokens.Add(new(FpTokenKind.Minus, "-", i)); i++; continue;
                case '*': tokens.Add(new(FpTokenKind.Star, "*", i)); i++; continue;
                case '&': tokens.Add(new(FpTokenKind.Amp, "&", i)); i++; continue;
                case '|': tokens.Add(new(FpTokenKind.Pipe, "|", i)); i++; continue;
                case '!':
                    if (Peek(expr, i + 1) == '=') { tokens.Add(new(FpTokenKind.NotEq, "!=", i)); i += 2; continue; }
                    if (Peek(expr, i + 1) == '~')
                        throw new FhirPathUnsupportedException($"'!~' equivalence operator at position {i} is outside the FHIRPath subset");
                    throw new FhirPathUnsupportedException($"unexpected '!' at position {i}");
                case '<':
                    if (Peek(expr, i + 1) == '=') { tokens.Add(new(FpTokenKind.Le, "<=", i)); i += 2; continue; }
                    tokens.Add(new(FpTokenKind.Lt, "<", i)); i++; continue;
                case '>':
                    if (Peek(expr, i + 1) == '=') { tokens.Add(new(FpTokenKind.Ge, ">=", i)); i += 2; continue; }
                    tokens.Add(new(FpTokenKind.Gt, ">", i)); i++; continue;
                case '/':
                    throw new FhirPathUnsupportedException($"'/' (division or comment) at position {i} is outside the FHIRPath subset");
                case '~':
                    throw new FhirPathUnsupportedException($"'~' equivalence operator at position {i} is outside the FHIRPath subset");
                case '@':
                    throw new FhirPathUnsupportedException($"date/time literal at position {i} is outside the FHIRPath subset");
                case '{':
                case '}':
                    throw new FhirPathUnsupportedException($"empty-collection literal at position {i} is outside the FHIRPath subset");
                default:
                    throw new FhirPathUnsupportedException($"unexpected character '{c}' at position {i}");
            }
        }
        tokens.Add(new(FpTokenKind.End, "", expr.Length));
        return tokens;
    }

    private static char Peek(string expr, int i) => i < expr.Length ? expr[i] : '\0';

    private static FpToken ReadString(string expr, ref int i)
    {
        int start = i;
        i++; // opening quote
        var sb = new StringBuilder();
        while (i < expr.Length && expr[i] != '\'')
        {
            char c = expr[i];
            if (c == '\\')
            {
                if (i + 1 >= expr.Length)
                    throw new FhirPathUnsupportedException($"unterminated escape in string at position {i}");
                char esc = expr[i + 1];
                sb.Append(esc switch
                {
                    '\'' => '\'',
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'f' => '\f',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'u' => ReadUnicodeEscape(expr, i),
                    _ => throw new FhirPathUnsupportedException($"unsupported escape '\\{esc}' at position {i}")
                });
                i += esc == 'u' ? 6 : 2;
                continue;
            }
            sb.Append(c);
            i++;
        }
        if (i >= expr.Length)
            throw new FhirPathUnsupportedException($"unterminated string starting at position {start}");
        i++; // closing quote
        return new FpToken(FpTokenKind.String, sb.ToString(), start);
    }

    private static char ReadUnicodeEscape(string expr, int backslashPos)
    {
        if (backslashPos + 6 > expr.Length ||
            !ushort.TryParse(expr.AsSpan(backslashPos + 2, 4), System.Globalization.NumberStyles.HexNumber, null, out var code))
            throw new FhirPathUnsupportedException($"malformed \\u escape at position {backslashPos}");
        return (char)code;
    }

    private static FpToken ReadNumber(string expr, ref int i)
    {
        int start = i;
        while (i < expr.Length && char.IsDigit(expr[i])) i++;
        bool isDecimal = false;
        if (i + 1 < expr.Length && expr[i] == '.' && char.IsDigit(expr[i + 1]))
        {
            isDecimal = true;
            i++;
            while (i < expr.Length && char.IsDigit(expr[i])) i++;
        }
        return new FpToken(isDecimal ? FpTokenKind.Decimal : FpTokenKind.Integer, expr[start..i], start);
    }

    private static FpToken ReadIdentifier(string expr, ref int i)
    {
        int start = i;
        while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_')) i++;
        return new FpToken(FpTokenKind.Identifier, expr[start..i], start);
    }

    private static FpToken ReadBacktick(string expr, ref int i)
    {
        int start = i;
        int close = expr.IndexOf('`', i + 1);
        if (close < 0)
            throw new FhirPathUnsupportedException($"unterminated backtick identifier at position {start}");
        var name = expr[(i + 1)..close];
        i = close + 1;
        return new FpToken(FpTokenKind.Identifier, name, start);
    }

    private static FpToken ReadDollar(string expr, ref int i)
    {
        int start = i;
        i++;
        int wordStart = i;
        while (i < expr.Length && char.IsLetter(expr[i])) i++;
        var word = expr[wordStart..i];
        if (word == "this") return new FpToken(FpTokenKind.This, "$this", start);
        throw new FhirPathUnsupportedException($"'${word}' at position {start} is outside the FHIRPath subset");
    }

    private static FpToken ReadEnvConstant(string expr, ref int i)
    {
        int start = i;
        i++;
        int wordStart = i;
        while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_')) i++;
        if (i == wordStart)
            throw new FhirPathUnsupportedException($"malformed environment constant at position {start}");
        return new FpToken(FpTokenKind.EnvConstant, expr[wordStart..i], start);
    }
}
