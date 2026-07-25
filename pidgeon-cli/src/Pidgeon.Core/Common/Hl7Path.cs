// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Pidgeon.Core.Common;

/// <summary>
/// HL7-positional field path used by the PINS field-pinning workbench.
/// Format: <c>SEGMENT.FIELD[.COMPONENT[.SUBCOMPONENT]]</c>. Examples: <c>PID.19</c>,
/// <c>PID.5.1</c>, <c>MSH.10</c>, <c>OBX.5</c>. Segment is three uppercase
/// alphanumerics (first char must be a letter); field/component/subcomponent are
/// positive integers.
/// </summary>
public sealed record Hl7Path(string Segment, int Field, int? Component, int? Subcomponent)
{
    private static readonly Regex Pattern = new(
        @"^(?<segment>[A-Z][A-Z0-9]{2})\.(?<field>\d+)(?:\.(?<component>\d+)(?:\.(?<subcomponent>\d+))?)?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses an HL7-positional path. Returns false for semantic paths
    /// (<c>patient.mrn</c>) or anything that doesn't match the syntax — those
    /// flow through the entity-level <c>LockedValueApplier</c> instead.
    /// </summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out Hl7Path? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var match = Pattern.Match(input);
        if (!match.Success) return false;

        if (!int.TryParse(match.Groups["field"].Value, out var field) || field <= 0) return false;

        int? component = null;
        if (match.Groups["component"].Success)
        {
            if (!int.TryParse(match.Groups["component"].Value, out var c) || c <= 0) return false;
            component = c;
        }

        int? subcomponent = null;
        if (match.Groups["subcomponent"].Success)
        {
            if (!int.TryParse(match.Groups["subcomponent"].Value, out var s) || s <= 0) return false;
            subcomponent = s;
        }

        path = new Hl7Path(match.Groups["segment"].Value, field, component, subcomponent);
        return true;
    }

    /// <summary>
    /// True when this path looks plausibly HL7-positional. Used to decide
    /// whether a <c>LockedValue.FieldPath</c> belongs to the composer (positional)
    /// or to the semantic applier (paths like <c>patient.mrn</c>).
    /// </summary>
    public static bool LooksPositional(string? input)
        => TryParse(input, out _);

    public override string ToString()
    {
        return Subcomponent.HasValue
            ? $"{Segment}.{Field}.{Component}.{Subcomponent}"
            : Component.HasValue
                ? $"{Segment}.{Field}.{Component}"
                : $"{Segment}.{Field}";
    }
}
