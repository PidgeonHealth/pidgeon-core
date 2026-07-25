// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Services.Clinical;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Resolves medication field values using clinical scenario coordinator.
/// Provides clinically coherent medications that align with patient's diagnosis.
/// Priority: 77 (higher than MedicationFieldResolver at 75, ensuring clinical coherence)
/// </summary>
public class ClinicalMedicationResolver : IFieldValueResolver
{
    private readonly ILogger<ClinicalMedicationResolver> _logger;
    private readonly ClinicalScenarioCoordinator _scenarioCoordinator;

    public int Priority => 77;

    public ClinicalMedicationResolver(
        ILogger<ClinicalMedicationResolver> logger,
        ClinicalScenarioCoordinator scenarioCoordinator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scenarioCoordinator = scenarioCoordinator ?? throw new ArgumentNullException(nameof(scenarioCoordinator));
    }

    public async Task<string?> ResolveAsync(FieldResolutionContext context)
    {
        await Task.Yield(); // Async for interface compliance

        var fieldName = context.Field.Name?.ToLowerInvariant() ?? "";
        var fieldDescription = context.Field.Description?.ToLowerInvariant() ?? "";

        try
        {
            // Medication name fields (generic name - most important for clinical coherence)
            if ((fieldName.Contains("drug") || fieldName.Contains("medication")) &&
                !fieldName.Contains("brand") && !fieldName.Contains("ndc") &&
                !fieldName.Contains("strength") && !fieldName.Contains("dose") &&
                !fieldName.Contains("form") && !fieldName.Contains("route"))
            {
                // Get medications from current scenario
                var medications = _scenarioCoordinator.GetMedications(maxMedications: 5);

                if (!medications.Any())
                    return null;

                // Select by segment instance so repeated RXE segments cycle through the list and the
                // medication name + dosage of one RXE stay coherent: a pure function of the
                // coordinate, not a process-global counter.
                var medication = medications[((context.GenerationContext?.SetId ?? 1) - 1) % medications.Count];

                _logger.LogDebug("Resolved medication from scenario: {GenericName} ({DrugClass})",
                    medication.GenericName, medication.DrugClass);

                return medication.GenericName;
            }

            // Dosage/strength fields - return typical dosage from scenario
            if ((fieldName.Contains("strength") || fieldName.Contains("dose") ||
                 fieldDescription.Contains("strength") || fieldDescription.Contains("dose")) &&
                !fieldName.Contains("form"))
            {
                var medications = _scenarioCoordinator.GetMedications(maxMedications: 5);

                if (!medications.Any())
                    return null;

                var medication = medications[((context.GenerationContext?.SetId ?? 1) - 1) % medications.Count];

                // Return typical dosage (e.g., "40-80mg daily")
                return medication.TypicalDosage;
            }

            // For other medication fields (NDC, brand name, route, form), return null
            // This allows the standard MedicationFieldResolver (Priority 75) to handle them
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving clinical medication field {FieldName}", fieldName);
            return null;
        }
    }
}
