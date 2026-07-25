// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Domain.Messaging.FHIR.Bundles;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4;

/// <summary>
/// Thin dispatcher over <see cref="FHIRResourceBuilderRegistry"/>. Each
/// <see cref="IFHIRResourceFactory"/> method resolves the matching
/// <see cref="IFHIRResourceBuilder"/> (Patient, Observation, ...), builds
/// a fresh <see cref="FHIRBuildContext"/>, and runs the builder
/// synchronously — the interface surface stays sync <c>Result&lt;string&gt;</c>
/// so callers in <c>FHIRBundleAssembler</c>, <c>FHIRSearchHarnessService</c>,
/// and the Flock FHIR generator keep working unchanged.
///
/// All resource generation logic lives in dedicated per-resource builders
/// under <c>Infrastructure/Standards/FHIR/R4/Builders/</c>.
///
/// No scenario coordinator is threaded through this path. Callers that want
/// scenario-coherent FHIR generation go through
/// <c>FHIRMessageGenerationPlugin</c>, which injects a scoped
/// <c>ClinicalScenarioCoordinator</c> into the build context. The factory
/// path stays coordinator-free because it serves resource-at-a-time callers
/// that have no scenario scope.
/// </summary>
internal class FHIRResourceFactory : IFHIRResourceFactory
{
    private readonly FHIRResourceBuilderRegistry _builderRegistry;

    public FHIRResourceFactory(FHIRResourceBuilderRegistry builderRegistry)
    {
        _builderRegistry = builderRegistry ?? throw new ArgumentNullException(nameof(builderRegistry));
    }

    public Result<string> GeneratePatient(Patient patient, GenerationOptions options) =>
        Dispatch("Patient", patient, options);

    public Result<string> GeneratePractitioner(Provider provider, GenerationOptions options) =>
        Dispatch("Practitioner", provider, options);

    public Result<string> GenerateObservation(ObservationResult observation, GenerationOptions options) =>
        Dispatch("Observation", observation, options);

    public Result<string> GenerateBundle(IReadOnlyList<FHIRResource> resources, FHIRBundleType bundleType, GenerationOptions options) =>
        Dispatch("Bundle", new BundleBuildInput(resources, bundleType), options);

    public Result<string> GenerateEncounter(Encounter encounter, GenerationOptions options) =>
        Dispatch("Encounter", encounter, options);

    public Result<string> GenerateOrganization(string organizationName, GenerationOptions options) =>
        Dispatch("Organization", organizationName, options);

    public Result<string> GenerateLocation(string locationName, GenerationOptions options) =>
        Dispatch("Location", locationName, options);

    public Result<string> GenerateMedication(Medication medication, GenerationOptions options) =>
        Dispatch("Medication", medication, options);

    public Result<string> GenerateMedicationRequest(Prescription prescription, GenerationOptions options) =>
        Dispatch("MedicationRequest", prescription, options);

    public Result<string> GenerateCondition(Patient patient, GenerationOptions options) =>
        Dispatch("Condition", patient, options);

    public Result<string> GenerateProcedure(Patient patient, GenerationOptions options) =>
        Dispatch("Procedure", patient, options);

    public Result<string> GenerateAllergyIntolerance(Patient patient, GenerationOptions options) =>
        Dispatch("AllergyIntolerance", patient, options);

    public Result<string> GenerateDiagnosticReport(Patient patient, GenerationOptions options) =>
        Dispatch("DiagnosticReport", patient, options);

    public Result<string> GenerateCoverage(Patient patient, GenerationOptions options) =>
        Dispatch("Coverage", patient, options);

    public Result<string> GenerateImmunization(Patient patient, GenerationOptions options) =>
        Dispatch("Immunization", patient, options);

    public Result<string> GenerateMedicationDispense(Prescription prescription, GenerationOptions options) =>
        Dispatch("MedicationDispense", prescription, options);

    public Result<string> GenerateServiceRequest(Patient patient, GenerationOptions options) =>
        Dispatch("ServiceRequest", patient, options);

    public Result<string> GenerateDocumentReference(Patient patient, GenerationOptions options) =>
        Dispatch("DocumentReference", patient, options);

    public Result<string> GenerateCarePlan(Patient patient, GenerationOptions options) =>
        Dispatch("CarePlan", patient, options);

    public Result<string> GeneratePractitionerRole(Provider provider, GenerationOptions options) =>
        Dispatch("PractitionerRole", provider, options);

    public Result<string> GenerateRelatedPerson(Patient patient, GenerationOptions options) =>
        Dispatch("RelatedPerson", patient, options);

    public Result<string> GenerateClaim(Patient patient, GenerationOptions options) =>
        Dispatch("Claim", patient, options);

    public Result<string> GenerateCareTeam(Patient patient, GenerationOptions options) =>
        Dispatch("CareTeam", patient, options);

    public Result<string> GenerateMedicationAdministration(Prescription prescription, GenerationOptions options) =>
        Dispatch("MedicationAdministration", prescription, options);

    /// <summary>
    /// Resolve the dedicated builder for <paramref name="resourceType"/> and
    /// run it synchronously. Builders in this codebase return
    /// <c>Task.FromResult(...)</c> without real async awaiting when no
    /// scenario coordinator is attached, so <c>GetAwaiter().GetResult()</c>
    /// is safe on the sync factory path.
    /// </summary>
    private Result<string> Dispatch(string resourceType, object? domainEntity, GenerationOptions options)
    {
        var builder = _builderRegistry.GetBuilder(resourceType);
        if (builder == null)
        {
            return Result<string>.Failure(
                $"No IFHIRResourceBuilder registered for resource type '{resourceType}'. "
                + "Add one under Infrastructure/Standards/FHIR/R4/Builders/ — it will auto-register via "
                + "ServiceRegistrationExtensions.AddFHIRResourceBuilders.");
        }

        var context = new FHIRBuildContext
        {
            DomainEntity = domainEntity,
            Options = options,
            References = new FHIRReferenceMap(),
            ScenarioCoordinator = null,
            Key = GenerationDeterminism.CreateKey(options).Derive("fhir").Derive(resourceType.ToLowerInvariant())
        };

        return builder.BuildAsync(context).GetAwaiter().GetResult();
    }
}
