// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// Contributes NTE (Notes and Comments) content as a note derived from the message's own coded
/// facts. The narrative axis is chosen by the NTE's structural position (<see cref="NarrativeNotePosition"/>),
/// so every note restates a fact the message already carries and can never contradict it:
/// <list type="bullet">
///   <item><b>Observation</b> (post-OBX) — restates the OBX lab result via
///   <see cref="Hl7NarrativeNoteComposer"/>. In an ORU ORDER OBSERVATION group a non-flagged result
///   instead gets the Tier-2 order↔result note; a flagged result always keeps the lab restatement
///   (the SEM-F15 anchor and reviewer-packet note).</item>
///   <item><b>OrderStatus</b> (post-OBR) — restates the ordered test (OBR-4, the message's first lab)
///   from the medication/order corpus.</item>
///   <item><b>Medication</b> (post-RXE/RXO) — restates the ordered chain medication from the corpus.</item>
/// </list>
///
/// NTE is opt-in: its inclusion odds are position-aware in <c>HL7SegmentStatistics</c> and gated by
/// <c>SegmentInclusionPolicy</c> on the presence of the position's fact. This contributor owns only
/// the CONTENT once a caller — or that occasional-inclusion policy — has decided the NTE is present.
/// With no fact in scope it emits an NTE with no comment rather than inventing one: an honest empty
/// remark, never boilerplate.
/// </summary>
public class NteValueContributor : IValueContributor
{
    private readonly Hl7NarrativeNoteComposer _composer;
    private readonly Hl7CorpusNoteComposer _corpusComposer;
    private readonly ILogger<NteValueContributor> _logger;

    public IReadOnlyList<string> SupportedSegments => new[] { "NTE" };
    public int Priority => 100;

    public NteValueContributor(
        Hl7NarrativeNoteComposer composer,
        Hl7CorpusNoteComposer corpusComposer,
        ILogger<NteValueContributor> logger)
    {
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _corpusComposer = corpusComposer ?? throw new ArgumentNullException(nameof(corpusComposer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<CoherentValueSet> ContributeAsync(
        SegmentGenerationContext context,
        GenerationOptions options,
        int setId = 1)
    {
        var values = new Dictionary<int, FieldValue>
        {
            [1] = new FieldValue.Primitive(setId.ToString()),   // NTE-1  Set ID
        };

        // The note is a pure (message, coordinate) function: context.Key is already the NTE
        // segment-instance coordinate (the composer derives NTE.setId before calling), so every draw
        // below is byte-reproducible and distinct across NTE instances.
        var note = Compose(context);
        if (!string.IsNullOrEmpty(note))
        {
            values[3] = new FieldValue.Primitive(note);         // NTE-3  Comment
            _logger.LogDebug("Contributed narrative NTE {SetId}: {Note}", setId, note);
        }
        else
        {
            _logger.LogDebug("No coded fact in scope for NTE {SetId}; contributing with no comment", setId);
        }

        return Task.FromResult(CoherentValueSet.SingleGroup(values));
    }

    private string? Compose(SegmentGenerationContext context) =>
        NarrativeNotePosition.Classify(context.GroupPath) switch
        {
            NarrativeNoteKind.Observation => ComposeObservationNote(context),
            NarrativeNoteKind.OrderStatus => ComposeOrderStatusNote(context),
            NarrativeNoteKind.Medication => ComposeMedicationNote(context),
            _ => null,
        };

    // The post-OBX observation note. A flagged result — and any observation NTE outside the ORU
    // ORDER OBSERVATION group — keeps the b71 lab restatement byte-for-byte (the coordinate and the
    // composer call are unchanged), so the SEM-F15 anchor and the reviewer-packet note are preserved.
    // Inside the ORU order group a non-flagged result gains variety: the order↔result note, which
    // restates that ONE observation's own test/value/units (never a cross-OBX or message-wide claim, so
    // it stays coherent regardless of what other cells the message carries). A message-wide all-normal
    // panel summary is deliberately NOT emitted here: a forward-pass contributor cannot see the OBX of a
    // later SPECIMEN cell, so "all results within reference limits" could contradict an abnormal result
    // it has not reached yet. That summary needs whole-message OBX visibility (a post-assembly pass) and
    // is deferred rather than risk violating the non-contradiction invariant.
    private string? ComposeObservationNote(SegmentGenerationContext context)
    {
        var observation = context.EmittedObservations.MostRecent;
        if (observation is not { } obs)
            return null;

        if (NarrativeNotePosition.InOrderObservationResultGroup(context.GroupPath) && !IsAbnormal(obs))
            return _corpusComposer.ComposeOrderResult(obs, context.Key.Derive("nte-order-result"));

        return _composer.Compose(obs, context.Key.Derive("nte-narrative"));
    }

    // The post-OBR order note: restate the ordered test (OBR-4). ClinicalLabResultResolver caches the
    // order's labs as the message's first lab, so the ordered test is available before this NTE.
    private string? ComposeOrderStatusNote(SegmentGenerationContext context)
    {
        var orderedTest = context.ObxLabs.Results is { Count: > 0 } labs
            ? labs[0].TestName?.Trim() ?? string.Empty
            : string.Empty;
        if (orderedTest.Length == 0)
            return null;

        return _corpusComposer.Compose("order-status", orderedTest, context.Key.Derive("nte-order-status"));
    }

    // The post-RXE/RXO pharmacy-order note: restate the chain medication. OrderMedicationSource
    // resolves it once per message when a give/dispense position asks, so it is present before this NTE.
    private string? ComposeMedicationNote(SegmentGenerationContext context)
    {
        var med = context.OrderMedication.Value;
        if (med is null || string.IsNullOrWhiteSpace(med.DrugName))
            return null;

        return _corpusComposer.Compose("medication", med.DrugName, context.Key.Derive("nte-medication"));
    }

    // An abnormal flag (anything the OBX carried other than the normal "N" or a blank) — the flagged
    // result whose lab restatement the SEM-F15 mutation rewrites, and which keeps the b71 restatement
    // rather than the order↔result variant so the anchor and reviewer-packet note are preserved.
    private static bool IsAbnormal(EmittedObservation obs)
    {
        var flag = obs.AbnormalFlag?.Trim() ?? string.Empty;
        return flag.Length > 0 && !flag.Equals("N", StringComparison.OrdinalIgnoreCase);
    }
}
