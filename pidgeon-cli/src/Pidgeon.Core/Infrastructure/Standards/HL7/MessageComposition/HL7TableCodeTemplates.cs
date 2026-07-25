// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Filters HL7 table entries that are NOTATION, not sendable values, out of value selection.
/// Two shapes are excluded:
/// <list type="bullet">
/// <item>Pattern-template notation (audit D-06) — table 0335 repeat patterns
/// ("Q&lt;integer&gt;J&lt;day#&gt;"), 0485 extended priorities ("TH&lt;integer&gt;"), 0356 escape
/// handling ("&lt;null&gt;"). The notation describes a code SHAPE.</item>
/// <item>Ellipsis range markers — table 0112 discharge disposition ("10 …19", "21 ... 29"), 0141
/// military rank ("O1 ... O9"), 0359 ranked diagnoses ("2 …"). The spec lists a numeric or graded
/// RANGE as one row instead of enumerating each code; the row is a placeholder for a concrete
/// in-range value, never a value itself. Emitting it verbatim leaks "10 …19" as a field value
/// (audit HL7-LEN-001 / range-marker-as-value) — the exact bug this exclusion kills.</item>
/// </list>
/// Neither is ever drawn onto the wire. Bare comparison-operator codes ("&lt;", "&gt;") carry no
/// ellipsis and remain literal, selectable values. Pure functions only.
/// </summary>
internal static class HL7TableCodeTemplates
{
    private static readonly Regex Placeholder = new("<[A-Za-z#/ ]+>", RegexOptions.Compiled);

    /// <summary>True when the code contains template-placeholder notation like &lt;integer&gt;.</summary>
    public static bool IsTemplateCode(string code) => Placeholder.IsMatch(code);

    /// <summary>
    /// True when the code is an ellipsis range marker ("10 …19", "O1 ... O9", "2 …") — a range
    /// placeholder the spec publishes in place of enumerated codes, not a sendable value. Mirrors
    /// the ellipsis normalization the validator's
    /// <see cref="Validation.HL7FieldFormatRules.MatchesNumericRange"/> applies (Unicode U+2026 and
    /// the three-ASCII-dot spelling are the same marker), and covers graded ranges (O1 … O9) the
    /// validator's numeric-only matcher does not, since no real HL7 code contains an ellipsis.
    /// </summary>
    public static bool IsRangeMarker(string code) => code.Contains('…') || code.Contains("...");

    /// <summary>
    /// The table's selectable codes: values minus template-notation and ellipsis-range entries.
    /// Returns the original list unchanged (no allocation, identical draw indices) for the
    /// overwhelmingly common table with neither; empty when every row is notation/range, which
    /// callers must treat exactly like an unpopulated table.
    /// </summary>
    public static IReadOnlyList<TableValue> Selectable(IReadOnlyList<TableValue> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (IsTemplateCode(values[i].Code) || IsRangeMarker(values[i].Code))
                return values.Where(v => !IsTemplateCode(v.Code) && !IsRangeMarker(v.Code)).ToList();
        }

        return values;
    }
}
