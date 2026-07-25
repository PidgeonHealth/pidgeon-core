// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Application.Interfaces.Standards;
using Pidgeon.Core.Application.Services.Clinical;
using Pidgeon.Core.Domain.Messaging.FHIR.Bundles;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

namespace Pidgeon.Core.Application.Services.Generation.Plugins;

/// <summary>
/// FHIR-specific resource generation plugin using thin orchestrator pattern.
/// Delegates FHIR JSON construction to IFHIRResourceFactory following HL7 architecture.
/// Each dispatch arm pairs a resource type with the domain entity its builder
/// (or the factory fallback) expects; the shared <c>WithEntityAsync</c> helpers
/// carry the entity-generation error handling once.
/// </summary>
internal class FHIRMessageGenerationPlugin : IMessageGenerationPlugin
{
    private readonly Pidgeon.Core.Generation.IGenerationService _domainGenerationService;
    private readonly IStandardPluginRegistry _pluginRegistry;
    private readonly IFHIRResourceFactory _fhirResourceFactory;
    private readonly FHIRResourceBuilderRegistry _builderRegistry;
    private readonly ClinicalScenarioCoordinator _scenarioCoordinator;

    public string StandardName => "fhir";

    public FHIRMessageGenerationPlugin(
        Pidgeon.Core.Generation.IGenerationService domainGenerationService,
        IStandardPluginRegistry pluginRegistry,
        IFHIRResourceFactory fhirResourceFactory,
        FHIRResourceBuilderRegistry builderRegistry,
        ClinicalScenarioCoordinator scenarioCoordinator)
    {
        _domainGenerationService = domainGenerationService;
        _pluginRegistry = pluginRegistry;
        _fhirResourceFactory = fhirResourceFactory;
        _builderRegistry = builderRegistry;
        _scenarioCoordinator = scenarioCoordinator;
    }

    /// <summary>
    /// Per-resource dispatch helper. If a dedicated <see cref="IFHIRResourceBuilder"/>
    /// is registered for <paramref name="resourceType"/>, it owns the
    /// generation. Returns null otherwise so the caller can fall back to the
    /// <see cref="IFHIRResourceFactory"/> path.
    ///
    /// The scenario coordinator is passed through the build context so
    /// scenario-aware builders (Observation, Condition, MedicationRequest,
    /// Encounter) produce clinically coherent codes matching what the HL7 path
    /// emits for the same scenario + seed.
    /// </summary>
    private async Task<Result<string>?> TryBuildFromRegistryAsync(
        string resourceType, object? domainEntity, GenerationOptions options)
    {
        var builder = _builderRegistry.GetBuilder(resourceType);
        if (builder == null) return null;

        var context = new FHIRBuildContext
        {
            DomainEntity = domainEntity,
            Options = options,
            References = new FHIRReferenceMap(),
            ScenarioCoordinator = _scenarioCoordinator,
            Key = GenerationDeterminism.CreateKey(options).Derive("fhir").Derive(resourceType.ToLowerInvariant())
        };
        return await builder.BuildAsync(context);
    }

    /// <summary>
    /// Runs <paramref name="build"/> against a successfully generated domain
    /// entity, or surfaces the generation error. The factory-only dispatch path.
    /// </summary>
    private static async Task<Result<string>> WithEntityAsync<T>(
        Result<T> entity, Func<T, Result<string>> build)
    {
        await Task.Yield();
        return entity.IsSuccess ? build(entity.Value) : Result<string>.Failure(entity.Error);
    }

    /// <summary>
    /// Registry-first dispatch: a dedicated builder (scenario-aware) wins when
    /// registered; otherwise the factory fallback serializes the entity.
    /// </summary>
    private async Task<Result<string>> WithEntityAsync<T>(
        Result<T> entity, string resourceType, Func<T, Result<string>> factoryFallback, GenerationOptions options)
    {
        await Task.Yield();
        if (!entity.IsSuccess)
            return Result<string>.Failure(entity.Error);

        var built = await TryBuildFromRegistryAsync(resourceType, entity.Value, options);
        return built ?? factoryFallback(entity.Value);
    }

    /// <summary>
    /// Dispatch default for resource types with a dedicated registry builder but no
    /// bespoke domain-entity pipeline (Task, Appointment, ClaimResponse, ...): seed the
    /// build with a generated Patient — the common linkage entity — and let the builder
    /// own the rest. Builders for patient-independent types (e.g. Questionnaire) simply
    /// ignore the entity. Types with neither a switch case nor a builder keep the
    /// unsupported-type error.
    /// </summary>
    private async Task<Result<string>> GenerateRegistryBackedResourceAsync(
        string resourceType, GenerationOptions options)
    {
        if (_builderRegistry.GetBuilder(resourceType) is not null)
        {
            var patientResult = _domainGenerationService.GeneratePatient(options);
            if (!patientResult.IsSuccess)
                return Result<string>.Failure(patientResult.Error);

            var built = await TryBuildFromRegistryAsync(resourceType, patientResult.Value, options);
            if (built is { } handBuilt)
                return handBuilt;
        }

        // Structural tier (ADR-0001 pilot): a type with no dedicated builder
        // routes to the schema-derived synthesizer registered under the "*" sentinel,
        // which reads the requested type from the domain-entity slot and emits a
        // minimal valid instance from the loaded base StructureDefinition. Hand-built
        // builders always win above; in a composition without the sentinel builder
        // the type stays honestly unsupported.
        var structural = await TryBuildFromRegistryAsync("*", resourceType, options);
        return structural ?? Result<string>.Failure(GetUnsupportedMessageTypeError(resourceType));
    }

    // Every concrete FHIR R4 resource type (the spec-constant list in
    // MessageTypeVocabulary). The curated core resolves to hand-built builders and
    // factory arms; the remainder routes to the structural synthesizer's sentinel
    // builder when it is registered, and to an actionable unsupported /
    // package-required error otherwise — so CanHandle/GetSupported stay honest
    // with dispatch: every listed type resolves to SOME handler that answers for
    // itself, and the capability catalog derives each cell's level from live
    // evidence rather than from this list.
    private static readonly HashSet<string> _resourceTypes =
        new(Pidgeon.Core.Domain.Messaging.MessageTypeVocabulary.FhirR4ResourceTypes, StringComparer.OrdinalIgnoreCase);

    public bool CanHandleMessageType(string messageType)
    {
        return !string.IsNullOrWhiteSpace(messageType) &&
               _resourceTypes.Contains(messageType);
    }

    public async Task<Result<IReadOnlyList<string>>> GenerateMessagesAsync(string messageType, int count, GenerationOptions? options = null)
    {
        try
        {
            var messages = new List<string>();
            var generationOptions = options ?? new GenerationOptions();

            // Clinical content is addressed by coordinate, not RNG draw order (mirrors the HL7
            // plugin): the scenario stream shares the seeded root, narrowed per resource index.
            var scenarioRoot = GenerationDeterminism.CreateKey(generationOptions).Derive(messageType).Derive("scenario");

            for (int i = 0; i < count; i++)
            {
                // Reseed the shared coordinator per resource. Without this, scenario-aware builders
                // (Observation, Condition, MedicationRequest, Encounter) draw from the coordinator's
                // constructor-time Random and a Random.Shared-rooted labs key, so a pinned-seed FHIR
                // run still produces different clinical content every run (b49 — the official-validator
                // cross-check flipped agree-valid/divergent on whichever lab the Observation drew).
                // Non-cohort resources get a fresh scenario per index; a cohort keeps its shared
                // scenario and patient across the batch, matching the HL7 plugin's contract.
                if (!generationOptions.IsCohortSequence)
                    _scenarioCoordinator.ClearScenario();
                _scenarioCoordinator.ApplySeed(scenarioRoot.Derive(i));

                // The index is the resource-instance coordinate (ADR-0005 §3): CreateKey folds it
                // into the entropy root so each entry draws its own entity, resource id, and field
                // values from a per-message stream, matching the HL7/NCPDP batch keying. Replaces
                // the per-iteration reseed (batchKey.Derive(i).AsRandom().Next()), which truncated
                // the derived key to a 31-bit seed — birthday-collision territory at batch scale.
                var iterationOptions = generationOptions with { MessageIndex = i };

                var result = await GenerateSingleResourceAsync(messageType, iterationOptions);

                if (!result.IsSuccess)
                    return Result<IReadOnlyList<string>>.Failure(result.Error);

                messages.Add(result.Value);
            }

            return Result<IReadOnlyList<string>>.Success(messages);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<string>>.Failure($"FHIR resource generation failed: {ex.Message}");
        }
    }

    public IReadOnlyList<string> GetSupportedMessageTypes()
    {
        return _resourceTypes.OrderBy(x => x).ToList();
    }

    public string GetUnsupportedMessageTypeError(string messageType)
    {
        var suggestions = new List<string>();

        var commonSuggestions = messageType.ToLowerInvariant() switch
        {
            "adt^a01" or "adt" => "FHIR doesn't use HL7 v2 format. Use 'Encounter' for patient visits or 'Patient' for demographics",
            "rde^o11" or "rde" => "FHIR doesn't use HL7 format. Use 'MedicationRequest' for prescriptions",
            "oru^r01" or "oru" => "FHIR doesn't use HL7 format. Use 'Observation' for lab results or 'DiagnosticReport' for reports",
            "prescription" => "FHIR uses 'MedicationRequest' (order) or 'MedicationDispense' (supply) for prescriptions",
            "lab" or "labs" => "FHIR uses 'Observation' for individual lab results or 'DiagnosticReport' for lab reports",
            "visit" => "FHIR uses 'Encounter' for patient visits and healthcare interactions",
            "appointment" => "FHIR has 'Appointment' resource - you're close! Try 'Appointment'",
            "order" => "FHIR uses 'ServiceRequest' for orders, or 'MedicationRequest' for medication orders",
            "result" or "results" => "FHIR uses 'Observation' for individual results or 'DiagnosticReport' for complete reports",
            "document" => "FHIR uses 'DocumentReference' for clinical documents",
            _ => null
        };

        if (commonSuggestions != null)
            suggestions.Add(commonSuggestions);

        var similarResources = _resourceTypes
            .Where(r => r.StartsWith(messageType, StringComparison.OrdinalIgnoreCase) ||
                       messageType.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        if (similarResources.Any())
            suggestions.Add($"Did you mean: {string.Join(", ", similarResources)}?");

        var errorMessage = $"FHIR standard doesn't support resource type: {messageType}";
        if (suggestions.Any())
        {
            errorMessage += "\n\nSuggestions:\n" + string.Join("\n", suggestions.Select(s => $"  • {s}"));
        }
        else
        {
            var commonResources = new[] { "Patient", "Encounter", "Observation", "MedicationRequest", "DiagnosticReport" };
            errorMessage += $"\n\nCommon FHIR resources:\n  • {string.Join("\n  • ", commonResources)}";
        }

        return errorMessage;
    }

    private async Task<Result<string>> GenerateSingleResourceAsync(string resourceType, GenerationOptions options)
    {
        var gen = _domainGenerationService;
        var factory = _fhirResourceFactory;

        return resourceType.ToLowerInvariant() switch
        {
            // Registry-first types: a dedicated builder wins, factory is the fallback.
            "patient" => await WithEntityAsync(gen.GeneratePatient(options), "Patient", p => factory.GeneratePatient(p, options), options),
            "encounter" => await WithEntityAsync(gen.GenerateEncounter(options), "Encounter", e => factory.GenerateEncounter(e, options), options),
            "observation" => await WithEntityAsync(gen.GenerateObservationResult(options), "Observation", o => factory.GenerateObservation(o, options), options),
            "condition" => await WithEntityAsync(gen.GeneratePatient(options), "Condition", p => factory.GenerateCondition(p, options), options),
            "medicationrequest" => await WithEntityAsync(gen.GeneratePrescription(options), "MedicationRequest", rx => factory.GenerateMedicationRequest(rx, options), options),

            // Factory-only types, keyed by the domain entity they serialize.
            "practitioner" => await WithEntityAsync(gen.GenerateProvider(options), p => factory.GeneratePractitioner(p, options)),
            "practitionerrole" => await WithEntityAsync(gen.GenerateProvider(options), p => factory.GeneratePractitionerRole(p, options)),
            "procedure" => await WithEntityAsync(gen.GeneratePatient(options), p => factory.GenerateProcedure(p, options)),
            "allergyintolerance" => await WithEntityAsync(gen.GeneratePatient(options), p => factory.GenerateAllergyIntolerance(p, options)),
            "diagnosticreport" => await WithEntityAsync(gen.GeneratePatient(options), p => factory.GenerateDiagnosticReport(p, options)),
            "medication" => await WithEntityAsync(gen.GenerateMedication(options), m => factory.GenerateMedication(m, options)),
            "medicationdispense" => await WithEntityAsync(gen.GeneratePrescription(options), rx => factory.GenerateMedicationDispense(rx, options)),
            "medicationadministration" => await WithEntityAsync(gen.GeneratePrescription(options), rx => factory.GenerateMedicationAdministration(rx, options)),
            "careplan" => await WithEntityAsync(gen.GeneratePatient(options), p => factory.GenerateCarePlan(p, options)),
            "careteam" => await WithEntityAsync(gen.GeneratePatient(options), p => factory.GenerateCareTeam(p, options)),
            "servicerequest" => await WithEntityAsync(gen.GeneratePatient(options), p => factory.GenerateServiceRequest(p, options)),
            "coverage" => await WithEntityAsync(gen.GeneratePatient(options), p => factory.GenerateCoverage(p, options)),
            "claim" => await WithEntityAsync(gen.GeneratePatient(options), p => factory.GenerateClaim(p, options)),
            "immunization" => await WithEntityAsync(gen.GeneratePatient(options), p => factory.GenerateImmunization(p, options)),
            "documentreference" => await WithEntityAsync(gen.GeneratePatient(options), p => factory.GenerateDocumentReference(p, options)),
            "relatedperson" => await WithEntityAsync(gen.GeneratePatient(options), p => factory.GenerateRelatedPerson(p, options)),

            // Name-picked administrative types.
            "organization" => await GenerateOrganizationResourceAsync(options),
            "location" => await GenerateLocationResourceAsync(options),

            // Bundle
            "bundle" => await GenerateBundleResourceAsync(options),

            // Registry-backed types with no bespoke pipeline (the b188 builder set):
            // a dedicated builder in DI owns the generation; no builder → unsupported.
            _ => await GenerateRegistryBackedResourceAsync(resourceType, options)
        };
    }

    private async Task<Result<string>> GenerateOrganizationResourceAsync(GenerationOptions options)
    {
        await Task.Yield();
        var random = GenerationDeterminism.CreateKey(options).Derive("fhir").Derive("organization").AsRandom();
        var orgNames = new[] { "General Hospital", "City Medical Center", "University Health System", "Community Clinic", "Regional Medical Center" };
        return _fhirResourceFactory.GenerateOrganization(orgNames[random.Next(orgNames.Length)], options);
    }

    private async Task<Result<string>> GenerateLocationResourceAsync(GenerationOptions options)
    {
        await Task.Yield();
        var random = GenerationDeterminism.CreateKey(options).Derive("fhir").Derive("location").AsRandom();
        var locations = new[] { "Emergency Department", "ICU", "Medical/Surgical Unit", "Outpatient Clinic", "Operating Room" };
        return _fhirResourceFactory.GenerateLocation(locations[random.Next(locations.Length)], options);
    }

    private async Task<Result<string>> GenerateBundleResourceAsync(GenerationOptions options)
    {
        var bundleAssembler = new FHIRBundleAssembler(_fhirResourceFactory, _domainGenerationService);

        // Check context for bundle type
        if (options.Context.TryGetValue("bundleType", out var bundleTypeObj) &&
            bundleTypeObj is string bundleTypeStr &&
            bundleTypeStr.Equals("searchset", StringComparison.OrdinalIgnoreCase))
        {
            return await bundleAssembler.AssembleSearchsetBundleAsync("Patient", 3, options);
        }

        return await bundleAssembler.AssembleTransactionBundleAsync(options);
    }

    /// <summary>
    /// Generates a bundle of the specified type with resources.
    /// Used by external callers needing specific bundle types.
    /// </summary>
    public async Task<Result<string>> GenerateBundleAsync(FHIRBundleType bundleType, GenerationOptions options, int resourceCount = 5)
    {
        var bundleAssembler = new FHIRBundleAssembler(_fhirResourceFactory, _domainGenerationService);
        return bundleType switch
        {
            FHIRBundleType.Transaction => await bundleAssembler.AssembleTransactionBundleAsync(options),
            FHIRBundleType.Searchset => await bundleAssembler.AssembleSearchsetBundleAsync("Patient", resourceCount, options),
            _ => await bundleAssembler.AssembleTransactionBundleAsync(options)
        };
    }
}
