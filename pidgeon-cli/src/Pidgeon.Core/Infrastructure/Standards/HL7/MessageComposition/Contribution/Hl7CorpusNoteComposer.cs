// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Pidgeon.Core.Application.Interfaces.Semantic;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// Composes deterministic narrative notes (NTE-3) for the non-lab clinical axes, drawing from the
/// checked-in <see cref="INarrativeNoteCorpus"/> (narrative-notes.yaml) BY COORDINATE — no model runs
/// at generation time. Mirrors <see cref="Hl7NarrativeNoteComposer"/>'s two independent draws (a
/// fact-clause template and a procedural qualifier) so the same (fact, coordinate) yields a
/// byte-identical note and a corpus spreads across the pool.
///
/// The corpus axes (medication, order-status) restate a single coded fact the message already
/// carries; the authored templates are lint-proven incapable of asserting a clinical disposition
/// (NarrativeNoteCorpusVerifierTests), so a note can only NAME the fact, never contradict it. The
/// Tier-2 order↔result note composes from the emitted OBX directly — it carries no coded number it
/// could get wrong, quoting ONE observation's own test/value/units, so it restates the result and
/// contradicts nothing regardless of what other cells the message carries.
/// </summary>
public sealed class Hl7CorpusNoteComposer
{
    private static readonly Regex Placeholder = new(@"\{(?<name>[a-zA-Z0-9_]+)\}", RegexOptions.Compiled);

    // order↔result phrasings ({0}=test, {1}=value, {2}=units). Every one names ONLY the observation's
    // own coded facts — no disposition — so it restates the result and contradicts nothing.
    private static readonly string[] OrderResultWithUnits =
    {
        "Result for {0}: {1} {2}.",
        "{0} result reported: {1} {2}.",
        "{0} resulted at {1} {2}.",
    };

    private static readonly string[] OrderResultNoUnits =
    {
        "Result for {0}: {1}.",
        "{0} result reported: {1}.",
        "{0} resulted at {1}.",
    };

    private readonly INarrativeNoteCorpus _corpus;

    public Hl7CorpusNoteComposer(INarrativeNoteCorpus corpus)
    {
        _corpus = corpus ?? throw new ArgumentNullException(nameof(corpus));
    }

    /// <summary>
    /// Composes a note for the named corpus axis restating <paramref name="fact"/> (the medication
    /// name, the ordered test, …), addressing the template and qualifier pools by two independent
    /// derivations of <paramref name="key"/>. Returns null when the corpus carries no such axis or the
    /// fact is blank — the caller then emits an NTE with no comment rather than boilerplate.
    /// </summary>
    public string? Compose(string axisName, string fact, GenerationKey key)
    {
        var trimmedFact = fact?.Trim() ?? string.Empty;
        if (trimmedFact.Length == 0)
            return null;

        var axis = _corpus.GetAxis(axisName);
        if (axis is null || axis.Templates.Count == 0)
            return null;

        var template = Draw(axis.Templates, key.Derive("nte-template"));
        var clause = Substitute(template, axis.Required, trimmedFact);

        var qualifier = axis.Qualifiers.Count > 0 ? Draw(axis.Qualifiers, key.Derive("nte-qualifier")) : string.Empty;
        return (clause + qualifier).Trim();
    }

    /// <summary>
    /// The order↔result Tier-2 note: "Result for {test}: {value} {units}." naming ONLY the emitted
    /// observation's own test, value and units, so it links the ordered service to its result without
    /// asserting any disposition. Falls to the no-units phrasing when the observation is unitless.
    /// </summary>
    public string ComposeOrderResult(EmittedObservation observation, GenerationKey key)
    {
        var name = observation.TestName?.Trim() ?? string.Empty;
        var value = observation.Value?.Trim() ?? string.Empty;
        var units = observation.Units?.Trim() ?? string.Empty;

        var pool = units.Length > 0 ? OrderResultWithUnits : OrderResultNoUnits;
        var template = Draw(pool, key.Derive("nte-template"));
        return (units.Length > 0
            ? string.Format(template, name, value, units)
            : string.Format(template, name, value)).Trim();
    }

    private static string Substitute(string template, string required, string fact)
        => Placeholder.Replace(template, m => m.Groups["name"].Value == required ? fact : string.Empty);

    private static string Draw(IReadOnlyList<string> pool, GenerationKey key)
        => pool[key.AsRandom().Next(pool.Count)];
}
