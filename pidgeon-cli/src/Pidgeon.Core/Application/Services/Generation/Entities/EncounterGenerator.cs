// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Domain.Clinical;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.Generation.Entities;

/// <summary>
/// Per-entity generator for <see cref="Encounter"/>. Composes a patient and a
/// provider from their respective generators under a shared seeded random.
/// </summary>
internal sealed class EncounterGenerator
{
    private static readonly string[] FacilityNames =
    {
        "Primary Care Clinic",
        "Family Health Center",
        "Medical Associates",
        "City General Hospital",
        "Regional Medical Center",
        "Community Health Center"
    };

    private static readonly EncounterType[] EncounterTypes =
    {
        EncounterType.Outpatient,
        EncounterType.Inpatient,
        EncounterType.Emergency,
        EncounterType.Observation
    };

    private readonly PatientGenerator _patientGenerator;
    private readonly ProviderGenerator _providerGenerator;
    private readonly LockedValueApplier _lockedValues;
    private readonly ILogger<EncounterGenerator> _logger;

    public EncounterGenerator(
        PatientGenerator patientGenerator,
        ProviderGenerator providerGenerator,
        LockedValueApplier lockedValues,
        ILogger<EncounterGenerator> logger)
    {
        _patientGenerator = patientGenerator ?? throw new ArgumentNullException(nameof(patientGenerator));
        _providerGenerator = providerGenerator ?? throw new ArgumentNullException(nameof(providerGenerator));
        _lockedValues = lockedValues ?? throw new ArgumentNullException(nameof(lockedValues));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Encounter Generate(EntityGenerationContext ctx, ScenarioConstraints? constraints = null)
    {
        var rng = ctx.Key.Stream();
        var patient = _patientGenerator.Generate(ctx.Derive("patient"), constraints);
        var provider = _providerGenerator.Generate(ctx.Derive("provider"));

        var encounter = new Encounter
        {
            Id = $"E{ctx.Clock:yyyyMMdd}{rng.Next(1000, 9999)}",
            Patient = patient,
            Provider = provider,
            Type = EncounterTypes[rng.Next(EncounterTypes.Length)],
            Status = EncounterStatus.Finished,
            StartTime = ctx.Clock.Date.AddDays(-rng.Next(0, 30)),
            Location = FacilityNames[rng.Next(FacilityNames.Length)],
            Priority = EncounterPriority.Routine
        };

        var applyTask = _lockedValues.ApplyAsync(encounter, ctx.Options, "Encounter");
        encounter = applyTask.IsCompletedSuccessfully
            ? applyTask.Result
            : applyTask.ConfigureAwait(false).GetAwaiter().GetResult();

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Generated encounter using demographic data: {EncounterId} for {Patient}",
                encounter.Id, encounter.Patient.Name.DisplayName);
        }

        return encounter;
    }

    public Task<Encounter> GenerateAsync(EntityGenerationContext ctx, ScenarioConstraints? constraints = null) =>
        Task.FromResult(Generate(ctx, constraints));
}
