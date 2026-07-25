// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>DiagnosticReport</c>.
/// </summary>
internal sealed class DiagnosticReportResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "DiagnosticReport";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not Patient patient)
        {
            return Task.FromResult(Result<string>.Failure(
                $"DiagnosticReportResourceBuilder expected Patient, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var random = context.Key.Derive("values").AsRandom();
            var reportId = FHIRBuilderSupport.GenerateDeterministicId("diagnosticreport", context.Key);
            var patientId = FHIRBuilderSupport.ResolvePatientId(context, patient);

            var reports = new[]
            {
                ("58410-2", "Complete blood count (CBC) panel", "LAB"),
                ("24323-8", "Comprehensive metabolic panel", "LAB"),
                ("36643-5", "Chest X-ray", "RAD"),
                ("57698-3", "Lipid panel", "LAB"),
                ("24356-8", "Urinalysis complete", "LAB")
            };
            var report = reports[random.Next(reports.Length)];

            var effectiveDate = GenerationDeterminism.CreateClock(context.Options).AddHours(-random.Next(1, 72));

            var fhirReport = new Dictionary<string, object?>
            {
                ["resourceType"] = "DiagnosticReport",
                ["id"] = reportId,
                ["status"] = "final",
                ["category"] = new object[]
                {
                    new
                    {
                        coding = new object[]
                        {
                            new
                            {
                                system = "http://terminology.hl7.org/CodeSystem/v2-0074",
                                code = report.Item3,
                                display = report.Item3 == "LAB" ? "Laboratory" : "Radiology"
                            }
                        }
                    }
                },
                ["code"] = new
                {
                    coding = new object[]
                    {
                        new
                        {
                            system = "http://loinc.org",
                            code = report.Item1,
                            display = report.Item2
                        }
                    },
                    text = report.Item2
                },
                ["subject"] = new { reference = $"Patient/{patientId}" },
                ["effectiveDateTime"] = effectiveDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["issued"] = GenerationDeterminism.CreateClock(context.Options).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["conclusion"] = "All values within normal limits."
            };

            context.References.Register("DiagnosticReport", reportId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirReport, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR DiagnosticReport: {ex.Message}"));
        }
    }
}
