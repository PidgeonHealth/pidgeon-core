// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Services.Clinical;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// Contributes OBX (Observation/Result) content from the clinical scenario coordinator. Emits the
/// LOINC observation identifier as a semantic <see cref="FieldValue.Coded"/> so the serializer
/// renders it as the version's coded type (CE at v2.3 through v2.5.1, CWE at v2.6+).
///
/// The identity+value core — OBX-2 "NM", OBX-3 (LOINC), OBX-5 (value), OBX-6 (units) and OBX-11
/// "F" — is whole-or-absent: a value only travels with its identifier and units. OBX-7 (reference
/// range) and OBX-8 (abnormal flag) are a SEPARATE paired unit computed by the coordinator from the
/// curated per-LOINC bounds; they are emitted together when the analyte is covered and its unit
/// matches, and omitted together when it is not. An absent honest range beats a fabricated one —
/// the flag can never contradict the value it was computed from.
/// </summary>
public class ObxValueContributor : IValueContributor
{
    private readonly ClinicalScenarioCoordinator _scenarioCoordinator;
    private readonly ILogger<ObxValueContributor> _logger;

    // Per-message lab cache keyed to (message, scenario). Load-bearing, not just an optimization:
    // GetLabTestsAsync re-draws the coordinator RNG on each call (random result values), so a single
    // cached selection is what keeps OBX deterministic across setIds and byte-identical to the builder
    // it replaces. The message key is part of the key because the contributor is AddScoped and outlives
    // a single generation call: two same-scope messages that draw the same scenario id must NOT share
    // one call's labs — the message epoch invalidates the cache per message, so each draws its own.
    private List<LabTestResult>? _cachedLabTests;
    private string? _cachedScenarioId;
    private GenerationKey _cachedMessageKey;

    public IReadOnlyList<string> SupportedSegments => new[] { "OBX" };
    public int Priority => 100;

    public ObxValueContributor(
        ClinicalScenarioCoordinator scenarioCoordinator,
        ILogger<ObxValueContributor> logger)
    {
        _scenarioCoordinator = scenarioCoordinator ?? throw new ArgumentNullException(nameof(scenarioCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CoherentValueSet> ContributeAsync(
        SegmentGenerationContext context,
        GenerationOptions options,
        int setId = 1)
    {
        var currentScenarioId = _scenarioCoordinator.GetCurrentScenario().ScenarioId;
        if (_cachedLabTests == null
            || _cachedScenarioId != currentScenarioId
            || !_cachedMessageKey.Equals(context.MessageKey))
        {
            var loaded = await _scenarioCoordinator.GetLabTestsAsync(maxTests: 10).ConfigureAwait(false);

            // Only keep labs that form a coherent numeric OBX core: a populated value (OBX-5) must
            // travel with units (OBX-6). Qualitative tests are unitless by design (e.g. blood culture,
            // INR — Units="" in LabReferenceRangeProvider), so emitting them as an NM result with a
            // value but no units would be a half-populated observation; they are dropped from the
            // numeric-result cycle. Reference range (OBX-7) and abnormal flag (OBX-8) are NOT required
            // here: the coordinator omits them together for an analyte it cannot honestly range, and an
            // OBX carrying value+units with no range is spec-legal and honest.
            _cachedLabTests = loaded
                .Where(t => !string.IsNullOrEmpty(t.Value)
                         && !string.IsNullOrEmpty(t.Units))
                .ToList();
            _cachedScenarioId = currentScenarioId;
            _cachedMessageKey = context.MessageKey;
        }
        var labTests = _cachedLabTests;

        if (!labTests.Any())
        {
            _logger.LogDebug("No coherent unit-bearing lab tests available from clinical scenario, contributing minimal OBX");
            return BuildFallback(setId);
        }

        // Cross-cell observation selection. The Set ID (OBX-1) restarts at 1 for each OBX cell
        // (the ORDER OBSERVATION result group, then the SPECIMEN group at v2.5.1+, three groups at
        // v2.7), so indexing labs by (setId - 1) makes every cell restart at the first lab and emit
        // it byte-identically twice. Instead the labs are drawn against a message-wide cursor on the
        // shared per-message lab state, so a later cell continues the cycle rather than repeating the
        // first observation. Once the distinct-lab pool is exhausted the cursor clamps to the last
        // lab, which — being the most recent one this cell emitted when it wraps — the composer's
        // per-cell duplicate guard catches and stops on, rather than appending a fabricated repeat.
        var obxLabs = context.ObxLabs;
        var observationOrdinal = obxLabs.NextObservationOrdinal;
        obxLabs.NextObservationOrdinal = observationOrdinal + 1;
        var testIndex = observationOrdinal < labTests.Count ? observationOrdinal : labTests.Count - 1;
        var test = labTests[testIndex];

        var values = new Dictionary<int, FieldValue>
        {
            [1] = new FieldValue.Primitive(setId.ToString()),                              // OBX-1  Set ID
            [2] = new FieldValue.Primitive("NM"),                                          // OBX-2  Value Type (numeric)
            [3] = new FieldValue.Coded(test.LoincCode, test.TestName, "LN"),               // OBX-3  Observation Identifier
            [5] = new FieldValue.Primitive(test.Value),                                    // OBX-5  Observation Value
            [6] = new FieldValue.Primitive(test.Units),                                    // OBX-6  Units
            [11] = new FieldValue.Primitive("F")                                           // OBX-11 Result Status (Final)
        };

        // OBX-7 (reference range) and OBX-8 (abnormal flag) are a computed pair: the coordinator
        // supplies both when the analyte is covered and its unit matches, or neither when it is not.
        // Emit them together or not at all — never a range without a flag, never a fabricated range.
        if (!string.IsNullOrEmpty(test.ReferenceRange) && !string.IsNullOrEmpty(test.AbnormalFlag))
        {
            values[7] = new FieldValue.Primitive(test.ReferenceRange);                     // OBX-7  Reference Range
            values[8] = new FieldValue.Primitive(test.AbnormalFlag);                       // OBX-8  Abnormal Flags
        }

        // Record the emitted identity+value core so a downstream narrative note can restate it
        // (coherent by construction — the note reads these exact bytes, never re-draws the
        // coordinator). Range and flag are recorded only when the paired unit was emitted; a note
        // must not claim a range this OBX did not carry.
        var emittedRange = values.ContainsKey(7) ? test.ReferenceRange : string.Empty;
        var emittedFlag = values.ContainsKey(8) ? test.AbnormalFlag : string.Empty;
        context.EmittedObservations.Record(new EmittedObservation(
            test.TestName, test.Value, test.Units, emittedRange, emittedFlag));

        _logger.LogDebug("Contributed OBX {SetId} for test {TestName} (LOINC: {LoincCode})",
            setId, test.TestName, test.LoincCode);

        return CoherentValueSet.SingleGroup(values);
    }

    private static CoherentValueSet BuildFallback(int setId)
    {
        // No coherent lab available: emit the minimal anchored OBX with no value. No reference
        // range or flag is invented for a non-existent value — the same honesty rule as the
        // populated path (a fabricated N flag on an empty value is exactly what this lane removes).
        var values = new Dictionary<int, FieldValue>
        {
            [1] = new FieldValue.Primitive(setId.ToString()),
            [2] = new FieldValue.Primitive("NM"),
            [3] = new FieldValue.Coded("UNKNOWN", "Unknown Test", "LN"),
            [5] = new FieldValue.Primitive(string.Empty),
            [6] = new FieldValue.Primitive(string.Empty),
            [11] = new FieldValue.Primitive("F")
        };
        return CoherentValueSet.SingleGroup(values);
    }
}
