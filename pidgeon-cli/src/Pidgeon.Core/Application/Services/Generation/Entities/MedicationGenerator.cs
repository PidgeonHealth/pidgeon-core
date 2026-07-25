// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Generation.Algorithmic.Data;

namespace Pidgeon.Core.Application.Services.Generation.Entities;

/// <summary>
/// Per-entity generator for <see cref="Medication"/>. Fully synchronous — no
/// async dependencies required.
/// </summary>
internal sealed class MedicationGenerator
{
    private readonly ILogger<MedicationGenerator> _logger;

    public MedicationGenerator(ILogger<MedicationGenerator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Medication Generate(EntityGenerationContext ctx)
    {
        var rng = ctx.Key.Stream();

        var availableMeds = HealthcareMedications.Medications
            .Where(m => m.AppropriateAgeGroups.HasFlag(AgeGroup.Adult))
            .ToList();

        if (availableMeds.Count == 0)
        {
            throw new InvalidOperationException("No medications available for specified patient type");
        }

        var medData = availableMeds[rng.Next(availableMeds.Count)];

        var medication = new Medication
        {
            Id = $"MED{rng.Next(100000000, 999999999)}",
            Name = medData.BrandName ?? medData.GenericName,
            GenericName = medData.GenericName,
            Strength = medData.AvailableStrengths.FirstOrDefault() ?? "Unknown",
            DrugClass = medData.TherapeuticClass,
            NdcCode = medData.Ndc,
            ControlledSchedule = medData.IsControlledSubstance ? ControlledSubstanceSchedule.ScheduleII : null
        };

        _logger.LogDebug("Generated medication: {Name} ({Generic})",
            medication.Name, medication.GenericName);

        return medication;
    }
}
