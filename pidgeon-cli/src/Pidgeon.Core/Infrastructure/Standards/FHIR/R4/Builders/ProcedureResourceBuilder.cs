// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>Procedure</c>.
/// </summary>
internal sealed class ProcedureResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "Procedure";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"ProcedureResourceBuilder expected Patient, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var procedureId = FHIRBuilderSupport.GenerateDeterministicId("procedure", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, patient);

            var procedures = new[]
            {
                ("47562", "Laparoscopic appendectomy", "80146002"),
                ("45378", "Colonoscopy", "73761001"),
                ("93458", "Left heart catheterization", "41976001"),
                ("29881", "Knee arthroscopy with meniscectomy", "29554001"),
                ("66984", "Extracapsular cataract removal", "54885007")
            };
            var procedure = procedures[random.Next(procedures.Length)];

            var performedDate = GenerationDeterminism.CreateClock(context.Options).AddDays(-random.Next(1, 90));

            var fhirProcedure = new Dictionary<string, object?>
            {
                ["resourceType"] = "Procedure",
                ["id"] = procedureId,
                ["status"] = "completed",
                ["code"] = new
                {
                    coding = new object[]
                    {
                        new { system = "http://www.ama-assn.org/go/cpt", code = procedure.Item1, display = procedure.Item2 },
                        new { system = "http://snomed.info/sct", code = procedure.Item3, display = procedure.Item2 }
                    },
                    text = procedure.Item2
                },
                ["subject"] = new { reference = $"Patient/{patientId}" },
                ["performedDateTime"] = performedDate.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };

            context.References.Register("Procedure", procedureId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirProcedure, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR Procedure: {ex.Message}"));
        }
    }
}
