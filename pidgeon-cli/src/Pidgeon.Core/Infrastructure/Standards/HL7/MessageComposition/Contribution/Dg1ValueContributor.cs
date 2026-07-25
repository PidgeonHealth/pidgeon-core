// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Services.Clinical;
using Pidgeon.Core.Domain.Clinical;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// Contributes DG1 (Diagnosis) content from the clinical scenario coordinator. Emits the ICD-10
/// code as a semantic <see cref="FieldValue.Coded"/> so the serializer renders it as the version's
/// coded type: CE at v2.3 through v2.5.1, CWE at v2.6+. The coding method (DG1-2) and description
/// (DG1-4) are also contributed; they are withdrawn ("W") at v2.6+ and the serializer suppresses
/// them there, so no version branch lives here.
/// </summary>
public class Dg1ValueContributor : IValueContributor
{
    private readonly ClinicalScenarioCoordinator _scenarioCoordinator;
    private readonly ILogger<Dg1ValueContributor> _logger;

    // Cache diagnoses per (message, scenario) so multiple DG1 setIds within one message draw from a
    // stable list. This is load-bearing, not just an optimization: GetDiagnoses advances the coordinator
    // RNG and re-rolls secondary diagnoses on each call, so a single cached selection is what keeps the
    // segment deterministic across setIds and byte-identical to the builder it replaces. The message key
    // is part of the key because the contributor is AddScoped and outlives a single generation call: two
    // same-scope messages that draw the same scenario id must NOT share one call's diagnoses — the
    // message epoch invalidates the cache per message, so each draws its own.
    private List<DiagnosisCode>? _cachedDiagnoses;
    private string? _cachedScenarioId;
    private GenerationKey _cachedMessageKey;

    public IReadOnlyList<string> SupportedSegments => new[] { "DG1" };
    public int Priority => 100;

    public Dg1ValueContributor(
        ClinicalScenarioCoordinator scenarioCoordinator,
        ILogger<Dg1ValueContributor> logger)
    {
        _scenarioCoordinator = scenarioCoordinator ?? throw new ArgumentNullException(nameof(scenarioCoordinator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<CoherentValueSet> ContributeAsync(
        SegmentGenerationContext context,
        GenerationOptions options,
        int setId = 1)
    {
        var currentScenarioId = _scenarioCoordinator.GetCurrentScenario().ScenarioId;
        if (_cachedDiagnoses == null
            || _cachedScenarioId != currentScenarioId
            || !_cachedMessageKey.Equals(context.MessageKey))
        {
            _cachedDiagnoses = _scenarioCoordinator.GetDiagnoses(maxDiagnoses: 5);
            _cachedScenarioId = currentScenarioId;
            _cachedMessageKey = context.MessageKey;
        }
        var diagnoses = _cachedDiagnoses;

        if (!diagnoses.Any())
        {
            _logger.LogDebug("No diagnoses available from clinical scenario, contributing minimal DG1");
            return Task.FromResult(BuildFallback(setId, context.Clock));
        }

        // Primary diagnosis (setId=1) gets type A=Admitting; subsequent are W=Working.
        var diagnosis = diagnoses[(setId - 1) % diagnoses.Count];
        var diagnosisType = setId == 1 ? "A" : "W";

        // DG1.5 (Diagnosis Date/Time) anchors to the encounter event on the single seeded timeline:
        // the EVN.2 anchor the temporal resolver records when present, else the per-message clock.
        // Diagnosed within the 48 hours leading up to that anchor (one draw off the DG1-5 coordinate,
        // so the offset is a pure function of the segment key, not message-wide draw order).
        var diagnosisAnchor = context.GeneratedTimestamps.TryGetValue("EVN.2", out var eventTime)
            ? eventTime
            : context.Clock;
        var diagnosisDateTime = diagnosisAnchor.AddHours(-context.Key.Derive(5).AsRandom().Next(0, 48))
            .ToString("yyyyMMddHHmmss");

        var values = new Dictionary<int, FieldValue>
        {
            [1] = new FieldValue.Primitive(setId.ToString()),                                  // DG1-1  Set ID
            [2] = new FieldValue.Primitive("I10"),                                             // DG1-2  Coding Method (W at v2.6+)
            [3] = new FieldValue.Coded(diagnosis.Code, diagnosis.Description, diagnosis.CodingSystem), // DG1-3 Diagnosis Code
            [4] = new FieldValue.Primitive(diagnosis.Description),                             // DG1-4  Description (W at v2.6+)
            [5] = new FieldValue.Primitive(diagnosisDateTime),                                 // DG1-5  Diagnosis Date/Time
            [6] = new FieldValue.Primitive(diagnosisType),                                     // DG1-6  Diagnosis Type
            [15] = new FieldValue.Primitive(setId.ToString())                                  // DG1-15 Diagnosis Priority
        };

        _logger.LogDebug("Contributed DG1 {SetId} for diagnosis {Code} ({Description}), type={Type}",
            setId, diagnosis.Code, diagnosis.Description, diagnosisType);

        return Task.FromResult(CoherentValueSet.SingleGroup(values));
    }

    private static CoherentValueSet BuildFallback(int setId, DateTime clock)
    {
        var values = new Dictionary<int, FieldValue>
        {
            [1] = new FieldValue.Primitive(setId.ToString()),
            [2] = new FieldValue.Primitive("I10"),
            [3] = new FieldValue.Coded("Z00.00", "General examination", "ICD10"),
            [4] = new FieldValue.Primitive("General examination"),
            [5] = new FieldValue.Primitive(clock.ToString("yyyyMMddHHmmss")),
            [6] = new FieldValue.Primitive("W"),
            [15] = new FieldValue.Primitive(setId.ToString())
        };
        return CoherentValueSet.SingleGroup(values);
    }
}
