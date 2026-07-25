// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>Practitioner</c>.
/// </summary>
internal sealed class PractitionerResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "Practitioner";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Provider provider)
        {
            return Task.FromResult(Result<string>.Failure(
                $"PractitionerResourceBuilder expected Provider, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var practitionerId = FHIRBuilderSupport.GenerateDeterministicId("practitioner", context.Key);

            var fhirPractitioner = new Dictionary<string, object?>
            {
                ["resourceType"] = "Practitioner",
                ["id"] = practitionerId,
                ["active"] = true,
                ["name"] = new object[]
                {
                    new
                    {
                        use = "official",
                        family = provider.Name.Family ?? "Unknown",
                        given = new[] { provider.Name.Given ?? "Unknown" },
                        prefix = new[] { "Dr." },
                        text = $"Dr. {provider.Name.DisplayName}"
                    }
                },
                ["telecom"] = new object[]
                {
                    new { system = "phone", value = provider.PhoneNumber ?? "+1-555-0123", use = "work" },
                    new
                    {
                        system = "email",
                        value = provider.EmailAddress ?? $"{provider.Name.Given?.ToLower() ?? "provider"}.{provider.Name.Family?.ToLower() ?? "unknown"}@hospital.org",
                        use = "work"
                    }
                },
                ["address"] = new object[]
                {
                    new
                    {
                        use = "work",
                        type = "physical",
                        line = new[] { "123 Medical Center Dr" },
                        city = "Healthcare City",
                        state = "HC",
                        postalCode = "12345",
                        country = "US"
                    }
                },
                ["gender"] = "unknown",
                ["qualification"] = new object[]
                {
                    new
                    {
                        identifier = new object[]
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
                                            code = "MD",
                                            display = "Medical License number"
                                        }
                                    }
                                },
                                system = "http://hl7.org/fhir/sid/us-npi",
                                value = provider.LicenseNumber ?? provider.NpiNumber ?? "0000000000"
                            }
                        },
                        code = new
                        {
                            coding = new object[]
                            {
                                new
                                {
                                    system = "http://nucc.org/provider-taxonomy",
                                    code = FHIRBuilderSupport.GetNUCCCodeForSpecialty(provider.Specialty),
                                    display = provider.Specialty ?? "Family Medicine"
                                }
                            },
                            text = provider.Specialty ?? "Family Medicine"
                        }
                    }
                }
            };

            context.References.Register("Practitioner", practitionerId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirPractitioner, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR Practitioner: {ex.Message}"));
        }
    }
}
