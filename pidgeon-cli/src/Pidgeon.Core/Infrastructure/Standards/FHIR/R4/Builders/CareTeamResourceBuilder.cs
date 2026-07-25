// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>CareTeam</c>.
/// </summary>
internal sealed class CareTeamResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "CareTeam";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"CareTeamResourceBuilder expected Patient, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var careTeamId = FHIRBuilderSupport.GenerateDeterministicId("careteam", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, patient);

            var fhirCareTeam = new Dictionary<string, object?>
            {
                ["resourceType"] = "CareTeam",
                ["id"] = careTeamId,
                ["status"] = "active",
                ["name"] = $"Care Team for {patient.Name.DisplayName}",
                ["subject"] = new { reference = $"Patient/{patientId}" },
                ["participant"] = new object[]
                {
                    new
                    {
                        role = new object[]
                        {
                            new
                            {
                                coding = new object[]
                                {
                                    new { system = "http://snomed.info/sct", code = "223366009", display = "Healthcare professional" }
                                }
                            }
                        },
                        member = new { display = "Dr. Primary Care Physician" }
                    },
                    new
                    {
                        role = new object[]
                        {
                            new
                            {
                                coding = new object[]
                                {
                                    new { system = "http://snomed.info/sct", code = "224535009", display = "Registered nurse" }
                                }
                            }
                        },
                        member = new { display = "Nurse Care Coordinator" }
                    }
                }
            };

            context.References.Register("CareTeam", careTeamId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirCareTeam, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR CareTeam: {ex.Message}"));
        }
    }
}
