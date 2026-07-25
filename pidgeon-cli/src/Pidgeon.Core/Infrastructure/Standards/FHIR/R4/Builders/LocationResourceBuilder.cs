// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>Location</c>. Input is a
/// plain location name string carried on <see cref="FHIRBuildContext.DomainEntity"/>.
/// </summary>
internal sealed class LocationResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "Location";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not string locationName)
        {
            return Task.FromResult(Result<string>.Failure(
                $"LocationResourceBuilder expected string, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var locationId = FHIRBuilderSupport.GenerateDeterministicId("location", context.Key);

            var fhirLocation = new Dictionary<string, object?>
            {
                ["resourceType"] = "Location",
                ["id"] = locationId,
                ["status"] = "active",
                ["name"] = locationName,
                ["mode"] = "instance",
                ["type"] = new object[]
                {
                    new
                    {
                        coding = new object[]
                        {
                            new
                            {
                                system = "http://terminology.hl7.org/CodeSystem/v3-RoleCode",
                                code = "HOSP",
                                display = "Hospital"
                            }
                        }
                    }
                },
                ["telecom"] = new object[]
                {
                    new { system = "phone", value = $"+1-555-{random.Next(100, 999)}-{random.Next(1000, 9999)}", use = "work" }
                },
                ["address"] = new
                {
                    use = "work",
                    type = "physical",
                    line = new[] { $"{random.Next(100, 999)} Hospital Dr" },
                    city = "Medical City",
                    state = "MC",
                    postalCode = $"{random.Next(10000, 99999)}",
                    country = "US"
                },
                ["position"] = new
                {
                    longitude = -73.0 + random.NextDouble() * 10,
                    latitude = 40.0 + random.NextDouble() * 5
                },
                ["managingOrganization"] = new
                {
                    reference = "Organization/organization-main",
                    display = "General Hospital"
                }
            };

            context.References.Register("Location", locationId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirLocation, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR Location: {ex.Message}"));
        }
    }
}
