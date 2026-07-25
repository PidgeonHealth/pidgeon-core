// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>RelatedPerson</c>.
/// </summary>
internal sealed class RelatedPersonResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "RelatedPerson";
    public int Priority => 0;

    /// <summary>
    /// Relationship codes emitted in <c>RelatedPerson.relationship</c>, drawn from the
    /// v3-RoleCode code system. Every code must exist in the published CodeSystem —
    /// a conformant FHIR server rejects the resource otherwise.
    /// </summary>
    internal static readonly (string Code, string Display)[] Relationships =
    [
        ("SPS", "spouse"),
        ("PRN", "parent"),
        ("CHILD", "child"),
        ("SIB", "sibling"),
        ("ECON", "emergency contact"),
        ("GUARD", "guardian")
    ];

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"RelatedPersonResourceBuilder expected Patient, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var relatedId = FHIRBuilderSupport.GenerateDeterministicId("relatedperson", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, patient);

            var relationship = Relationships[random.Next(Relationships.Length)];

            var firstNames = new[] { "Jane", "John", "Mary", "Robert", "Susan", "James" };
            var lastNames = new[] { patient.Name.Family ?? "Smith" };
            var firstName = firstNames[random.Next(firstNames.Length)];
            var lastName = lastNames[0];

            var fhirRelated = new Dictionary<string, object?>
            {
                ["resourceType"] = "RelatedPerson",
                ["id"] = relatedId,
                ["active"] = true,
                ["patient"] = new { reference = $"Patient/{patientId}" },
                ["relationship"] = new object[]
                {
                    new
                    {
                        coding = new object[]
                        {
                            new
                            {
                                system = "http://terminology.hl7.org/CodeSystem/v3-RoleCode",
                                code = relationship.Code,
                                display = relationship.Display
                            }
                        }
                    }
                },
                ["name"] = new object[]
                {
                    new
                    {
                        use = "official",
                        family = lastName,
                        given = new[] { firstName }
                    }
                },
                ["telecom"] = new object[]
                {
                    new { system = "phone", value = $"+1-555-{random.Next(100, 999)}-{random.Next(1000, 9999)}", use = "home" }
                },
                ["gender"] = firstName is "Jane" or "Mary" or "Susan" ? "female" : "male"
            };

            context.References.Register("RelatedPerson", relatedId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirRelated, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR RelatedPerson: {ex.Message}"));
        }
    }
}
