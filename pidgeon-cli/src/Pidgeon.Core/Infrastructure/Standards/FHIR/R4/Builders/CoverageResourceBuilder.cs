// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>Coverage</c>.
/// </summary>
internal sealed class CoverageResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "Coverage";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"CoverageResourceBuilder expected Patient, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var coverageId = FHIRBuilderSupport.GenerateDeterministicId("coverage", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, patient);

            var insurers = new[] {
                ("Blue Cross Blue Shield", "BCBS"),
                ("Aetna", "AETNA"),
                ("Cigna", "CIGNA"),
                ("UnitedHealthcare", "UHC"),
                ("Medicare", "MCR")
            };
            var insurer = insurers[random.Next(insurers.Length)];

            var fhirCoverage = new Dictionary<string, object?>
            {
                ["resourceType"] = "Coverage",
                ["id"] = coverageId,
                ["status"] = "active",
                ["type"] = new
                {
                    coding = new object[]
                    {
                        new
                        {
                            system = "http://terminology.hl7.org/CodeSystem/v3-ActCode",
                            code = "HIP",
                            display = "health insurance plan policy"
                        }
                    }
                },
                ["subscriber"] = new
                {
                    reference = $"Patient/{patientId}",
                    display = patient.Name.DisplayName
                },
                ["subscriberId"] = $"{insurer.Item2}-{random.Next(100000000, 999999999)}",
                ["beneficiary"] = new { reference = $"Patient/{patientId}" },
                ["relationship"] = new
                {
                    coding = new object[]
                    {
                        new
                        {
                            system = "http://terminology.hl7.org/CodeSystem/subscriber-relationship",
                            code = "self",
                            display = "Self"
                        }
                    }
                },
                ["period"] = new
                {
                    start = GenerationDeterminism.CreateClock(context.Options).AddYears(-1).ToString("yyyy-MM-dd"),
                    end = GenerationDeterminism.CreateClock(context.Options).AddYears(1).ToString("yyyy-MM-dd")
                },
                ["payor"] = new object[]
                {
                    new
                    {
                        display = insurer.Item1
                    }
                },
                ["class"] = new object[]
                {
                    new
                    {
                        type = new
                        {
                            coding = new object[]
                            {
                                new
                                {
                                    system = "http://terminology.hl7.org/CodeSystem/coverage-class",
                                    code = "group"
                                }
                            }
                        },
                        value = $"GRP-{random.Next(10000, 99999)}",
                        name = $"{insurer.Item1} Group Plan"
                    },
                    new
                    {
                        type = new
                        {
                            coding = new object[]
                            {
                                new
                                {
                                    system = "http://terminology.hl7.org/CodeSystem/coverage-class",
                                    code = "plan"
                                }
                            }
                        },
                        value = $"PLN-{random.Next(1000, 9999)}",
                        name = "PPO"
                    }
                }
            };

            context.References.Register("Coverage", coverageId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirCoverage, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR Coverage: {ex.Message}"));
        }
    }
}
