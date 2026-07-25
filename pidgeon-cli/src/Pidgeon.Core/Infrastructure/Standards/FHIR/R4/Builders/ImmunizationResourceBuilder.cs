// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>Immunization</c>.
/// </summary>
internal sealed class ImmunizationResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "Immunization";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"ImmunizationResourceBuilder expected Patient, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var immunizationId = FHIRBuilderSupport.GenerateDeterministicId("immunization", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, patient);

            var vaccines = new[]
            {
                ("08", "Hepatitis B vaccine", "28571000087109"),
                ("03", "Measles, mumps, rubella vaccine", "61153008"),
                ("10", "Poliovirus vaccine, inactivated", "396441007"),
                ("20", "Diphtheria, tetanus toxoids, pertussis", "421245007"),
                ("88", "Influenza virus vaccine", "46233009"),
                ("207", "COVID-19, mRNA, LNP-S, PF, 100 mcg/0.5mL", "28531000087107"),
                ("21", "Varicella virus vaccine", "108729007"),
                ("33", "Pneumococcal polysaccharide vaccine", "12866006")
            };
            var vaccine = vaccines[random.Next(vaccines.Length)];
            var lotNumber = $"LOT-{random.Next(10000, 99999)}-{(char)('A' + random.Next(0, 26))}";

            var fhirImmunization = new Dictionary<string, object?>
            {
                ["resourceType"] = "Immunization",
                ["id"] = immunizationId,
                ["status"] = "completed",
                ["vaccineCode"] = new
                {
                    coding = new object[]
                    {
                        new { system = "http://hl7.org/fhir/sid/cvx", code = vaccine.Item1, display = vaccine.Item2 },
                        new { system = "http://snomed.info/sct", code = vaccine.Item3, display = vaccine.Item2 }
                    },
                    text = vaccine.Item2
                },
                ["patient"] = new { reference = $"Patient/{patientId}" },
                ["occurrenceDateTime"] = GenerationDeterminism.CreateClock(context.Options).AddDays(-random.Next(1, 365)).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["lotNumber"] = lotNumber,
                ["site"] = new
                {
                    coding = new object[]
                    {
                        new { system = "http://terminology.hl7.org/CodeSystem/v3-ActSite", code = "LA", display = "Left arm" }
                    }
                },
                ["route"] = new
                {
                    coding = new object[]
                    {
                        new { system = "http://terminology.hl7.org/CodeSystem/v3-RouteOfAdministration", code = "IM", display = "Injection, intramuscular" }
                    }
                },
                ["performer"] = new object[]
                {
                    new
                    {
                        function = new
                        {
                            coding = new object[]
                            {
                                new
                                {
                                    system = "http://terminology.hl7.org/CodeSystem/v2-0443",
                                    code = "AP",
                                    display = "Administering Provider"
                                }
                            }
                        },
                        actor = new { display = "Nurse Smith, RN" }
                    }
                }
            };

            context.References.Register("Immunization", immunizationId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirImmunization, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR Immunization: {ex.Message}"));
        }
    }
}
