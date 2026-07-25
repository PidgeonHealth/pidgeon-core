// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>DocumentReference</c>.
/// </summary>
internal sealed class DocumentReferenceResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "DocumentReference";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"DocumentReferenceResourceBuilder expected Patient, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var docRefId = FHIRBuilderSupport.GenerateDeterministicId("documentreference", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, patient);

            var docTypes = new[]
            {
                ("11506-3", "Progress note"),
                ("18842-5", "Discharge summary"),
                ("11488-4", "Consultation note"),
                ("26436-6", "Laboratory studies"),
                ("18748-4", "Diagnostic imaging study")
            };
            var docType = docTypes[random.Next(docTypes.Length)];

            var fhirDocRef = new Dictionary<string, object?>
            {
                ["resourceType"] = "DocumentReference",
                ["id"] = docRefId,
                ["status"] = "current",
                ["type"] = new
                {
                    coding = new object[]
                    {
                        new { system = "http://loinc.org", code = docType.Item1, display = docType.Item2 }
                    },
                    text = docType.Item2
                },
                ["subject"] = new { reference = $"Patient/{patientId}" },
                ["date"] = GenerationDeterminism.CreateClock(context.Options).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["content"] = new object[]
                {
                    new
                    {
                        attachment = new
                        {
                            contentType = "application/pdf",
                            url = $"http://hospital.example.org/documents/{docRefId}.pdf",
                            title = docType.Item2,
                            creation = GenerationDeterminism.CreateClock(context.Options).ToString("yyyy-MM-ddTHH:mm:ssZ")
                        }
                    }
                }
            };

            context.References.Register("DocumentReference", docRefId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirDocRef, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR DocumentReference: {ex.Message}"));
        }
    }
}
