// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>PractitionerRole</c>.
/// </summary>
internal sealed class PractitionerRoleResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "PractitionerRole";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Provider provider)
        {
            return Task.FromResult(Result<string>.Failure(
                $"PractitionerRoleResourceBuilder expected Provider, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var practRoleId = FHIRBuilderSupport.GenerateDeterministicId("practitionerrole", context.Key);

            var fhirPractRole = new Dictionary<string, object?>
            {
                ["resourceType"] = "PractitionerRole",
                ["id"] = practRoleId,
                ["active"] = true,
                ["practitioner"] = new
                {
                    reference = $"Practitioner/practitioner-{provider.Id}",
                    display = provider.Name.DisplayName
                },
                ["organization"] = new
                {
                    display = provider.Organization ?? "General Hospital"
                },
                ["code"] = new object[]
                {
                    new
                    {
                        coding = new object[]
                        {
                            new
                            {
                                system = "http://nucc.org/provider-taxonomy",
                                code = FHIRBuilderSupport.GetNUCCCodeForSpecialty(provider.Specialty),
                                display = provider.Specialty ?? "Family Medicine"
                            }
                        }
                    }
                },
                ["specialty"] = new object[]
                {
                    new
                    {
                        coding = new object[]
                        {
                            new
                            {
                                system = "http://nucc.org/provider-taxonomy",
                                code = FHIRBuilderSupport.GetNUCCCodeForSpecialty(provider.Specialty),
                                display = provider.Specialty ?? "Family Medicine"
                            }
                        }
                    }
                },
                ["telecom"] = new object[]
                {
                    new { system = "phone", value = provider.PhoneNumber ?? "+1-555-0123", use = "work" }
                }
            };

            if (provider.Department != null)
            {
                fhirPractRole["location"] = new object[]
                {
                    new { display = provider.Department }
                };
            }

            context.References.Register("PractitionerRole", practRoleId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirPractRole, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR PractitionerRole: {ex.Message}"));
        }
    }
}
