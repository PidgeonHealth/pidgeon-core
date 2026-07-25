// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>CarePlan</c>.
/// </summary>
internal sealed class CarePlanResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "CarePlan";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"CarePlanResourceBuilder expected Patient, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var carePlanId = FHIRBuilderSupport.GenerateDeterministicId("careplan", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, patient);

            var carePlans = new[]
            {
                ("Diabetes Management Plan", "698360004"),
                ("Cardiac Rehabilitation Program", "390893007"),
                ("Post-Surgical Recovery Plan", "736372004"),
                ("Chronic Pain Management Plan", "735321000")
            };
            var carePlan = carePlans[random.Next(carePlans.Length)];

            var fhirCarePlan = new Dictionary<string, object?>
            {
                ["resourceType"] = "CarePlan",
                ["id"] = carePlanId,
                ["status"] = "active",
                ["intent"] = "plan",
                ["title"] = carePlan.Item1,
                ["subject"] = new { reference = $"Patient/{patientId}" },
                ["period"] = new
                {
                    start = GenerationDeterminism.CreateClock(context.Options).ToString("yyyy-MM-dd"),
                    end = GenerationDeterminism.CreateClock(context.Options).AddMonths(6).ToString("yyyy-MM-dd")
                },
                ["category"] = new object[]
                {
                    new
                    {
                        coding = new object[]
                        {
                            new { system = "http://snomed.info/sct", code = carePlan.Item2, display = carePlan.Item1 }
                        }
                    }
                },
                ["activity"] = new object[]
                {
                    new
                    {
                        detail = new
                        {
                            status = "not-started",
                            description = "Follow-up appointment in 2 weeks"
                        }
                    },
                    new
                    {
                        detail = new
                        {
                            status = "not-started",
                            description = "Lab work before next visit"
                        }
                    }
                }
            };

            context.References.Register("CarePlan", carePlanId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirCarePlan, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR CarePlan: {ex.Message}"));
        }
    }
}
