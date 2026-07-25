// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Clinical;
using Pidgeon.Core.Domain.Clinical;
using Pidgeon.Core.Domain.Clinical.Entities;

namespace Pidgeon.Core.Application.Services.Clinical;

/// <summary>
/// Coordinates clinical scenario SELECTION (which scenario, diagnoses, medications, and which lab
/// analytes to order). Ensures diagnoses, medications, and lab tests are clinically coherent. When
/// IClinicalRelationshipGraph is available, uses it as the canonical source of LOINC/RxNorm/CPT codes
/// (eliminating the split-brain problem); falls back to ClinicalScenarioRepository otherwise.
///
/// Lab VALUATION is delegated to the shared realism model (Realism Program S2): ScenarioLabValuer
/// over LabValueEngine values the ordered analytes as one correlated, condition-conditioned vector
/// keyed on a shared labs coordinate (RI-1/RI-2/RI-4). The coordinator no longer draws values or
/// derives flags — that value logic (uniform draw, midpoint flag, band-as-range) is deleted.
/// </summary>
public class ClinicalScenarioCoordinator
{
    private readonly ILogger<ClinicalScenarioCoordinator> _logger;
    private readonly ClinicalScenarioRepository _scenarioRepository;
    private readonly IClinicalRelationshipGraph? _graph;
    private readonly LabReferenceRangeProvider _rangeProvider;
    private readonly Realism.ScenarioLabValuer? _labValuer;

    // Per-message RNG governing scenario / diagnosis / medication / lab SELECTION (not valuation).
    // Reseeded via ApplySeed at the start of each message so a seeded generation run selects the
    // same clinical content reproducibly. Non-readonly for that reseed.
    private Random _random;

    // The coordinate the lab vector is valued from (RI-4): a pure function of (seed, message-type,
    // index), narrowed to a "labs" child. Set alongside _random in ApplySeed so valuation is
    // draw-order-, cache-, and parallelism-invariant. Default keeps a directly-constructed
    // coordinator (no ApplySeed) working with a fresh random root.
    private Pidgeon.Core.Generation.GenerationKey _labsKey =
        Pidgeon.Core.Generation.GenerationKey.Root(Random.Shared.Next()).Derive("labs");

    // Instance fields for scenario state. Previous AsyncLocal approach was broken:
    // Task.Yield() in segment builders creates child execution contexts, and AsyncLocal
    // changes in child contexts don't propagate back to the parent. This caused each
    // builder call to see null and initialize a NEW random scenario mid-message.
    // Instance fields work because the coordinator is AddScoped (singleton in CLI),
    // and ClearScenario() is called between messages.
    private ClinicalScenario? _currentScenario;
    private DiagnosisCode? _selectedPrimary;

    /// <summary>
    /// Cached patient for cross-message cohort sequences (IsCohortSequence=true).
    /// When set, all message types in the sequence share the same demographics.
    /// Cleared by ClearScenario().
    /// </summary>
    public Patient? CohortPatient { get; set; }

    // Regex to identify real LOINC test codes (numeric format like "2160-0", "718-7").
    // Excludes LOINC Answer codes (LA*) and Part codes (LP*) from UMLS data.
    private static readonly Regex LoincTestCodePattern = new(@"^\d+-\d+$", RegexOptions.Compiled);

    public ClinicalScenarioCoordinator(
        ILogger<ClinicalScenarioCoordinator> logger,
        ClinicalScenarioRepository scenarioRepository,
        LabReferenceRangeProvider rangeProvider,
        IClinicalRelationshipGraph? graph = null,
        Realism.ScenarioLabValuer? labValuer = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scenarioRepository = scenarioRepository ?? throw new ArgumentNullException(nameof(scenarioRepository));
        _rangeProvider = rangeProvider ?? throw new ArgumentNullException(nameof(rangeProvider));
        _graph = graph;
        _labValuer = labValuer;
        _random = new Random();
    }

    /// <summary>
    /// (Re)seeds the scenario-selection RNG and the lab-valuation coordinate from a
    /// coordinate-addressed GenerationKey: clinical-content selection AND the lab vector become a pure
    /// function of (seed, message-type, index) rather than RNG draw order. Called once per message by
    /// the plugin, alongside <see cref="ClearScenario"/>.
    /// </summary>
    public void ApplySeed(Pidgeon.Core.Generation.GenerationKey scenarioKey)
    {
        _random = scenarioKey.AsRandom();
        _labsKey = scenarioKey.Derive("labs");
    }

    /// <summary>
    /// Initializes a new clinical scenario for the current message generation context.
    /// Should be called once per message to establish clinical coherence.
    /// </summary>
    public void InitializeScenario(string? specificScenarioId = null)
    {
        _currentScenario = string.IsNullOrEmpty(specificScenarioId)
            ? _scenarioRepository.GetRandomScenario(_random)
            : _scenarioRepository.GetScenarioById(specificScenarioId) ?? _scenarioRepository.GetRandomScenario(_random);

        _logger.LogDebug("Initialized clinical scenario: {ScenarioName} ({ScenarioId})",
            _currentScenario?.Name, _currentScenario?.ScenarioId);
    }

    /// <summary>
    /// Gets the current active scenario (or initializes one if none exists).
    /// </summary>
    public ClinicalScenario GetCurrentScenario()
    {
        if (_currentScenario == null)
        {
            InitializeScenario();
        }

        return _currentScenario!;
    }

    /// <summary>
    /// Clears the current scenario context.
    /// </summary>
    public void ClearScenario()
    {
        _currentScenario = null;
        _selectedPrimary = null;
        CohortPatient = null;
    }

    /// <summary>
    /// Gets appropriate diagnosis codes for the current scenario.
    /// Returns 1-3 diagnoses with primary diagnosis always included.
    /// </summary>
    public List<DiagnosisCode> GetDiagnoses(int maxDiagnoses = 3)
    {
        var scenario = GetCurrentScenario();
        var diagnoses = new List<DiagnosisCode>();

        // Always include primary diagnosis — use cached selection for consistency with lab resolution
        if (scenario.PrimaryDiagnoses.Any())
        {
            if (_selectedPrimary == null)
            {
                _selectedPrimary = scenario.PrimaryDiagnoses[_random.Next(scenario.PrimaryDiagnoses.Count)];
            }
            diagnoses.Add(_selectedPrimary);
        }

        // Add secondary diagnoses with probability
        var remainingSlots = maxDiagnoses - diagnoses.Count;
        var shuffledSecondary = scenario.SecondaryDiagnoses.OrderBy(_ => _random.Next()).ToList();

        foreach (var secondaryDx in shuffledSecondary.Take(remainingSlots))
        {
            // 70% chance to include each secondary diagnosis
            if (_random.NextDouble() < 0.7)
            {
                diagnoses.Add(secondaryDx);
            }
        }

        _logger.LogDebug("Generated {Count} diagnoses for scenario {Scenario}",
            diagnoses.Count, scenario.Name);

        return diagnoses;
    }

    /// <summary>
    /// The scenario's primary diagnosis — the DG1 (setId=1) the DG1 contributor renders. Resolved
    /// idempotently: the cached selection is reused, and only drawn (one <see cref="_random"/> step)
    /// on first call when unset, exactly as <see cref="GetLabTestsAsync"/> already does. Unlike
    /// <see cref="GetDiagnoses"/> it does NOT re-roll secondaries, so a caller reading the DG1 to
    /// couple an order to it (S5 B8) doesn't perturb the coordinator RNG on repeated reads. Null only
    /// when the scenario carries no primary diagnosis.
    /// </summary>
    public DiagnosisCode? GetPrimaryDiagnosis()
    {
        var scenario = GetCurrentScenario();
        if (_selectedPrimary == null && scenario.PrimaryDiagnoses.Any())
        {
            _selectedPrimary = scenario.PrimaryDiagnoses[_random.Next(scenario.PrimaryDiagnoses.Count)];
        }
        return _selectedPrimary;
    }

    /// <summary>
    /// Gets appropriate medications for the current scenario.
    /// Returns medications based on prescription probability.
    /// </summary>
    public List<MedicationOption> GetMedications(int maxMedications = 5)
    {
        var scenario = GetCurrentScenario();
        var medications = new List<MedicationOption>();

        foreach (var medication in scenario.TypicalMedications)
        {
            if (medications.Count >= maxMedications)
                break;

            // Use prescription probability to determine if this med is included
            if (_random.NextDouble() < medication.PrescriptionProbability)
            {
                medications.Add(medication);
            }
        }

        _logger.LogDebug("Generated {Count} medications for scenario {Scenario}",
            medications.Count, scenario.Name);

        return medications;
    }

    /// <summary>
    /// Gets appropriate lab tests for the current scenario, valued by the shared realism engine.
    /// When the Clinical Relationship Graph is available, uses its canonical LOINC codes.
    /// Falls back to the repository's hardcoded codes when graph is unavailable.
    /// </summary>
    public async Task<List<LabTestResult>> GetLabTestsAsync(int maxTests = 5, CancellationToken cancellationToken = default)
    {
        var scenario = GetCurrentScenario();

        // Ensure _selectedPrimary is set before graph resolution.
        // The DG1 value contributor normally sets this via GetDiagnoses(), but if the OBX
        // builder runs first, it would be null causing graph lookup to fail.
        if (_selectedPrimary == null && scenario.PrimaryDiagnoses.Any())
        {
            _selectedPrimary = scenario.PrimaryDiagnoses[_random.Next(scenario.PrimaryDiagnoses.Count)];
        }

        // Resolve the ordered analyte set (which LOINCs, and each one's condition-specific sampling
        // band from the scenario): graph-resolved canonical LOINCs first, repository fallback otherwise.
        var orders = new List<Realism.LabOrder>();
        if (_graph != null && scenario.PrimaryDiagnoses.Any())
            orders = await TryGetGraphLabOrdersAsync(scenario, maxTests, cancellationToken).ConfigureAwait(false)
                     ?? new List<Realism.LabOrder>();
        if (orders.Count == 0)
            orders = GetRepositoryLabOrders(scenario, maxTests);

        var results = _labValuer?.Value(orders, _labsKey) ?? new List<LabTestResult>();

        _logger.LogDebug("Generated {Count} lab tests for scenario {Scenario}", results.Count, scenario.Name);
        return results;
    }

    private async Task<List<Realism.LabOrder>?> TryGetGraphLabOrdersAsync(ClinicalScenario scenario, int maxTests, CancellationToken cancellationToken)
    {
        try
        {
            var primaryDx = _selectedPrimary ?? scenario.PrimaryDiagnoses.First();
            var bundleResult = await _graph!.ResolveAsync(primaryDx.Code).ConfigureAwait(false);

            if (bundleResult.IsFailure || bundleResult.Value.ExpectedLabs.Count == 0)
                return null;

            // Filter to real LOINC test codes only — UMLS data includes LOINC Answer codes (LA*) and
            // Part codes (LP*) that aren't orderable tests. Cap to a clinically reasonable number to
            // prevent UMLS flooding (e.g. I10/hypertension gets 31 UMLS relationships, only 2 curated).
            var validLabs = bundleResult.Value.ExpectedLabs
                .Where(lab => LoincTestCodePattern.IsMatch(lab.Code))
                .Take(maxTests)
                .ToList();

            var orders = new List<Realism.LabOrder>();
            foreach (var lab in validLabs)
            {
                // Match graph lab to the repository row for the analyte's units and its condition-specific
                // sampling band — by LOINC first (reliable), then test name, then the range provider.
                var repoTest = scenario.RelevantLabTests
                    .FirstOrDefault(t => string.Equals(t.LoincCode, lab.Code, StringComparison.OrdinalIgnoreCase))
                    ?? scenario.RelevantLabTests
                    .FirstOrDefault(t => string.Equals(t.TestName, lab.Display, StringComparison.OrdinalIgnoreCase));

                var band = repoTest?.ExpectedRange
                    ?? _rangeProvider.GetRange(lab.Code)
                    ?? new ResultRange { MinValue = 0, MaxValue = 100, Units = "" };

                // Advance the selection RNG one draw per ordered lab, matching the pre-S2 per-value
                // draw this path made (the value itself is now the engine's job, keyed on _labsKey).
                // Preserving the draw count keeps the SELECTION of every downstream segment (DG1/RXE/
                // OBR test code) byte-identical, so the v5 byte-move is confined to OBX values.
                _random.NextDouble();

                orders.Add(new Realism.LabOrder(lab.Code, lab.Display, band));
            }
            return orders;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve graph labs for scenario; falling back to repository");
            return null;
        }
    }

    // Draws the same OrderProbability + per-value RNG draws the pre-S2 repository path made, so the
    // SELECTION (and every downstream segment's draw) stays byte-identical; only the OBX value moves
    // (the value is now the engine's, keyed on _labsKey). The per-value NextDouble is consumed and
    // discarded purely to preserve the stream position.
    private List<Realism.LabOrder> GetRepositoryLabOrders(ClinicalScenario scenario, int maxTests)
    {
        var orders = new List<Realism.LabOrder>();
        foreach (var test in scenario.RelevantLabTests)
        {
            if (orders.Count >= maxTests)
                break;
            if (_random.NextDouble() < test.OrderProbability)
            {
                _random.NextDouble();   // preserves the pre-S2 per-value draw position
                orders.Add(new Realism.LabOrder(test.LoincCode, test.TestName, test.ExpectedRange));
            }
        }
        return orders;
    }
}

/// <summary>
/// Lab test result with value and metadata
/// </summary>
public class LabTestResult
{
    public string LoincCode { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Units { get; set; } = string.Empty;
    public string ReferenceRange { get; set; } = string.Empty;
    public string AbnormalFlag { get; set; } = "N";
}
