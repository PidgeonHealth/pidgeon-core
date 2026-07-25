// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>Claim</c>.
/// </summary>
internal sealed class ClaimResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "Claim";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"ClaimResourceBuilder expected Patient, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var claimId = FHIRBuilderSupport.GenerateDeterministicId("claim", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, patient);

            var diagnoses = new[]
            {
                ("E11.9", "Type 2 diabetes mellitus without complications"),
                ("I10", "Essential (primary) hypertension"),
                ("J06.9", "Acute upper respiratory infection, unspecified")
            };
            var diagnosis = diagnoses[random.Next(diagnoses.Length)];

            var procedures = new[]
            {
                ("99213", "Office visit, established patient, moderate", 150.00),
                ("99214", "Office visit, established patient, high", 225.00),
                ("85025", "Complete blood count with differential", 35.00),
                ("80053", "Comprehensive metabolic panel", 45.00)
            };
            var procedure = procedures[random.Next(procedures.Length)];

            var fhirClaim = new Dictionary<string, object?>
            {
                ["resourceType"] = "Claim",
                ["id"] = claimId,
                ["status"] = "active",
                ["type"] = new
                {
                    coding = new object[]
                    {
                        new
                        {
                            system = "http://terminology.hl7.org/CodeSystem/claim-type",
                            code = "professional",
                            display = "Professional"
                        }
                    }
                },
                ["use"] = "claim",
                ["patient"] = new { reference = $"Patient/{patientId}" },
                ["created"] = GenerationDeterminism.CreateClock(context.Options).ToString("yyyy-MM-dd"),
                ["provider"] = new { display = "General Hospital" },
                ["priority"] = new
                {
                    coding = new object[]
                    {
                        new { system = "http://terminology.hl7.org/CodeSystem/processpriority", code = "normal" }
                    }
                },
                ["diagnosis"] = new object[]
                {
                    new
                    {
                        sequence = 1,
                        diagnosisCodeableConcept = new
                        {
                            coding = new object[]
                            {
                                new { system = "http://hl7.org/fhir/sid/icd-10-cm", code = diagnosis.Item1, display = diagnosis.Item2 }
                            }
                        }
                    }
                },
                ["procedure"] = new object[]
                {
                    new
                    {
                        sequence = 1,
                        procedureCodeableConcept = new
                        {
                            coding = new object[]
                            {
                                new { system = "http://www.ama-assn.org/go/cpt", code = procedure.Item1, display = procedure.Item2 }
                            }
                        }
                    }
                },
                ["item"] = new object[]
                {
                    new
                    {
                        sequence = 1,
                        productOrService = new
                        {
                            coding = new object[]
                            {
                                new { system = "http://www.ama-assn.org/go/cpt", code = procedure.Item1, display = procedure.Item2 }
                            }
                        },
                        unitPrice = new { value = procedure.Item3, currency = "USD" },
                        net = new { value = procedure.Item3, currency = "USD" }
                    }
                },
                ["total"] = new { value = procedure.Item3, currency = "USD" }
            };

            context.References.Register("Claim", claimId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirClaim, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR Claim: {ex.Message}"));
        }
    }
}
