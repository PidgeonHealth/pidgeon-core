// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.Validation;

/// <summary>
/// Static format and reference data for the per-field HL7 conformance rules in
/// <see cref="HL7FieldRuleValidator"/>: date/time format patterns per datatype,
/// numeric-range table-code matching, and the data-type full names used in
/// diagnostics. Genuine constants and pure functions only — no state, no I/O.
/// </summary>
internal static class HL7FieldFormatRules
{
    /// <summary>
    /// Format rule for one HL7 date/time datatype: the wire pattern plus the
    /// vocabulary used in diagnostics.
    /// </summary>
    internal sealed record DateTimeRule(Regex Pattern, string ExpectedFormat, string Noun, string Example);

    // HL7 timestamp pattern: YYYY[MM[DD[HH[MM[SS[.S[S[S[S]]]]]]]]][+/-ZZZZ]
    private static readonly Regex TimestampPattern = new(
        @"^\d{4}(\d{2}(\d{2}(\d{2}(\d{2}(\d{2}(\.\d{1,4})?)?)?)?)?)?([+-]\d{4})?$",
        RegexOptions.Compiled);

    // HL7 date pattern: YYYY[MM[DD]]
    private static readonly Regex DatePattern = new(
        @"^\d{4}(\d{2}(\d{2})?)?$",
        RegexOptions.Compiled);

    // HL7 time pattern: HH[MM[SS[.S[S[S[S]]]]]][+/-ZZZZ]
    private static readonly Regex TimePattern = new(
        @"^\d{2}(\d{2}(\d{2}(\.\d{1,4})?)?)?([+-]\d{4})?$",
        RegexOptions.Compiled);

    // Format rules per HL7 date/time datatype. TS (v2.3–2.5.1) and DTM (its v2.6+
    // replacement) share the full timestamp shape; DT is date-only; TM is time-only.
    // v2.6+ carries ZERO TS-typed fields — every timestamp there is DTM — so a
    // TS-only check leaves the datetime rule dead from 2.6 on.
    internal static readonly Dictionary<string, DateTimeRule> DateTimeRules = new(StringComparer.Ordinal)
    {
        ["TS"] = new(TimestampPattern, "YYYY[MM[DD[HH[MM[SS[.S+]]]]]][+/-ZZZZ]", "timestamp", "20240115143022 for Jan 15 2024 14:30:22"),
        ["DTM"] = new(TimestampPattern, "YYYY[MM[DD[HH[MM[SS[.S+]]]]]][+/-ZZZZ]", "timestamp", "20240115143022 for Jan 15 2024 14:30:22"),
        ["DT"] = new(DatePattern, "YYYY[MM[DD]]", "date", "20240115 for Jan 15 2024"),
        ["TM"] = new(TimePattern, "HH[MM[SS[.S+]]][+/-ZZZZ]", "time", "143022 for 14:30:22"),
    };

    // Some HL7 standard tables publish a NUMERIC RANGE as a single value rather than
    // enumerating each code, in two forms:
    //   open-ended  "N …" / "N ..."     -> any integer >= N   (e.g. 0359 "2 …" = ranked secondary diagnoses 2+)
    //   bounded     "N … M" / "N ... M" -> any integer in [N, M] (e.g. 0112 "10 … 19", 0153 "70 ... 72")
    // Matches both the Unicode ellipsis (U+2026) and the three-ASCII-dot spelling, with
    // optional surrounding spaces. Non-range codes never match, so ordinary enum tables are
    // unaffected.
    private static readonly Regex NumericRangeMarker = new(
        @"^(?<lo>\d+)\s*\.\.\.\s*(?<hi>\d+)?$",
        RegexOptions.Compiled);

    // Common HL7 v2 data type full names for educational output
    internal static readonly Dictionary<string, string> DataTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AD"] = "Address",
        ["CE"] = "Coded Element",
        ["CF"] = "Coded Element with Formatted Values",
        ["CK"] = "Composite ID with Check Digit",
        ["CM"] = "Composite",
        ["CN"] = "Composite ID Number and Name",
        ["CP"] = "Composite Price",
        ["CQ"] = "Composite Quantity with Units",
        ["CX"] = "Extended Composite ID with Check Digit",
        ["DT"] = "Date",
        ["ED"] = "Encapsulated Data",
        ["FT"] = "Formatted Text",
        ["HD"] = "Hierarchic Designator",
        ["ID"] = "Coded Value for HL7 Tables",
        ["IS"] = "Coded Value for User-Defined Tables",
        ["MO"] = "Money",
        ["NM"] = "Numeric",
        ["PL"] = "Person Location",
        ["PT"] = "Processing Type",
        ["RP"] = "Reference Pointer",
        ["SI"] = "Sequence ID",
        ["SN"] = "Structured Numeric",
        ["ST"] = "String",
        ["TM"] = "Time",
        ["TN"] = "Telephone Number",
        ["TS"] = "Timestamp",
        ["TX"] = "Text Data",
        ["XAD"] = "Extended Address",
        ["XCN"] = "Extended Composite ID and Name",
        ["XON"] = "Extended Composite Name for Organizations",
        ["XPN"] = "Extended Person Name",
        ["XTN"] = "Extended Telecommunication Number",
    };

    /// <summary>
    /// True when <paramref name="tableCode"/> is a published numeric-range value
    /// ("N …" open-ended, or "N … M" bounded) that admits <paramref name="fieldValue"/>.
    /// Returns false for any non-range code and any non-integer field value, so ordinary
    /// enumerated tables (M/F/O/U, A/C/F/…) fall through to the exact-match path unchanged.
    /// </summary>
    internal static bool MatchesNumericRange(string tableCode, string fieldValue)
    {
        // Normalize the Unicode ellipsis (U+2026) some versions use to the ASCII spelling,
        // so a single ASCII pattern covers both "2 …" and "2 ..." forms.
        var match = NumericRangeMarker.Match(tableCode.Replace("…", "..."));
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(fieldValue, out var n))
        {
            return false;
        }

        var lo = int.Parse(match.Groups["lo"].Value);
        if (n < lo)
        {
            return false;
        }

        return !match.Groups["hi"].Success || n <= int.Parse(match.Groups["hi"].Value);
    }
}
