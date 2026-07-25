// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Semantic;

/// <summary>
/// One clinical axis in the narrative-note corpus (b73): a family of free-text NTE templates that
/// restate a coded fact of a given kind (a medication, a diagnosis, a specimen). Every template
/// references only <see cref="Required"/> (the fact it wraps) and draws a procedural closer from
/// <see cref="Qualifiers"/>. See narrative-notes.yaml for the non-contradiction invariant these
/// families are authored — and lint-enforced — to hold.
/// </summary>
/// <param name="Axis">The axis name, e.g. "medication", "diagnosis", "specimen".</param>
/// <param name="Required">The placeholder every template in the axis must restate (the wrapped fact).</param>
/// <param name="Placeholders">Every field guaranteed present when a note for this axis fires; v1 templates use only <see cref="Required"/>.</param>
/// <param name="Templates">Fact-clause templates, addressed by coordinate at generation time.</param>
/// <param name="Qualifiers">Procedural-only closers appended after the fact clause; the empty string keeps some notes terse.</param>
public sealed record NarrativeNoteAxis(
    string Axis,
    string Required,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<string> Templates,
    IReadOnlyList<string> Qualifiers);

/// <summary>
/// Loads the checked-in, versioned narrative-note template corpus (narrative-notes.yaml) for the
/// non-lab clinical axes (b73). The corpus is drawn from BY COORDINATE at generation time — no model
/// runs at runtime — the same deterministic pattern reference-ranges.yaml and the comorbidity matrix
/// follow. Each axis's templates are authored, and lint-enforced, to be structurally incapable of
/// contradicting the fact they wrap (see NarrativeNoteCorpusVerifierTests).
/// </summary>
public interface INarrativeNoteCorpus
{
    /// <summary>The corpus schema version (bump on any breaking shape change).</summary>
    int Version { get; }

    /// <summary>The clinical axes carried by the corpus, in file order.</summary>
    IReadOnlyList<NarrativeNoteAxis> Axes { get; }

    /// <summary>The axis by name (case-insensitive), or null when the corpus carries no such axis.</summary>
    NarrativeNoteAxis? GetAxis(string axis);
}
