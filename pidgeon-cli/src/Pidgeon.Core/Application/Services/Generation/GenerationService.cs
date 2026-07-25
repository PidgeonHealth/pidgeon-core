// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Application.Services.Generation.Entities;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Generation.Algorithmic.Data;
using Pidgeon.Core.Generation.Types;

namespace Pidgeon.Core.Application.Services.Generation;

/// <summary>
/// Thin dispatcher over per-entity generators. Each call seeds a deterministic
/// <see cref="Random"/> from <see cref="GenerationOptions.Seed"/> and forwards
/// to the appropriate generator, then wraps the outcome in <see cref="Result{T}"/>
/// with uniform error reporting.
///
/// <para>
/// The per-entity logic lives in <c>Entities/</c>. This dispatcher stays
/// free of sync-over-async: entity generators expose synchronous
/// <c>Generate</c> methods that drive their async dependencies through a
/// single completion-check fast-path inside the generator file, so
/// state-machine allocations do not dominate Debug-config batch generation.
/// </para>
/// </summary>
internal sealed class GenerationService : IGenerationService
{
    private readonly ILogger<GenerationService> _logger;
    private readonly PatientGenerator _patientGenerator;
    private readonly ProviderGenerator _providerGenerator;
    private readonly MedicationGenerator _medicationGenerator;
    private readonly PrescriptionGenerator _prescriptionGenerator;
    private readonly EncounterGenerator _encounterGenerator;
    private readonly ObservationResultGenerator _observationGenerator;

    public GenerationService(
        ILogger<GenerationService> logger,
        PatientGenerator patientGenerator,
        ProviderGenerator providerGenerator,
        MedicationGenerator medicationGenerator,
        PrescriptionGenerator prescriptionGenerator,
        EncounterGenerator encounterGenerator,
        ObservationResultGenerator observationGenerator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _patientGenerator = patientGenerator ?? throw new ArgumentNullException(nameof(patientGenerator));
        _providerGenerator = providerGenerator ?? throw new ArgumentNullException(nameof(providerGenerator));
        _medicationGenerator = medicationGenerator ?? throw new ArgumentNullException(nameof(medicationGenerator));
        _prescriptionGenerator = prescriptionGenerator ?? throw new ArgumentNullException(nameof(prescriptionGenerator));
        _encounterGenerator = encounterGenerator ?? throw new ArgumentNullException(nameof(encounterGenerator));
        _observationGenerator = observationGenerator ?? throw new ArgumentNullException(nameof(observationGenerator));
    }

    /// <summary>
    /// Backward-compatibility constructor for callers that still wire
    /// <see cref="GenerationService"/> from raw dependencies. Builds the
    /// per-entity generators in-process using <see cref="NullLogger{T}"/>.
    /// The <paramref name="fieldPathResolver"/> parameter is retained for signature
    /// compatibility and is intentionally unused by the dispatcher itself.
    /// </summary>
    internal GenerationService(
        ILogger<GenerationService> logger,
        IDemographicsDataService demographicsService,
        IConstraintResolver constraintResolver,
        ILockSessionService lockSessionService,
        IFieldPathResolver fieldPathResolver)
        : this(logger, BuildEntityGenerators(demographicsService, constraintResolver, lockSessionService))
    {
        _ = fieldPathResolver;
    }

    private GenerationService(
        ILogger<GenerationService> logger,
        (PatientGenerator P, ProviderGenerator Pr, MedicationGenerator M, PrescriptionGenerator Rx, EncounterGenerator E, ObservationResultGenerator O) built)
        : this(logger, built.P, built.Pr, built.M, built.Rx, built.E, built.O)
    {
    }

    private static (PatientGenerator, ProviderGenerator, MedicationGenerator, PrescriptionGenerator, EncounterGenerator, ObservationResultGenerator)
        BuildEntityGenerators(
            IDemographicsDataService demographics,
            IConstraintResolver constraints,
            ILockSessionService sessions)
    {
        var applier = new LockedValueApplier(sessions, NullLogger<LockedValueApplier>.Instance);
        var patient = new PatientGenerator(demographics, constraints, applier, NullLogger<PatientGenerator>.Instance);
        var provider = new ProviderGenerator(demographics, applier, NullLogger<ProviderGenerator>.Instance);
        var medication = new MedicationGenerator(NullLogger<MedicationGenerator>.Instance);
        var prescription = new PrescriptionGenerator(patient, provider, medication, NullLogger<PrescriptionGenerator>.Instance);
        var encounter = new EncounterGenerator(patient, provider, applier, NullLogger<EncounterGenerator>.Instance);
        var observation = new ObservationResultGenerator();
        return (patient, provider, medication, prescription, encounter, observation);
    }

    // The patient-bearing generators consult the active scenario's demographic constraints (set on
    // the options by the HL7 plugin) so the sampled patient matches the story. A null constraint
    // (the common case) leaves demographics a free draw.
    public Result<Patient> GeneratePatient(GenerationOptions options) =>
        Dispatch(options, "patient", ctx => _patientGenerator.Generate(ctx, options.ScenarioConstraints));

    public Result<Medication> GenerateMedication(GenerationOptions options) =>
        Dispatch(options, "medication", ctx => _medicationGenerator.Generate(ctx));

    public Result<Prescription> GeneratePrescription(GenerationOptions options) =>
        Dispatch(options, "prescription", ctx => _prescriptionGenerator.Generate(ctx, options.ScenarioConstraints));

    public Result<Encounter> GenerateEncounter(GenerationOptions options) =>
        Dispatch(options, "encounter", ctx => _encounterGenerator.Generate(ctx, options.ScenarioConstraints));

    public Result<Provider> GenerateProvider(GenerationOptions options) =>
        Dispatch(options, "provider", ctx => _providerGenerator.Generate(ctx));

    public Result<ObservationResult> GenerateObservationResult(GenerationOptions options) =>
        Dispatch(options, "observation", ctx => _observationGenerator.Generate(ctx));

    public GenerationServiceInfo GetServiceInfo()
    {
        return new GenerationServiceInfo
        {
            ServiceTier = "Core (Free)",
            AIAvailable = false,
            AvailablePatientTypes = new[] { PatientType.General },
            AvailableVendorProfiles = new[] { VendorProfile.Generic },
            Dataset = new DatasetInfo
            {
                MedicationCount = HealthcareMedications.Medications.Length,
                FirstNameCount = 0,
                SurnameCount = 0,
                Freshness = "Data-Driven",
                CoveragePercentage = 95,
                SpecialtyCategories = new[] { "Common Medications", "Demographic Data", "Geographic Data", "Healthcare Specialties" }
            },
            Limits = new UsageLimits
            {
                MaxGenerationsPerPeriod = 10,
                LimitPeriod = "Session",
                BatchProcessingAvailable = false,
                CloudAPIAvailable = false,
                MaxTeamSize = 1
            }
        };
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private Result<T> Dispatch<T>(GenerationOptions options, string entityType, Func<EntityGenerationContext, T> generate)
    {
        try
        {
            // Coordinate-addressed entropy: each entity type draws from its own stable
            // sub-coordinate of the run root, so output depends on the coordinate, not on draw
            // order. The root is seed-reproducible when a seed is set and a captured random root
            // otherwise, so even an unseeded run is internally coordinate-addressed.
            var rootKey = GenerationDeterminism.CreateKey(options);
            var clock = GenerationDeterminism.CreateClock(options);
            var ctx = new EntityGenerationContext(rootKey.Derive(entityType), clock, options);
            var value = generate(ctx);
            return Result<T>.Success(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate {EntityType}", entityType);
            return Result<T>.Failure($"{char.ToUpper(entityType[0])}{entityType[1..]} generation failed: {ex.Message}");
        }
    }
}
