// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>ServiceRequest</c>.
/// </summary>
internal sealed class ServiceRequestResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "ServiceRequest";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"ServiceRequestResourceBuilder expected Patient, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var serviceReqId = FHIRBuilderSupport.GenerateDeterministicId("servicerequest", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, patient);

            var services = new[]
            {
                ("70553", "MRI Brain without and with contrast", "241601008"),
                ("93306", "Echocardiogram, complete", "40701008"),
                ("97110", "Physical therapy, therapeutic exercises", "91251008"),
                ("99243", "Office or outpatient consultation", "11429006"),
                ("77067", "Screening mammography, bilateral", "241055006")
            };
            var service = services[random.Next(services.Length)];

            var fhirServiceReq = new Dictionary<string, object?>
            {
                ["resourceType"] = "ServiceRequest",
                ["id"] = serviceReqId,
                ["status"] = "active",
                ["intent"] = "order",
                ["priority"] = "routine",
                ["code"] = new
                {
                    coding = new object[]
                    {
                        new { system = "http://www.ama-assn.org/go/cpt", code = service.Item1, display = service.Item2 },
                        new { system = "http://snomed.info/sct", code = service.Item3, display = service.Item2 }
                    },
                    text = service.Item2
                },
                ["subject"] = new { reference = $"Patient/{patientId}" },
                ["authoredOn"] = GenerationDeterminism.CreateClock(context.Options).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["requester"] = new { display = "Dr. Ordering Provider" }
            };

            context.References.Register("ServiceRequest", serviceReqId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirServiceReq, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR ServiceRequest: {ex.Message}"));
        }
    }
}
