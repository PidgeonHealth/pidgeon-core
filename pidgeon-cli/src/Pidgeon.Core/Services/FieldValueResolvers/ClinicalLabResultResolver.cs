// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Services.Clinical;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Resolves lab result field values using clinical scenario coordinator.
/// Provides clinically coherent lab tests and results that align with patient's diagnosis.
/// Priority: 91 (higher than HL7SpecificFieldResolver at 90, ensuring OBX.1 Set ID increments properly)
/// </summary>
public class ClinicalLabResultResolver : IFieldValueResolver
{
    private readonly ILogger<ClinicalLabResultResolver> _logger;
    private readonly ClinicalScenarioCoordinator _scenarioCoordinator;

    public int Priority => 91;

    public ClinicalLabResultResolver(
        ILogger<ClinicalLabResultResolver> logger,
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
        var segmentCode = context.SegmentCode?.ToUpperInvariant() ?? "";

        try
        {
            // Only handle observation/result fields (OBX segment primarily)
            if (segmentCode != "OBX" && segmentCode != "OBR")
                return null;

            // GenerationContext is non-null on the composer path but is `null!` by default;
            // callers that reuse resolvers without it (e.g. validation lanes) get no value.
            if (context.GenerationContext is null)
                return null;

            // Per-message OBX state, carried on the generation context so it survives
            // async thread hops and stays coherent with ClinicalCodedElementResolver.
            var obx = context.GenerationContext.ObxLabs;

            // Initialize lab results for this message if not already done
            if (obx.Results == null || obx.Results.Count == 0)
            {
                obx.Results = await _scenarioCoordinator.GetLabTestsAsync(maxTests: 5).ConfigureAwait(false);
            }

            if (obx.Results.Count == 0)
                return null;

            // OBX content (set id, value type, value, units, range, abnormal flag, result status)
            // is contributed by ObxValueContributor and no longer routes
            // through the field pipeline. The order's first lab backs OBR fields below.
            var labResult = obx.Results[0];

            // For OBR.4 - Universal Service Identifier
            if (context.FieldPosition == 4 && segmentCode == "OBR")
            {
                return labResult.LoincCode;
            }

            // A field bound to a POPULATED HL7 table is a constrained enumeration whose value
            // must come from that table, not a lab LOINC. The greedy "result"/"observation"
            // keyword fallback below otherwise stamps the test's LOINC into status/handling
            // enums (OBR.25 Result Status -> table 0123; OBR.49 Result Handling -> 0507),
            // producing codes a real interface rejects. Defer to HL7TableFieldResolver
            // (priority 85), which emits a table-valid code. The explicit OBX.1/2/3/5/6/7/8 and
            // OBR.4 positions handled above return before reaching here, so lab coherence
            // (value type, value, units, range, abnormal flag, LOINC) is untouched.
            if (await HL7TableBinding.GetPopulatedTableAsync(
                    context.Field, context.GenerationContext.TableProvider).ConfigureAwait(false) is not null)
            {
                return null;
            }

            // Field name/description based detection for other observation fields
            if (fieldName.Contains("observation") || fieldName.Contains("loinc") ||
                fieldName.Contains("test") || fieldName.Contains("result") ||
                fieldDescription.Contains("observation") || fieldDescription.Contains("loinc"))
            {
                // LOINC code field
                if (fieldName.Contains("code") || fieldName.Contains("identifier") ||
                    fieldDescription.Contains("code") || fieldDescription.Contains("identifier"))
                    return labResult.LoincCode;

                // Test name field
                if (fieldName.Contains("name") || fieldName.Contains("component") ||
                    fieldName.Contains("description") ||
                    fieldDescription.Contains("name") || fieldDescription.Contains("component"))
                    return labResult.TestName;

                // Value field
                if (fieldName.Contains("value") || fieldDescription.Contains("value"))
                    return labResult.Value;

                // Units field
                if (fieldName.Contains("unit") || fieldDescription.Contains("unit"))
                    return labResult.Units;

                // Reference range field
                if (fieldName.Contains("reference") || fieldName.Contains("range") ||
                    fieldDescription.Contains("reference") || fieldDescription.Contains("range"))
                    return labResult.ReferenceRange;

                // Abnormal flag field
                if (fieldName.Contains("abnormal") || fieldName.Contains("flag") ||
                    fieldDescription.Contains("abnormal") || fieldDescription.Contains("flag"))
                    return labResult.AbnormalFlag;

                // Default to LOINC code
                return labResult.LoincCode;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving clinical lab result field {FieldName}", fieldName);
            return null;
        }
    }
}
