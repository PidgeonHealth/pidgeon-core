// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>MedicationRequest</c>.
///
/// Resolves <c>subject</c> through <see cref="FHIRBuildContext.References"/>
/// so a MedicationRequest inside a bundle targets the same Patient as the
/// bundle's Observation and Condition.
///
/// When a scenario coordinator is present, <c>medicationCodeableConcept</c>
/// is driven from the scenario's therapeutic coupling (e.g., hypertension
/// → antihypertensive).
/// </summary>
internal sealed class MedicationRequestResourceBuilder : IFHIRResourceBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string ResourceType => "MedicationRequest";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Prescription prescription)
        {
            return Task.FromResult(Result<string>.Failure(
                $"MedicationRequestResourceBuilder expected Prescription, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var medReqId = FHIRBuilderSupport.GenerateDeterministicId("medicationrequest", context.Key);
            var patientId = context.References.GetId("Patient")
                ?? (prescription.Patient.MedicalRecordNumber != null
                    ? $"patient-{prescription.Patient.MedicalRecordNumber}"
                    : $"patient-{prescription.Patient.Id}");

            // When a scenario coordinator is present, prefer the first
            // medication the scenario suggests. Scenario data carries generic
            // name + drug class (no RxNorm code), so the RxNorm slot stays
            // populated from the domain entity. The display/text surfaces the
            // scenario-coherent medication name.
            //
            // ClinicalScenarioCoordinator.GetMedications uses probability-
            // weighted selection over TypicalMedications and can return an
            // empty list when rolls don't hit. When a coordinator IS present,
            // fall back to the scenario's first typical medication rather than
            // to the prescription's (unrelated) random medication so callers
            // can rely on "bundle matches scenario" regardless of RNG state.
            // Without this, the MedicationRequest display could be Ultram on
            // a hypertension scenario when rolls went cold — exactly the
            // Windows-only flake surfaced by the Loft team.
            string? scenarioMedicationName = null;
            if (context.ScenarioCoordinator != null)
            {
                var picked = context.ScenarioCoordinator.GetMedications(maxMedications: 1).FirstOrDefault();
                scenarioMedicationName = !string.IsNullOrEmpty(picked?.GenericName)
                    ? picked!.GenericName
                    : context.ScenarioCoordinator.GetCurrentScenario()
                        .TypicalMedications.FirstOrDefault()?.GenericName;
            }
            var displayName = !string.IsNullOrEmpty(scenarioMedicationName)
                ? scenarioMedicationName!
                : prescription.Medication.DisplayName;

            // A coding is emitted only when the domain medication carries a real RxNorm code —
            // labeling the internal id under the RxNorm system asserts a code that isn't one
            // (audit D-24). Without one the concept is text-only, which FHIR R4 permits.
            object medicationConcept = !string.IsNullOrEmpty(prescription.Medication.RxNormCode)
                ? new
                {
                    coding = new object[]
                    {
                        new
                        {
                            system = "http://www.nlm.nih.gov/research/umls/rxnorm",
                            code = prescription.Medication.RxNormCode,
                            display = displayName
                        }
                    },
                    text = displayName
                }
                : new { text = displayName };

            var fhirMedReq = new Dictionary<string, object?>
            {
                ["resourceType"] = "MedicationRequest",
                ["id"] = medReqId,
                ["status"] = "active",
                ["intent"] = "order",
                ["medicationCodeableConcept"] = medicationConcept,
                ["subject"] = new { reference = $"Patient/{patientId}" },
                ["authoredOn"] = prescription.DatePrescribed.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["requester"] = new
                {
                    reference = $"Practitioner/practitioner-{prescription.Prescriber.Id}",
                    display = prescription.Prescriber.Name.DisplayName
                },
                ["dosageInstruction"] = new object[]
                {
                    new
                    {
                        text = prescription.Dosage.Instructions,
                        timing = new
                        {
                            code = new
                            {
                                coding = new object[]
                                {
                                    new
                                    {
                                        system = "http://terminology.hl7.org/CodeSystem/v3-GTSAbbreviation",
                                        code = MapFrequencyToTimingCode(prescription.Dosage.Frequency),
                                        display = prescription.Dosage.Frequency
                                    }
                                }
                            }
                        },
                        route = new
                        {
                            coding = new object[]
                            {
                                new
                                {
                                    system = "http://snomed.info/sct",
                                    code = GetRouteCode(prescription.Dosage.Route),
                                    display = prescription.Dosage.Route.ToString()
                                }
                            }
                        },
                        doseAndRate = new object[]
                        {
                            new
                            {
                                doseQuantity = new
                                {
                                    value = double.TryParse(prescription.Dosage.Dose, out var dose) ? dose : 1.0,
                                    unit = prescription.Dosage.DoseUnit,
                                    system = "http://unitsofmeasure.org"
                                }
                            }
                        }
                    }
                },
                ["substitution"] = new { allowedBoolean = prescription.AllowGenericSubstitution }
            };

            if (prescription.Dosage.Quantity.HasValue)
            {
                fhirMedReq["dispenseRequest"] = new
                {
                    quantity = new
                    {
                        value = prescription.Dosage.Quantity.Value,
                        unit = prescription.Dosage.DoseUnit
                    },
                    numberOfRepeatsAllowed = prescription.Dosage.Refills ?? 0,
                    expectedSupplyDuration = prescription.Dosage.DaysSupply.HasValue
                        ? new { value = prescription.Dosage.DaysSupply.Value, unit = "d", system = "http://unitsofmeasure.org", code = "d" }
                        : null
                };
            }

            context.References.Register("MedicationRequest", medReqId);
            return Task.FromResult(Result<string>.Success(JsonSerializer.Serialize(fhirMedReq, JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR MedicationRequest: {ex.Message}"));
        }
    }

    private static string MapFrequencyToTimingCode(string frequency) =>
        frequency.ToUpperInvariant() switch
        {
            "QD" or "DAILY" => "QD",
            "BID" => "BID",
            "TID" => "TID",
            "QID" => "QID",
            "Q4H" => "Q4H",
            "Q6H" => "Q6H",
            "Q8H" => "Q8H",
            "Q12H" => "Q12H",
            "QHS" => "QHS",
            "PRN" => "PRN",
            _ => frequency
        };

    private static string GetRouteCode(RouteOfAdministration route) =>
        route switch
        {
            RouteOfAdministration.Oral => "26643006",
            RouteOfAdministration.Intravenous => "47625008",
            RouteOfAdministration.Intramuscular => "78421000",
            RouteOfAdministration.Subcutaneous => "34206005",
            RouteOfAdministration.Topical => "6064005",
            RouteOfAdministration.Inhalation => "18679011000001101",
            RouteOfAdministration.Rectal => "37161004",
            RouteOfAdministration.Sublingual => "37839007",
            RouteOfAdministration.Nasal => "46713006",
            RouteOfAdministration.Ophthalmic => "54485002",
            _ => "26643006"
        };
}
