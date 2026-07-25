// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// Composes a deterministic, clinically-coherent free-text note (NTE-3) from an observation the
/// message already carries. The note is a template grammar over the observation's own coded facts —
/// analyte, value, units, and the range clause is derived FROM the abnormal flag — so the note can
/// never contradict the message: the same flag that says "high" produces "above the reference range".
///
/// No model runs at generation time. Variety comes from two independent coordinate-addressed draws
/// (a fact-clause template and a procedural qualifier), so the same seed yields a byte-identical note
/// and a corpus spreads across the pool instead of repeating one string. The qualifier pool is
/// strictly procedural (verified / reviewed / released) — it makes no clinical assertion that could
/// interact with the coded value, keeping the coherent-by-construction guarantee.
/// </summary>
public sealed class Hl7NarrativeNoteComposer
{
    // Fact-clause templates by range disposition. {0}=analyte, {1}=value, {2}=units, {3}=range.
    // Every phrasing restates the same coded fact; the disposition is chosen from the flag, so the
    // clause and the flag agree by construction.
    private static readonly string[] WithinRange =
    {
        "{0} {1} {2} within reference range ({3}).",
        "{0}: {1} {2}, within reference limits ({3}).",
        "{0} result {1} {2} — normal, ref {3}.",
    };

    private static readonly string[] AboveRange =
    {
        "{0} {1} {2} above reference range ({3}).",
        "{0}: {1} {2}, elevated (ref {3}).",
        "{0} result {1} {2} — high, ref {3}.",
    };

    private static readonly string[] BelowRange =
    {
        "{0} {1} {2} below reference range ({3}).",
        "{0}: {1} {2}, low (ref {3}).",
        "{0} result {1} {2} — low, ref {3}.",
    };

    private static readonly string[] AbnormalUnspecified =
    {
        "{0} {1} {2} flagged abnormal (ref {3}).",
        "{0}: {1} {2}, outside reference limits (ref {3}).",
    };

    // No range/flag emitted with the OBX: state the value alone. {0}=analyte, {1}=value, {2}=units.
    private static readonly string[] ValueOnly =
    {
        "{0} {1} {2} reported.",
        "{0}: {1} {2}.",
        "{0} result {1} {2} on file.",
    };

    // Same shape when units are absent too (qualitative or unit-less). {0}=analyte, {1}=value.
    private static readonly string[] ValueNoUnits =
    {
        "{0} {1} reported.",
        "{0}: {1}.",
    };

    // Procedural qualifiers appended after the fact clause. Purely operational — none asserts a
    // clinical fact that could contradict the coded value. The empty string keeps some notes terse.
    private static readonly string[] Qualifiers =
    {
        "",
        " Result verified.",
        " Reviewed by laboratory.",
        " Specimen received in acceptable condition.",
        " Reported to ordering provider.",
    };

    /// <summary>
    /// Composes the note for <paramref name="observation"/>, addressing the template and qualifier
    /// pools by <paramref name="key"/> (a per-note coordinate). Two independent derivations keep the
    /// fact clause and the qualifier from co-varying, so the pool cross-product is reachable.
    /// </summary>
    public string Compose(EmittedObservation observation, GenerationKey key)
    {
        var name = observation.TestName?.Trim() ?? string.Empty;
        var value = observation.Value?.Trim() ?? string.Empty;
        var units = observation.Units?.Trim() ?? string.Empty;
        var range = observation.ReferenceRange?.Trim() ?? string.Empty;
        var flag = observation.AbnormalFlag?.Trim().ToUpperInvariant() ?? string.Empty;

        var (pool, hasRange) = SelectPool(flag, range, units);
        var template = Draw(pool, key.Derive("nte-template"));

        var clause = hasRange
            ? string.Format(template, name, value, units, range)
            : units.Length > 0
                ? string.Format(template, name, value, units)
                : string.Format(template, name, value);

        var qualifier = Draw(Qualifiers, key.Derive("nte-qualifier"));
        return (clause + qualifier).Trim();
    }

    // Chooses the fact-clause pool from the emitted range/flag. A disposition is used only when the
    // OBX actually carried the range+flag pair; otherwise the note states the value alone (never a
    // fabricated range claim).
    private static (string[] Pool, bool HasRange) SelectPool(string flag, string range, string units)
    {
        if (range.Length > 0 && flag.Length > 0)
        {
            return flag switch
            {
                "N" => (WithinRange, true),
                "H" or "HH" or ">" => (AboveRange, true),
                "L" or "LL" or "<" => (BelowRange, true),
                _ => (AbnormalUnspecified, true),
            };
        }

        return units.Length > 0 ? (ValueOnly, false) : (ValueNoUnits, false);
    }

    private static string Draw(string[] pool, GenerationKey key)
        => pool[key.AsRandom().Next(pool.Length)];
}
