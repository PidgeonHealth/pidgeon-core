// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>Medication</c>.
/// </summary>
internal sealed class MedicationResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "Medication";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Medication medication)
        {
            return Task.FromResult(Result<string>.Failure(
                $"MedicationResourceBuilder expected Medication, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var medicationId = FHIRBuilderSupport.GenerateDeterministicId("medication", context.Key);

            var codeCodings = new List<object>
            {
                new
                {
                    system = "http://www.nlm.nih.gov/research/umls/rxnorm",
                    code = medication.RxNormCode ?? medication.Id,
                    display = medication.DisplayName
                }
            };

            if (medication.NdcCode != null)
            {
                codeCodings.Add(new
                {
                    system = "http://hl7.org/fhir/sid/ndc",
                    code = medication.NdcCode,
                    display = medication.DisplayName
                });
            }

            var form = medication.DosageForm?.ToString().ToLowerInvariant() ?? "tablet";
            var fhirMedication = new Dictionary<string, object?>
            {
                ["resourceType"] = "Medication",
                ["id"] = medicationId,
                ["code"] = new
                {
                    coding = codeCodings.ToArray(),
                    text = medication.DisplayName
                },
                ["status"] = "active",
                ["form"] = new
                {
                    coding = new object[]
                    {
                        new
                        {
                            system = "http://snomed.info/sct",
                            code = FHIRBuilderSupport.GetSnomedFormCode(form),
                            display = form
                        }
                    },
                    text = form
                }
            };

            if (medication.Manufacturer != null)
            {
                fhirMedication["manufacturer"] = new
                {
                    display = medication.Manufacturer
                };
            }

            context.References.Register("Medication", medicationId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirMedication, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR Medication: {ex.Message}"));
        }
    }
}
