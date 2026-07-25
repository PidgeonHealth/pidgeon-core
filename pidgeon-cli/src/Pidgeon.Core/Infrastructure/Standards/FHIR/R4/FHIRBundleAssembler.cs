// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Messaging.FHIR.Bundles;
using Pidgeon.Core.Generation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4;

/// <summary>
/// Assembles FHIR resources into Bundle structures with proper reference integrity.
/// Supports Transaction, Searchset, and clinical scenario bundles.
/// </summary>
public class FHIRBundleAssembler
{
    private readonly IFHIRResourceFactory _resourceFactory;
    private readonly Pidgeon.Core.Generation.IGenerationService _generationService;

    public FHIRBundleAssembler(
        IFHIRResourceFactory resourceFactory,
        Pidgeon.Core.Generation.IGenerationService generationService)
    {
        _resourceFactory = resourceFactory;
        _generationService = generationService;
    }

    /// <summary>
    /// Assembles a Transaction bundle with a clinical scenario (Patient + Encounter + Condition + Observation).
    /// Resources reference each other with consistent internal IDs.
    /// </summary>
    public async Task<Result<string>> AssembleTransactionBundleAsync(GenerationOptions options)
    {
        await Task.Yield();
        var resources = new List<FHIRResource>();

        // Generate a coherent set of resources with shared patient context
        var patientResult = _generationService.GeneratePatient(options);
        if (!patientResult.IsSuccess)
            return Result<string>.Failure($"Bundle assembly failed: {patientResult.Error}");

        var patient = patientResult.Value;

        // Patient resource
        var patientJsonResult = _resourceFactory.GeneratePatient(patient, options);
        if (!patientJsonResult.IsSuccess)
            return Result<string>.Failure(patientJsonResult.Error);

        var patientId = ExtractResourceId(patientJsonResult.Value);
        resources.Add(new FHIRResource { ResourceType = "Patient", Id = patientId, JsonContent = patientJsonResult.Value });

        // Practitioner resource (needed for Encounter references)
        var providerResult = _generationService.GenerateProvider(options);
        string? practitionerId = null;
        if (providerResult.IsSuccess)
        {
            var practitionerJsonResult = _resourceFactory.GeneratePractitioner(providerResult.Value, options);
            if (practitionerJsonResult.IsSuccess)
            {
                practitionerId = ExtractResourceId(practitionerJsonResult.Value);
                resources.Add(new FHIRResource { ResourceType = "Practitioner", Id = practitionerId, JsonContent = practitionerJsonResult.Value });
            }
        }

        // Encounter resource
        var encounterResult = _generationService.GenerateEncounter(options);
        if (encounterResult.IsSuccess)
        {
            var encounterJsonResult = _resourceFactory.GenerateEncounter(encounterResult.Value, options);
            if (encounterJsonResult.IsSuccess)
            {
                var encounterId = ExtractResourceId(encounterJsonResult.Value);
                var patchedEncounter = PatchReference(encounterJsonResult.Value, "Patient", patientId);
                if (practitionerId != null)
                    patchedEncounter = PatchPractitionerReference(patchedEncounter, practitionerId);
                resources.Add(new FHIRResource { ResourceType = "Encounter", Id = encounterId, JsonContent = patchedEncounter });
            }
        }

        // Condition resource
        var conditionJsonResult = _resourceFactory.GenerateCondition(patient, options);
        if (conditionJsonResult.IsSuccess)
        {
            var conditionId = ExtractResourceId(conditionJsonResult.Value);
            var patchedCondition = PatchReference(conditionJsonResult.Value, "Patient", patientId);
            resources.Add(new FHIRResource { ResourceType = "Condition", Id = conditionId, JsonContent = patchedCondition });
        }

        // Observation resource
        var observationResult = _generationService.GenerateObservationResult(options);
        if (observationResult.IsSuccess)
        {
            var observationJsonResult = _resourceFactory.GenerateObservation(observationResult.Value, options);
            if (observationJsonResult.IsSuccess)
            {
                var observationId = ExtractResourceId(observationJsonResult.Value);
                var patchedObservation = PatchReference(observationJsonResult.Value, "Patient", patientId);
                resources.Add(new FHIRResource { ResourceType = "Observation", Id = observationId, JsonContent = patchedObservation });
            }
        }

        return _resourceFactory.GenerateBundle(resources, FHIRBundleType.Transaction, options);
    }

    /// <summary>
    /// Assembles a Searchset bundle with multiple resources of the same type.
    /// </summary>
    public async Task<Result<string>> AssembleSearchsetBundleAsync(string resourceType, int count, GenerationOptions options)
    {
        await Task.Yield();
        var resources = new List<FHIRResource>();

        // Each bundle entry's seed is derived from a coordinate-addressed bundle key rather than
        // seed + i arithmetic, so a seeded bundle reproduces byte-for-byte and an unseeded one still
        // varies per run (CreateKey draws a fresh root when no seed is supplied).
        var bundleKey = GenerationDeterminism.CreateKey(options).Derive("fhir-bundle").Derive(resourceType.ToLowerInvariant());
        for (int i = 0; i < count; i++)
        {
            var iterationOptions = options with { Seed = bundleKey.Derive(i).AsRandom().Next() };

            var jsonResult = resourceType.ToLowerInvariant() switch
            {
                "patient" => GeneratePatientForBundle(iterationOptions),
                "observation" => GenerateObservationForBundle(iterationOptions),
                "encounter" => GenerateEncounterForBundle(iterationOptions),
                _ => GeneratePatientForBundle(iterationOptions)
            };

            if (jsonResult.IsSuccess)
            {
                var id = ExtractResourceId(jsonResult.Value);
                resources.Add(new FHIRResource { ResourceType = resourceType, Id = id, JsonContent = jsonResult.Value });
            }
        }

        return _resourceFactory.GenerateBundle(resources, FHIRBundleType.Searchset, options);
    }

    /// <summary>
    /// Assembles a clinical scenario bundle (e.g., cardiac admission) with coherent cross-references.
    /// </summary>
    public async Task<Result<string>> AssembleClinicalScenarioBundleAsync(string scenarioType, GenerationOptions options)
    {
        await Task.Yield();
        var resources = new List<FHIRResource>();

        // Generate shared patient
        var patientResult = _generationService.GeneratePatient(options);
        if (!patientResult.IsSuccess)
            return Result<string>.Failure($"Clinical scenario bundle failed: {patientResult.Error}");
        var patient = patientResult.Value;

        var patientJsonResult = _resourceFactory.GeneratePatient(patient, options);
        if (!patientJsonResult.IsSuccess) return Result<string>.Failure(patientJsonResult.Error);
        var patientId = ExtractResourceId(patientJsonResult.Value);
        resources.Add(new FHIRResource { ResourceType = "Patient", Id = patientId, JsonContent = patientJsonResult.Value });

        // Generate shared provider
        var providerResult = _generationService.GenerateProvider(options);
        if (providerResult.IsSuccess)
        {
            var practitionerJsonResult = _resourceFactory.GeneratePractitioner(providerResult.Value, options);
            if (practitionerJsonResult.IsSuccess)
            {
                var practId = ExtractResourceId(practitionerJsonResult.Value);
                resources.Add(new FHIRResource { ResourceType = "Practitioner", Id = practId, JsonContent = practitionerJsonResult.Value });
            }
        }

        // Encounter
        var encounterResult = _generationService.GenerateEncounter(options);
        if (encounterResult.IsSuccess)
        {
            var encounterJsonResult = _resourceFactory.GenerateEncounter(encounterResult.Value, options);
            if (encounterJsonResult.IsSuccess)
            {
                var encId = ExtractResourceId(encounterJsonResult.Value);
                var patchedEnc = PatchReference(encounterJsonResult.Value, "Patient", patientId);
                resources.Add(new FHIRResource { ResourceType = "Encounter", Id = encId, JsonContent = patchedEnc });
            }
        }

        // Condition (cardiac for cardiac scenario)
        var conditionJsonResult = _resourceFactory.GenerateCondition(patient, options);
        if (conditionJsonResult.IsSuccess)
        {
            var condId = ExtractResourceId(conditionJsonResult.Value);
            var patchedCond = PatchReference(conditionJsonResult.Value, "Patient", patientId);
            resources.Add(new FHIRResource { ResourceType = "Condition", Id = condId, JsonContent = patchedCond });
        }

        // Observation (vitals)
        var observationResult = _generationService.GenerateObservationResult(options);
        if (observationResult.IsSuccess)
        {
            var obsJsonResult = _resourceFactory.GenerateObservation(observationResult.Value, options);
            if (obsJsonResult.IsSuccess)
            {
                var obsId = ExtractResourceId(obsJsonResult.Value);
                var patchedObs = PatchReference(obsJsonResult.Value, "Patient", patientId);
                resources.Add(new FHIRResource { ResourceType = "Observation", Id = obsId, JsonContent = patchedObs });
            }
        }

        // MedicationRequest
        var rxResult = _generationService.GeneratePrescription(options);
        if (rxResult.IsSuccess)
        {
            var medReqJsonResult = _resourceFactory.GenerateMedicationRequest(rxResult.Value, options);
            if (medReqJsonResult.IsSuccess)
            {
                var medReqId = ExtractResourceId(medReqJsonResult.Value);
                var patchedMedReq = PatchReference(medReqJsonResult.Value, "Patient", patientId);
                resources.Add(new FHIRResource { ResourceType = "MedicationRequest", Id = medReqId, JsonContent = patchedMedReq });
            }
        }

        return _resourceFactory.GenerateBundle(resources, FHIRBundleType.Transaction, options);
    }

    private Result<string> GeneratePatientForBundle(GenerationOptions options)
    {
        var patientResult = _generationService.GeneratePatient(options);
        if (!patientResult.IsSuccess) return Result<string>.Failure(patientResult.Error);
        return _resourceFactory.GeneratePatient(patientResult.Value, options);
    }

    private Result<string> GenerateObservationForBundle(GenerationOptions options)
    {
        var obsResult = _generationService.GenerateObservationResult(options);
        if (!obsResult.IsSuccess) return Result<string>.Failure(obsResult.Error);
        return _resourceFactory.GenerateObservation(obsResult.Value, options);
    }

    private Result<string> GenerateEncounterForBundle(GenerationOptions options)
    {
        var encResult = _generationService.GenerateEncounter(options);
        if (!encResult.IsSuccess) return Result<string>.Failure(encResult.Error);
        return _resourceFactory.GenerateEncounter(encResult.Value, options);
    }

    /// <summary>
    /// Extracts the resource ID from generated FHIR JSON.
    /// </summary>
    internal static string ExtractResourceId(string fhirJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(fhirJson);
            if (doc.RootElement.TryGetProperty("id", out var idElement) && idElement.GetString() is { } id)
                return id;
        }
        catch { }
        // Builders always emit a deterministic id, so this is an unreachable guard on the happy path.
        // Derive a stable id from the content hash rather than Guid.NewGuid, so even the guard never makes
        // a bundle entry id depend on run-to-run randomness.
        return $"resource-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fhirJson)))[..32].ToLowerInvariant()}";
    }

    /// <summary>
    /// Patches all Practitioner references in FHIR JSON to point to the correct practitioner ID.
    /// </summary>
    private static string PatchPractitionerReference(string fhirJson, string practitionerId)
    {
        try
        {
            // Find and replace any Practitioner/xxx reference with the correct one
            var practPattern = "Practitioner/practitioner-";
            var idx = fhirJson.IndexOf(practPattern, StringComparison.Ordinal);
            if (idx >= 0)
            {
                // Find the end of the reference value (up to the next quote)
                var startOfRef = idx;
                var endOfRef = fhirJson.IndexOf('"', startOfRef);
                if (endOfRef > startOfRef)
                {
                    var oldRef = fhirJson[startOfRef..endOfRef];
                    return fhirJson.Replace(oldRef, $"Practitioner/{practitionerId}");
                }
            }
        }
        catch { }
        return fhirJson;
    }

    /// <summary>
    /// Patches a reference in FHIR JSON to point to the correct resource ID.
    /// </summary>
    private static string PatchReference(string fhirJson, string resourceType, string resourceId)
    {
        try
        {
            using var doc = JsonDocument.Parse(fhirJson);
            var root = doc.RootElement;

            // Check if subject reference exists and update it
            if (root.TryGetProperty("subject", out var subject))
            {
                if (subject.TryGetProperty("reference", out var refValue))
                {
                    var currentRef = refValue.GetString();
                    if (currentRef != null && currentRef.StartsWith($"{resourceType}/"))
                    {
                        return fhirJson.Replace(currentRef, $"{resourceType}/{resourceId}");
                    }
                }
            }
        }
        catch { }
        return fhirJson;
    }
}
