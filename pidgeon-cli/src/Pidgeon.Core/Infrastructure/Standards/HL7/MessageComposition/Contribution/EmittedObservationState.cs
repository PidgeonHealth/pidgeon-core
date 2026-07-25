// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// One observation fact the composer actually emitted (the OBX identity+value core), recorded so a
/// downstream narrative note can restate it. Because the note is composed FROM this record — the same
/// value/units/range/flag the OBX carries — a note can never contradict the message: it is coherent
/// by construction. Never re-draw the coordinator to obtain these; the coordinator re-samples result
/// values on each call, so a second draw would state a different number than the OBX.
/// </summary>
/// <param name="TestName">Human analyte name (OBX-3.2), e.g. "Potassium".</param>
/// <param name="Value">Observation value as emitted (OBX-5), e.g. "4.1".</param>
/// <param name="Units">Units as emitted (OBX-6), e.g. "mmol/L"; may be empty.</param>
/// <param name="ReferenceRange">Reference range as emitted (OBX-7); empty when the analyte was not honestly rangeable.</param>
/// <param name="AbnormalFlag">Abnormal flag as emitted (OBX-8); empty when no range/flag pair was emitted.</param>
public readonly record struct EmittedObservation(
    string TestName,
    string Value,
    string Units,
    string ReferenceRange,
    string AbnormalFlag);

/// <summary>
/// Per-message record of the observation facts the composer emitted, in emit order. Shared on
/// <see cref="SegmentGenerationContext"/> the same way as <c>ObxLabState</c>/<c>OrderMedicationState</c>
/// (reference-typed init prop, so per-segment <c>with</c> copies share one instance and it survives the
/// async hops in the composition chain). <c>ObxValueContributor</c> appends to it; a narrative-note
/// contributor reads the most recent entry to compose a coherent note.
/// </summary>
public sealed class EmittedObservationState
{
    private readonly List<EmittedObservation> _observations = new();

    /// <summary>The observations emitted so far this message, in emit order.</summary>
    public IReadOnlyList<EmittedObservation> Observations => _observations;

    /// <summary>Records one emitted observation fact.</summary>
    public void Record(EmittedObservation observation) => _observations.Add(observation);

    /// <summary>The most recently emitted observation, or null when none has been emitted yet.</summary>
    public EmittedObservation? MostRecent =>
        _observations.Count == 0 ? null : _observations[^1];
}
