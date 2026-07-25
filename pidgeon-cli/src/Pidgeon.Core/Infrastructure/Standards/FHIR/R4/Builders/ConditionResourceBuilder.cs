// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>Condition</c>.
///
/// When a scenario coordinator is present the condition is picked from the
/// shared scenario (e.g., diabetic scenario → E11.9) so a bundled Condition,
/// Observation, and MedicationRequest tell a coherent clinical story.
/// </summary>
internal sealed class ConditionResourceBuilder : IFHIRResourceBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string ResourceType => "Condition";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"ConditionResourceBuilder expected Patient, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var conditionId = FHIRBuilderSupport.GenerateDeterministicId("condition", context.Key);
            var patientId = context.References.GetId("Patient")
                ?? (patient.MedicalRecordNumber != null
                    ? $"patient-{patient.MedicalRecordNumber}"
                    : $"patient-{patient.Id}");

            // When a scenario coordinator is present, pull the primary
            // diagnosis straight from the shared scenario so the emitted ICD-10
            // matches whatever HL7 DG1 or any other Condition in the same bundle
            // used. Falls back to the hardcoded rotation when no coordinator is
            // present (single-resource generation).
            (string Code, string Description, string SnomedCode) condition;
            var primary = context.ScenarioCoordinator?.GetDiagnoses(maxDiagnoses: 1).FirstOrDefault();
            if (primary != null && !string.IsNullOrEmpty(primary.Code))
            {
                var snomed = SnomedFallbackFor(primary.Code);
                condition = (primary.Code, primary.Description, snomed);
            }
            else
            {
                var conditions = new[]
                {
                    ("E11.9", "Type 2 diabetes mellitus without complications", "73211009"),
                    ("I10", "Essential (primary) hypertension", "59621000"),
                    ("E78.5", "Hyperlipidemia, unspecified", "55822004"),
                    ("J45.909", "Unspecified asthma, uncomplicated", "195967001"),
                    ("N18.9", "Chronic kidney disease, unspecified", "709044004"),
                    ("I25.10", "Atherosclerotic heart disease of native coronary artery", "53741008"),
                    ("F32.9", "Major depressive disorder, single episode, unspecified", "35489007"),
                    ("M54.5", "Low back pain", "279039007")
                };
                condition = conditions[random.Next(conditions.Length)];
            }

            var severities = new[] { "mild", "moderate", "severe" };
            var severity = severities[random.Next(severities.Length)];

            var onsetDate = GenerationDeterminism.CreateClock(context.Options).AddDays(-random.Next(30, 365 * 5));

            var fhirCondition = new Dictionary<string, object?>
            {
                ["resourceType"] = "Condition",
                ["id"] = conditionId,
                ["clinicalStatus"] = new
                {
                    coding = new object[]
                    {
                        new
                        {
                            system = "http://terminology.hl7.org/CodeSystem/condition-clinical",
                            code = "active",
                            display = "Active"
                        }
                    }
                },
                ["verificationStatus"] = new
                {
                    coding = new object[]
                    {
                        new
                        {
                            system = "http://terminology.hl7.org/CodeSystem/condition-ver-status",
                            code = "confirmed",
                            display = "Confirmed"
                        }
                    }
                },
                ["category"] = new object[]
                {
                    new
                    {
                        coding = new object[]
                        {
                            new
                            {
                                system = "http://terminology.hl7.org/CodeSystem/condition-category",
                                code = "problem-list-item",
                                display = "Problem List Item"
                            }
                        }
                    }
                },
                ["severity"] = new
                {
                    coding = new object[]
                    {
                        new
                        {
                            system = "http://snomed.info/sct",
                            code = severity == "mild" ? "255604002" : severity == "moderate" ? "6736007" : "24484000",
                            display = severity
                        }
                    }
                },
                ["code"] = new
                {
                    coding = new object[]
                    {
                        new { system = "http://hl7.org/fhir/sid/icd-10-cm", code = condition.Code, display = condition.Description },
                        new { system = "http://snomed.info/sct", code = condition.SnomedCode, display = condition.Description }
                    },
                    text = condition.Description
                },
                ["subject"] = new { reference = $"Patient/{patientId}" },
                ["onsetDateTime"] = onsetDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["recordedDate"] = onsetDate.ToString("yyyy-MM-dd")
            };

            context.References.Register("Condition", conditionId);
            return Task.FromResult(Result<string>.Success(JsonSerializer.Serialize(fhirCondition, JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR Condition: {ex.Message}"));
        }
    }

    // SNOMED CT mapping for the ICD-10 codes the scenario repository emits.
    // TODO (Low): replace this curated table with a terminology-service-driven
    // lookup once the clinical graph exposes one.
    private static string SnomedFallbackFor(string icd10) => icd10 switch
    {
        "E11.9" => "73211009",
        "I10"   => "59621000",
        "E78.5" => "55822004",
        "J45.909" => "195967001",
        "N18.9" => "709044004",
        "I25.10" => "53741008",
        "F32.9" => "35489007",
        "M54.5" => "279039007",
        "I50.9" => "42343007",    // Heart failure
        "J44.9" => "13645005",    // COPD
        "J18.9" => "233604007",   // Pneumonia
        "I21.9" => "57054005",    // Acute myocardial infarction
        "C78.00" => "126713003",  // Secondary malignant neoplasm of lung
        "I63.9" => "230690007",   // Cerebrovascular accident / stroke
        "S72.009A" => "208158009", // Hip fracture
        "O80" => "255410002",     // Normal delivery
        "J06.9" => "54150009",    // Upper respiratory infection
        "K74.60" => "19943007",   // Cirrhosis
        "E03.9" => "40930008",    // Hypothyroidism
        _ => "404684003",          // Clinical finding (fallback)
    };
}
