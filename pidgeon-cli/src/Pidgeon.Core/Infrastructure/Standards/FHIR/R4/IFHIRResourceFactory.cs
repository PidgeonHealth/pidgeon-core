// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Domain.Messaging.FHIR.Bundles;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4;

/// <summary>
/// Factory interface for generating FHIR R4 standards-compliant resources.
/// Follows HL7 FHIR R4 specification for resource structure and content requirements.
/// </summary>
public interface IFHIRResourceFactory
{
    Result<string> GeneratePatient(Patient patient, GenerationOptions options);
    Result<string> GeneratePractitioner(Provider provider, GenerationOptions options);
    Result<string> GenerateObservation(ObservationResult observation, GenerationOptions options);
    Result<string> GenerateBundle(IReadOnlyList<FHIRResource> resources, FHIRBundleType bundleType, GenerationOptions options);
    Result<string> GenerateEncounter(Encounter encounter, GenerationOptions options);
    Result<string> GenerateOrganization(string organizationName, GenerationOptions options);
    Result<string> GenerateLocation(string locationName, GenerationOptions options);
    Result<string> GenerateMedication(Medication medication, GenerationOptions options);
    Result<string> GenerateMedicationRequest(Prescription prescription, GenerationOptions options);
    Result<string> GenerateCondition(Patient patient, GenerationOptions options);
    Result<string> GenerateProcedure(Patient patient, GenerationOptions options);
    Result<string> GenerateAllergyIntolerance(Patient patient, GenerationOptions options);
    Result<string> GenerateDiagnosticReport(Patient patient, GenerationOptions options);
    Result<string> GenerateCoverage(Patient patient, GenerationOptions options);
    Result<string> GenerateImmunization(Patient patient, GenerationOptions options);
    Result<string> GenerateMedicationDispense(Prescription prescription, GenerationOptions options);
    Result<string> GenerateServiceRequest(Patient patient, GenerationOptions options);
    Result<string> GenerateDocumentReference(Patient patient, GenerationOptions options);
    Result<string> GenerateCarePlan(Patient patient, GenerationOptions options);
    Result<string> GeneratePractitionerRole(Provider provider, GenerationOptions options);
    Result<string> GenerateRelatedPerson(Patient patient, GenerationOptions options);
    Result<string> GenerateClaim(Patient patient, GenerationOptions options);
    Result<string> GenerateCareTeam(Patient patient, GenerationOptions options);
    Result<string> GenerateMedicationAdministration(Prescription prescription, GenerationOptions options);
}

/// <summary>
/// Represents a FHIR resource for Bundle composition.
/// </summary>
public record FHIRResource
{
    public required string ResourceType { get; init; }
    public required string Id { get; init; }
    public required string JsonContent { get; init; }
}
