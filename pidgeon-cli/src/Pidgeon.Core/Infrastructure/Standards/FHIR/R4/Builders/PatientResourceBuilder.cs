// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>Patient</c>. Registers the emitted ID on
/// <see cref="FHIRBuildContext.References"/> so downstream resources in
/// the same bundle can resolve <c>subject</c> references to this patient.
/// </summary>
internal sealed class PatientResourceBuilder : IFHIRResourceBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string ResourceType => "Patient";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"PatientResourceBuilder expected Pidgeon.Core.Domain.Clinical.Entities.Patient on the context, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var patientId = FHIRBuilderSupport.GenerateDeterministicId("patient", context.Key);

            var fhirPatient = new Dictionary<string, object?>
            {
                ["resourceType"] = "Patient",
                ["id"] = patientId,
                ["meta"] = new { versionId = "1", lastUpdated = GenerationDeterminism.CreateClock(context.Options).ToString("yyyy-MM-ddTHH:mm:ssZ") },
                ["identifier"] = new object[]
                {
                    new
                    {
                        use = "usual",
                        type = new
                        {
                            coding = new object[]
                            {
                                new
                                {
                                    system = "http://terminology.hl7.org/CodeSystem/v2-0203",
                                    code = "MR",
                                    display = "Medical Record Number"
                                }
                            }
                        },
                        system = "http://hospital.example.org/mrn",
                        value = patient.MedicalRecordNumber ?? patientId
                    }
                },
                ["active"] = true,
                ["name"] = new object[]
                {
                    new
                    {
                        use = "official",
                        family = patient.Name.Family ?? "Unknown",
                        given = new[] { patient.Name.Given ?? "Unknown" },
                        text = patient.Name.DisplayName
                    }
                },
                ["gender"] = patient.Gender?.ToString().ToLowerInvariant() ?? "unknown",
                ["birthDate"] = patient.BirthDate?.ToString("yyyy-MM-dd")
            };

            if (patient.PhoneNumber != null || patient.EmailAddress != null)
            {
                var telecom = new List<object>();
                if (patient.PhoneNumber != null)
                    telecom.Add(new { system = "phone", value = patient.PhoneNumber, use = "home" });
                if (patient.EmailAddress != null)
                    telecom.Add(new { system = "email", value = patient.EmailAddress });
                fhirPatient["telecom"] = telecom.ToArray();
            }

            if (patient.Address != null)
            {
                fhirPatient["address"] = new object[]
                {
                    new
                    {
                        use = "home",
                        type = "physical",
                        line = new[] { patient.Address.Street1 ?? "123 Main St" },
                        city = patient.Address.City ?? "Anytown",
                        state = patient.Address.State ?? "XX",
                        postalCode = patient.Address.PostalCode ?? "00000",
                        country = patient.Address.Country ?? "US"
                    }
                };
            }

            if (patient.MaritalStatus.HasValue)
            {
                var (code, display) = patient.MaritalStatus.Value switch
                {
                    MaritalStatus.Single => ("S", "Never Married"),
                    MaritalStatus.Married => ("M", "Married"),
                    MaritalStatus.Divorced => ("D", "Divorced"),
                    MaritalStatus.Widowed => ("W", "Widowed"),
                    MaritalStatus.Separated => ("L", "Legally Separated"),
                    _ => ("UNK", "Unknown")
                };
                fhirPatient["maritalStatus"] = new
                {
                    coding = new object[]
                    {
                        new { system = "http://terminology.hl7.org/CodeSystem/v3-MaritalStatus", code, display }
                    },
                    text = display
                };
            }

            context.References.Register("Patient", patientId);
            return Task.FromResult(Result<string>.Success(JsonSerializer.Serialize(fhirPatient, JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR Patient: {ex.Message}"));
        }
    }
}
