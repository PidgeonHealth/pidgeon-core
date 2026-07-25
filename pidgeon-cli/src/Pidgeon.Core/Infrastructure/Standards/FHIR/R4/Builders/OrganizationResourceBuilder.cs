// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>Organization</c>. Input is a
/// plain organization name string passed through <see cref="FHIRBuildContext.DomainEntity"/>.
/// </summary>
internal sealed class OrganizationResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "Organization";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not string organizationName)
        {
            return Task.FromResult(Result<string>.Failure(
                $"OrganizationResourceBuilder expected string, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var orgId = FHIRBuilderSupport.GenerateDeterministicId("organization", context.Key);

            var orgTypes = new[] {
                ("prov", "Healthcare Provider"),
                ("dept", "Hospital Department"),
                ("ins", "Insurance Company"),
                ("other", "Other")
            };
            var orgType = orgTypes[random.Next(orgTypes.Length)];

            var fhirOrg = new Dictionary<string, object?>
            {
                ["resourceType"] = "Organization",
                ["id"] = orgId,
                ["active"] = true,
                ["type"] = new object[]
                {
                    new
                    {
                        coding = new object[]
                        {
                            new
                            {
                                system = "http://terminology.hl7.org/CodeSystem/organization-type",
                                code = orgType.Item1,
                                display = orgType.Item2
                            }
                        }
                    }
                },
                ["name"] = organizationName,
                ["identifier"] = new object[]
                {
                    new
                    {
                        use = "official",
                        type = new
                        {
                            coding = new object[]
                            {
                                new
                                {
                                    system = "http://terminology.hl7.org/CodeSystem/v2-0203",
                                    code = "NPI",
                                    display = "National provider identifier"
                                }
                            }
                        },
                        system = "http://hl7.org/fhir/sid/us-npi",
                        value = $"{random.Next(1000000000, 2000000000)}"
                    }
                },
                ["telecom"] = new object[]
                {
                    new { system = "phone", value = $"+1-555-{random.Next(100, 999)}-{random.Next(1000, 9999)}", use = "work" }
                },
                ["address"] = new object[]
                {
                    new
                    {
                        use = "work",
                        type = "physical",
                        line = new[] { $"{random.Next(100, 999)} Healthcare Blvd" },
                        city = "Medical City",
                        state = "MC",
                        postalCode = $"{random.Next(10000, 99999)}",
                        country = "US"
                    }
                }
            };

            context.References.Register("Organization", orgId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirOrg, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR Organization: {ex.Message}"));
        }
    }
}
