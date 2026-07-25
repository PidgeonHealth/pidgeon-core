// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Services.Clinical;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Resolves diagnosis field values using clinical scenario coordinator.
/// Provides clinically coherent ICD-10 codes that align with selected patient scenario.
/// Priority: 77 (higher than DiagnosisFieldResolver at 75, ensuring clinical coherence)
/// </summary>
public class ClinicalDiagnosisResolver : IFieldValueResolver
{
    private readonly ILogger<ClinicalDiagnosisResolver> _logger;
    private readonly ClinicalScenarioCoordinator _scenarioCoordinator;

    public int Priority => 77;

    public ClinicalDiagnosisResolver(
        ILogger<ClinicalDiagnosisResolver> logger,
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
            // Diagnosis code fields (DG1 segment)
            if (fieldName.Contains("diagnosis") || fieldName.Contains("icd") ||
                fieldDescription.Contains("diagnosis") || fieldDescription.Contains("icd"))
            {
                // Get diagnoses from current scenario
                var diagnoses = _scenarioCoordinator.GetDiagnoses(maxDiagnoses: 3);

                if (!diagnoses.Any())
                    return null;

                // Select by segment instance so repeated DG1 segments cycle through the panel and
                // every field of one DG1 resolves the SAME diagnosis: a pure function of the
                // coordinate, not a process-global counter.
                var diagnosis = diagnoses[((context.GenerationContext?.SetId ?? 1) - 1) % diagnoses.Count];

                _logger.LogDebug("Resolved diagnosis from scenario: {Code} - {Description}",
                    diagnosis.Code, diagnosis.Description);

                // ICD-10 code field
                if (fieldName.Contains("code") || fieldDescription.Contains("code"))
                    return diagnosis.Code;

                // Diagnosis description field
                if (fieldName.Contains("description") || fieldName.Contains("text") ||
                    fieldDescription.Contains("description") || fieldDescription.Contains("text"))
                    return diagnosis.Description;

                // Default to code for diagnosis fields
                return diagnosis.Code;
            }

            // Not a diagnosis field
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving clinical diagnosis field {FieldName}", fieldName);
            return null;
        }
    }
}
