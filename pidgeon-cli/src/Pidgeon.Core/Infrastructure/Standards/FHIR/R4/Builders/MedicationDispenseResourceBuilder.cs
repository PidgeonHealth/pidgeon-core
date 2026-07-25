// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>MedicationDispense</c>.
/// </summary>
internal sealed class MedicationDispenseResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "MedicationDispense";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Prescription prescription)
        {
            return Task.FromResult(Result<string>.Failure(
                $"MedicationDispenseResourceBuilder expected Prescription, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var dispenseId = FHIRBuilderSupport.GenerateDeterministicId("medicationdispense", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, prescription.Patient);

            var fhirDispense = new Dictionary<string, object?>
            {
                ["resourceType"] = "MedicationDispense",
                ["id"] = dispenseId,
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
                ["performer"] = new object[]
                {
                    new
                    {
                        actor = new { display = "Pharmacy Department" }
                    }
                },
                ["quantity"] = new
                {
                    value = prescription.Dosage.Quantity ?? 30,
                    unit = prescription.Dosage.DoseUnit,
                    system = "http://unitsofmeasure.org"
                },
                ["daysSupply"] = new
                {
                    value = prescription.Dosage.DaysSupply ?? 30,
                    unit = "Day",
                    system = "http://unitsofmeasure.org",
                    code = "d"
                },
                ["whenHandedOver"] = GenerationDeterminism.CreateClock(context.Options).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["dosageInstruction"] = new object[]
                {
                    new { text = prescription.Dosage.Instructions }
                }
            };

            context.References.Register("MedicationDispense", dispenseId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirDispense, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR MedicationDispense: {ex.Message}"));
        }
    }
}
