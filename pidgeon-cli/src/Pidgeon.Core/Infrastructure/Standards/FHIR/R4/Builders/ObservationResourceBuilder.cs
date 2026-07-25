// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated FHIR R4 builder for <c>Observation</c>.
///
/// Resolves <c>subject.reference</c> via the bundle-scoped
/// <see cref="FHIRBuildContext.References"/> map: if a Patient has been
/// registered in the same bundle, points to it; otherwise falls back to an
/// MRN-derived id or a fresh one.
///
/// When a scenario coordinator is present, generation is scenario-coherent:
/// LOINC codes coupled to the primary diagnosis, units/ranges from the scenario.
/// </summary>
internal sealed class ObservationResourceBuilder : IFHIRResourceBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string ResourceType => "Observation";
    public int Priority => 0;

    public async Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not ObservationResult observation)
        {
            return Result<string>.Failure(
                $"ObservationResourceBuilder expected ObservationResult, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}");
        }

        try
        {
            var observationId = FHIRBuilderSupport.GenerateDeterministicId("observation", context.Key);

            // Prefer the bundle-scoped Patient registered by PatientResourceBuilder;
            // fall back to the MRN-derived or coordinate-derived form so standalone
            // generation produces the same reproducible shape.
            var patientId = context.References.GetId("Patient")
                ?? (observation.Patient?.MedicalRecordNumber != null
                    ? $"patient-{observation.Patient.MedicalRecordNumber}"
                    : FHIRBuilderSupport.GenerateDeterministicId("patient", context.Key));

            // When a scenario coordinator is present, pull a LOINC+value from the
            // same lab test set the HL7 path uses. Falls back to the seed-driven
            // random pick when no coordinator is attached.
            var observationType = await ResolveObservationTypeAsync(context, cancellationToken)
                .ConfigureAwait(false);

            var fhirObservation = new Dictionary<string, object?>
            {
                ["resourceType"] = "Observation",
                ["id"] = observationId,
                ["status"] = "final",
                ["category"] = new object[]
                {
                    new
                    {
                        coding = new object[]
                        {
                            new
                            {
                                system = "http://terminology.hl7.org/CodeSystem/observation-category",
                                code = observationType.Category,
                                display = observationType.CategoryDisplay
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
                            code = observationType.LoincCode,
                            display = observationType.Display
                        }
                    }
                },
                ["subject"] = new { reference = $"Patient/{patientId}" },
                ["effectiveDateTime"] = GenerationDeterminism.CreateClock(context.Options).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["valueQuantity"] = new
                {
                    value = observationType.Value,
                    unit = observationType.Unit,
                    system = "http://unitsofmeasure.org",
                    code = observationType.UcumCode
                }
            };

            if (observation.ReferenceRange != null)
            {
                fhirObservation["referenceRange"] = new object[]
                {
                    new { text = observation.ReferenceRange }
                };
            }

            context.References.Register("Observation", observationId);
            return Result<string>.Success(JsonSerializer.Serialize(fhirObservation, JsonOptions));
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Failed to generate FHIR Observation: {ex.Message}");
        }
    }

    /// <summary>
    /// If the context carries a scenario coordinator, pull the first
    /// scenario-driven lab test and translate it into the ObservationType
    /// record used by the serializer. Otherwise, fall back to the seed-driven
    /// random pick.
    /// </summary>
    private static async Task<ObservationType> ResolveObservationTypeAsync(
        FHIRBuildContext context, CancellationToken cancellationToken)
    {
        if (context.ScenarioCoordinator != null)
        {
            var labs = await context.ScenarioCoordinator
                .GetLabTestsAsync(maxTests: 1, cancellationToken)
                .ConfigureAwait(false);
            var lab = labs.FirstOrDefault();
            if (lab != null && !string.IsNullOrEmpty(lab.LoincCode))
            {
                return new ObservationType(
                    LoincCode: lab.LoincCode,
                    Display: string.IsNullOrEmpty(lab.TestName) ? lab.LoincCode : lab.TestName,
                    Category: "laboratory",
                    CategoryDisplay: "Laboratory",
                    Value: double.TryParse(lab.Value, out var v) ? v : (object)lab.Value,
                    Unit: lab.Units,
                    UcumCode: lab.Units);
            }
        }

        return GetRandomObservationType(context.Key.Derive("observation-type"));
    }

    // Seed-driven random lab pick used when no scenario coordinator is
    // attached. When one is present, ClinicalScenarioCoordinator drives
    // scenario-coherent lab picks instead.
    private static ObservationType GetRandomObservationType(GenerationKey key)
    {
        var random = key.AsRandom();
        var observationTypes = new[]
        {
            new ObservationType("8480-6", "Systolic blood pressure", "vital-signs", "Vital Signs",
                               random.Next(90, 160), "mmHg", "mm[Hg]"),
            new ObservationType("8867-4", "Heart rate", "vital-signs", "Vital Signs",
                               random.Next(60, 120), "beats/minute", "/min"),
            new ObservationType("8310-5", "Body temperature", "vital-signs", "Vital Signs",
                               Math.Round(random.NextDouble() * (99.5 - 96.5) + 96.5, 1), "degrees F", "[degF]"),
            new ObservationType("33747-0", "General appearance", "exam", "Physical Exam",
                               1, "Normal", "1")
        };

        return observationTypes[random.Next(observationTypes.Length)];
    }

    private sealed record ObservationType(
        string LoincCode, string Display, string Category, string CategoryDisplay,
        object Value, string Unit, string UcumCode);
}
