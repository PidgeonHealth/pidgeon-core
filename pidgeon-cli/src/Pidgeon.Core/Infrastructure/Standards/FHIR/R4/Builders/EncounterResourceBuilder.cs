// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>Encounter</c>.
///
/// Resolves <c>subject</c> through <see cref="FHIRBuildContext.References"/>
/// and registers the generated Encounter ID so downstream resources
/// (e.g., Observation, Procedure in later sprints) can reference this
/// encounter in the same bundle.
/// </summary>
internal sealed class EncounterResourceBuilder : IFHIRResourceBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string ResourceType => "Encounter";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Encounter encounter)
        {
            return Task.FromResult(Result<string>.Failure(
                $"EncounterResourceBuilder expected Encounter, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var encounterId = FHIRBuilderSupport.GenerateDeterministicId("encounter", context.Key);
            var patientId = context.References.GetId("Patient")
                ?? (encounter.Patient.MedicalRecordNumber != null
                    ? $"patient-{encounter.Patient.MedicalRecordNumber}"
                    : $"patient-{encounter.Patient.Id}");

            var encounterClass = encounter.Type switch
            {
                EncounterType.Inpatient => ("IMP", "inpatient encounter"),
                EncounterType.Emergency => ("EMER", "emergency"),
                EncounterType.Outpatient => ("AMB", "ambulatory"),
                EncounterType.Observation => ("OBSENC", "observation encounter"),
                EncounterType.DaySurgery => ("SS", "short stay"),
                EncounterType.Telemedicine => ("VR", "virtual"),
                _ => ("AMB", "ambulatory")
            };

            var status = encounter.Status switch
            {
                EncounterStatus.Planned => "planned",
                EncounterStatus.Arrived => "arrived",
                EncounterStatus.InProgress => "in-progress",
                EncounterStatus.OnHold => "onleave",
                EncounterStatus.Finished => "finished",
                EncounterStatus.Cancelled => "cancelled",
                _ => "in-progress"
            };

            var fhirEncounter = new Dictionary<string, object?>
            {
                ["resourceType"] = "Encounter",
                ["id"] = encounterId,
                ["status"] = status,
                ["class"] = new
                {
                    system = "http://terminology.hl7.org/CodeSystem/v3-ActCode",
                    code = encounterClass.Item1,
                    display = encounterClass.Item2
                },
                ["subject"] = new { reference = $"Patient/{patientId}" },
                ["participant"] = new object[]
                {
                    new
                    {
                        type = new object[]
                        {
                            new
                            {
                                coding = new object[]
                                {
                                    new
                                    {
                                        system = "http://terminology.hl7.org/CodeSystem/v3-ParticipationType",
                                        code = "ATND",
                                        display = "attender"
                                    }
                                }
                            }
                        },
                        individual = new
                        {
                            reference = $"Practitioner/practitioner-{encounter.Provider.Id}",
                            display = encounter.Provider.Name.DisplayName
                        }
                    }
                }
            };

            if (encounter.StartTime.HasValue)
            {
                var period = new Dictionary<string, string>
                {
                    ["start"] = encounter.StartTime.Value.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
                if (encounter.EndTime.HasValue)
                    period["end"] = encounter.EndTime.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
                fhirEncounter["period"] = period;
            }

            if (encounter.ReasonForVisit != null)
            {
                fhirEncounter["reasonCode"] = new object[]
                {
                    new { text = encounter.ReasonForVisit }
                };
            }

            if (encounter.PrimaryDiagnosis != null)
            {
                fhirEncounter["diagnosis"] = new object[]
                {
                    new
                    {
                        condition = new { display = encounter.PrimaryDiagnosis.Description },
                        use = new
                        {
                            coding = new object[]
                            {
                                new
                                {
                                    system = "http://terminology.hl7.org/CodeSystem/diagnosis-role",
                                    code = "AD",
                                    display = "Admission diagnosis"
                                }
                            }
                        }
                    }
                };
            }

            if (encounter.Location != null)
            {
                fhirEncounter["location"] = new object[]
                {
                    new
                    {
                        location = new { display = encounter.Location },
                        status = "active"
                    }
                };
            }

            context.References.Register("Encounter", encounterId);
            return Task.FromResult(Result<string>.Success(JsonSerializer.Serialize(fhirEncounter, JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR Encounter: {ex.Message}"));
        }
    }
}
