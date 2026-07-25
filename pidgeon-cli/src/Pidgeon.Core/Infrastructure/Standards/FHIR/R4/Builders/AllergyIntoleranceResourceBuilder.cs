// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>AllergyIntolerance</c>.
/// </summary>
internal sealed class AllergyIntoleranceResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "AllergyIntolerance";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"AllergyIntoleranceResourceBuilder expected Patient, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var allergyId = FHIRBuilderSupport.GenerateDeterministicId("allergyintolerance", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, patient);

            var allergies = new[]
            {
                ("7980", "Penicillin", "764146007", "Rash", "271807003"),
                ("2670", "Codeine", "387494007", "Nausea", "422587007"),
                ("1191", "Aspirin", "387458008", "Hives", "126485001"),
                ("4053", "Sulfa drugs", "763875007", "Anaphylaxis", "39579001"),
                ("8163", "Latex", "111088007", "Contact dermatitis", "40275004")
            };
            var allergy = allergies[random.Next(allergies.Length)];

            var criticalities = new[] { "low", "high", "unable-to-assess" };
            var criticality = criticalities[random.Next(criticalities.Length)];

            var fhirAllergy = new Dictionary<string, object?>
            {
                ["resourceType"] = "AllergyIntolerance",
                ["id"] = allergyId,
                ["clinicalStatus"] = new
                {
                    coding = new object[]
                    {
                        new
                        {
                            system = "http://terminology.hl7.org/CodeSystem/allergyintolerance-clinical",
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
                            system = "http://terminology.hl7.org/CodeSystem/allergyintolerance-verification",
                            code = "confirmed",
                            display = "Confirmed"
                        }
                    }
                },
                ["type"] = "allergy",
                ["category"] = new[] { "medication" },
                ["criticality"] = criticality,
                ["code"] = new
                {
                    coding = new object[]
                    {
                        new
                        {
                            system = "http://www.nlm.nih.gov/research/umls/rxnorm",
                            code = allergy.Item1,
                            display = allergy.Item2
                        }
                    },
                    text = allergy.Item2
                },
                ["patient"] = new { reference = $"Patient/{patientId}" },
                ["recordedDate"] = GenerationDeterminism.CreateClock(context.Options).AddDays(-random.Next(30, 365 * 3)).ToString("yyyy-MM-dd"),
                ["reaction"] = new object[]
                {
                    new
                    {
                        substance = new
                        {
                            coding = new object[]
                            {
                                new
                                {
                                    system = "http://snomed.info/sct",
                                    code = allergy.Item3,
                                    display = allergy.Item2
                                }
                            }
                        },
                        manifestation = new object[]
                        {
                            new
                            {
                                coding = new object[]
                                {
                                    new
                                    {
                                        system = "http://snomed.info/sct",
                                        code = allergy.Item5,
                                        display = allergy.Item4
                                    }
                                }
                            }
                        },
                        severity = criticality == "high" ? "severe" : "moderate"
                    }
                }
            };

            context.References.Register("AllergyIntolerance", allergyId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirAllergy, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR AllergyIntolerance: {ex.Message}"));
        }
    }
}
