// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>MedicationAdministration</c>.
/// </summary>
internal sealed class MedicationAdministrationResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "MedicationAdministration";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Prescription prescription)
        {
            return Task.FromResult(Result<string>.Failure(
                $"MedicationAdministrationResourceBuilder expected Prescription, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var medAdminId = FHIRBuilderSupport.GenerateDeterministicId("medicationadministration", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, prescription.Patient);

            var fhirMedAdmin = new Dictionary<string, object?>
            {
                ["resourceType"] = "MedicationAdministration",
                ["id"] = medAdminId,
                ["status"] = "completed",
                ["medicationCodeableConcept"] = new
                {
                    coding = new object[]
                    {
                        new
                        {
                            system = "http://www.nlm.nih.gov/research/umls/rxnorm",
                            code = prescription.Medication.RxNormCode ?? prescription.Medication.Id,
                            display = prescription.Medication.DisplayName
                        }
                    },
                    text = prescription.Medication.DisplayName
                },
                ["subject"] = new { reference = $"Patient/{patientId}" },
                ["effectiveDateTime"] = GenerationDeterminism.CreateClock(context.Options).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["performer"] = new object[]
                {
                    new
                    {
                        actor = new { display = "Nurse Administering" }
                    }
                },
                ["dosage"] = new
                {
                    text = prescription.Dosage.Instructions,
                    route = new
                    {
                        coding = new object[]
                        {
                            new
                            {
                                system = "http://snomed.info/sct",
                                code = FHIRBuilderSupport.GetRouteCode(prescription.Dosage.Route),
                                display = prescription.Dosage.Route.ToString()
                            }
                        }
                    },
                    dose = new
                    {
                        value = double.TryParse(prescription.Dosage.Dose, out var dose) ? dose : 1.0,
                        unit = prescription.Dosage.DoseUnit,
                        system = "http://unitsofmeasure.org"
                    }
                }
            };

            context.References.Register("MedicationAdministration", medAdminId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirMedAdmin, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR MedicationAdministration: {ex.Message}"));
        }
    }
}
