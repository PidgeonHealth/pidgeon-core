// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Pidgeon.Core.Domain.Messaging;

/// <summary>
/// The message-type → standard map: "ORU^R01 is HL7", "Patient is FHIR", "NewRx is NCPDP".
/// This is domain knowledge about the standards themselves, so it lives in Domain alongside
/// <see cref="StandardNames"/> rather than in a generation service — letting the agent/judge
/// layer infer a standard without reaching across into <c>Application.Services.Generation</c>.
///
/// The returned tokens are the lowercase machine tokens defined by <see cref="StandardNames"/>
/// (<c>"hl7"</c>/<c>"fhir"</c>/<c>"ncpdp"</c>). Surfacing a new message type is a data change in
/// the table below; surfacing a new standard adds its token to <see cref="StandardNames"/>.
/// </summary>
public static class MessageTypeVocabulary
{
    private static readonly Dictionary<string, string> _exactMatches = new(StringComparer.OrdinalIgnoreCase)
    {
        // HL7 v2 Messages - ADT (Admit/Discharge/Transfer)
        ["ADT^A01"] = StandardNames.Hl7, // Admit/visit notification
        ["ADT^A02"] = StandardNames.Hl7, // Transfer a patient
        ["ADT^A03"] = StandardNames.Hl7, // Discharge/end visit
        ["ADT^A04"] = StandardNames.Hl7, // Register a patient
        ["ADT^A05"] = StandardNames.Hl7, // Pre-admit a patient
        ["ADT^A06"] = StandardNames.Hl7, // Change an outpatient to an inpatient
        ["ADT^A07"] = StandardNames.Hl7, // Change an inpatient to an outpatient
        ["ADT^A08"] = StandardNames.Hl7, // Update patient information
        ["ADT^A09"] = StandardNames.Hl7, // Patient departing - tracking
        ["ADT^A10"] = StandardNames.Hl7, // Patient arriving - tracking
        ["ADT^A11"] = StandardNames.Hl7, // Cancel admit/visit notification
        ["ADT^A12"] = StandardNames.Hl7, // Cancel transfer
        ["ADT^A13"] = StandardNames.Hl7, // Cancel discharge/end visit
        ["ADT^A14"] = StandardNames.Hl7, // Pending admit
        ["ADT^A15"] = StandardNames.Hl7, // Pending transfer
        ["ADT^A16"] = StandardNames.Hl7, // Pending discharge

        // HL7 v2 Messages - ORU (Observation Result)
        ["ORU^R01"] = StandardNames.Hl7, // Unsolicited transmission of an observation message
        ["ORU^R03"] = StandardNames.Hl7, // Display oriented results, query/unsolicited update
        ["ORU^R04"] = StandardNames.Hl7, // Response to query; transmission of requested observation

        // HL7 v2 Messages - ORM/RDE (Order Entry)
        ["ORM^O01"] = StandardNames.Hl7, // Order message
        ["ORM^O02"] = StandardNames.Hl7, // Order response (general order acknowledgment)
        ["ORM^O03"] = StandardNames.Hl7, // Diet order
        ["RDE^O11"] = StandardNames.Hl7, // Pharmacy/treatment encoded order
        ["RDE^O25"] = StandardNames.Hl7, // Pharmacy/treatment refill authorization request

        // HL7 v2 Messages - OML (Laboratory Order Entry)
        ["OML^O21"] = StandardNames.Hl7, // Laboratory order

        // HL7 v2 Messages - SIU (Scheduling)
        ["SIU^S12"] = StandardNames.Hl7, // New appointment booking
        ["SIU^S13"] = StandardNames.Hl7, // Appointment rescheduling
        ["SIU^S14"] = StandardNames.Hl7, // Appointment modification
        ["SIU^S15"] = StandardNames.Hl7, // Appointment cancellation

        // HL7 v2 Messages - MDM (Medical Document Management)
        ["MDM^T02"] = StandardNames.Hl7, // Original document notification
        ["MDM^T04"] = StandardNames.Hl7, // Document edit notification
        ["MDM^T06"] = StandardNames.Hl7, // Document addendum notification
        ["MDM^T08"] = StandardNames.Hl7, // Document edit notification

        // HL7 v2 Messages - DFT (Detailed Financial Transaction)
        ["DFT^P03"] = StandardNames.Hl7, // Post detail financial transaction
        ["DFT^P11"] = StandardNames.Hl7, // Post detail financial transaction - expanded

        // HL7 v2 Messages - BAR (Add/Change Billing Account)
        ["BAR^P01"] = StandardNames.Hl7, // Add patient accounts
        ["BAR^P02"] = StandardNames.Hl7, // Purge patient accounts
        ["BAR^P05"] = StandardNames.Hl7, // Update account
        ["BAR^P06"] = StandardNames.Hl7, // End account

        // HL7 v2 Messages - VXU (Vaccination Update)
        ["VXU^V04"] = StandardNames.Hl7, // Vaccination record update

        // HL7 v2 Messages - QRY (Query)
        ["QRY^A19"] = StandardNames.Hl7, // Patient query
        ["QRY^Q02"] = StandardNames.Hl7, // Query for results of previous query

        // HL7 v2 Messages - ACK (Acknowledgment)
        ["ACK"] = StandardNames.Hl7, // General acknowledgment

        // FHIR R4 Resources - Core Patient/Demographics
        ["Patient"] = StandardNames.Fhir,        // Patient demographics and identifiers
        ["Person"] = StandardNames.Fhir,         // Generic person (not necessarily a patient)
        ["RelatedPerson"] = StandardNames.Fhir,  // Person related to the patient (emergency contact, etc.)

        // FHIR R4 Resources - Clinical Observations
        ["Observation"] = StandardNames.Fhir,       // Clinical observations and vital signs
        ["DiagnosticReport"] = StandardNames.Fhir,  // Lab results, imaging reports
        ["Condition"] = StandardNames.Fhir,         // Diagnoses, problems, concerns
        ["AllergyIntolerance"] = StandardNames.Fhir, // Allergies and adverse reactions
        ["FamilyMemberHistory"] = StandardNames.Fhir, // Family history and genetic information

        // FHIR R4 Resources - Medications & Procedures
        ["MedicationRequest"] = StandardNames.Fhir,      // Prescription orders
        ["MedicationAdministration"] = StandardNames.Fhir, // Record of medication given to patient
        ["MedicationDispense"] = StandardNames.Fhir,     // Dispensing of medication by pharmacy
        ["MedicationStatement"] = StandardNames.Fhir,    // Patient's use of medication
        ["Procedure"] = StandardNames.Fhir,              // Procedures performed on patient
        ["ServiceRequest"] = StandardNames.Fhir,         // Orders for services/procedures
        ["ImagingStudy"] = StandardNames.Fhir,           // DICOM imaging studies

        // FHIR R4 Resources - Care Management
        ["Encounter"] = StandardNames.Fhir,      // Patient visits, admissions, episodes
        ["EpisodeOfCare"] = StandardNames.Fhir,  // Care delivery over time period
        ["CareTeam"] = StandardNames.Fhir,       // Care team participants
        ["CarePlan"] = StandardNames.Fhir,       // Care plans and treatment protocols
        ["Goal"] = StandardNames.Fhir,           // Patient care goals
        ["RiskAssessment"] = StandardNames.Fhir, // Risk assessments and predictions

        // FHIR R4 Resources - Administrative
        ["Organization"] = StandardNames.Fhir,     // Healthcare organizations
        ["Location"] = StandardNames.Fhir,         // Physical locations
        ["Practitioner"] = StandardNames.Fhir,     // Healthcare providers
        ["PractitionerRole"] = StandardNames.Fhir, // Provider roles and specialties
        ["HealthcareService"] = StandardNames.Fhir, // Services offered by organization

        // FHIR R4 Resources - Infrastructure
        ["Bundle"] = StandardNames.Fhir,           // Collection of resources
        ["Composition"] = StandardNames.Fhir,      // Clinical documents
        ["DocumentReference"] = StandardNames.Fhir, // Document metadata and links
        ["Device"] = StandardNames.Fhir,           // Medical devices
        ["DeviceRequest"] = StandardNames.Fhir,    // Device use requests
        ["Immunization"] = StandardNames.Fhir,     // Vaccination records

        // FHIR R4 Resources - Scheduling & Workflow
        ["Appointment"] = StandardNames.Fhir,         // Scheduled appointments
        ["AppointmentResponse"] = StandardNames.Fhir, // Appointment responses/confirmations
        ["Schedule"] = StandardNames.Fhir,            // Provider/service schedules
        ["Slot"] = StandardNames.Fhir,                // Available appointment slots
        ["Task"] = StandardNames.Fhir,                // Workflow tasks
        ["Communication"] = StandardNames.Fhir,       // Patient communications
        ["CommunicationRequest"] = StandardNames.Fhir, // Requests for communication
        ["Questionnaire"] = StandardNames.Fhir,        // Structured question sets
        ["QuestionnaireResponse"] = StandardNames.Fhir, // Completed questionnaire answers
        ["ClaimResponse"] = StandardNames.Fhir,        // Adjudication results for a claim
        ["Provenance"] = StandardNames.Fhir,           // Record provenance and attribution
        ["Specimen"] = StandardNames.Fhir,             // Collected specimens
        ["NutritionOrder"] = StandardNames.Fhir,       // Diet and nutrition orders
        ["VisionPrescription"] = StandardNames.Fhir,   // Lens prescriptions
        ["Parameters"] = StandardNames.Fhir,           // Operation parameter lists
        ["Subscription"] = StandardNames.Fhir,         // Server push subscriptions

        // NCPDP SCRIPT Standard - Core Transactions
        ["NewRx"] = StandardNames.Ncpdp,              // New prescription
        ["RxChangeRequest"] = StandardNames.Ncpdp,    // Request to change prescription
        ["RxChangeResponse"] = StandardNames.Ncpdp,   // Response to prescription change request
        ["CancelRx"] = StandardNames.Ncpdp,           // Cancel prescription request
        ["CancelRxResponse"] = StandardNames.Ncpdp,   // Response to prescription cancellation
        ["RxFill"] = StandardNames.Ncpdp,             // Prescription fill notification
        ["RxHistoryRequest"] = StandardNames.Ncpdp,   // Request prescription history
        ["RxHistoryResponse"] = StandardNames.Ncpdp,  // Prescription history data
        ["Verify"] = StandardNames.Ncpdp,             // Verification message
        ["Error"] = StandardNames.Ncpdp,              // Error notification
        ["Status"] = StandardNames.Ncpdp,             // Status notification

        // NCPDP SCRIPT Standard - Prior Authorization
        ["PAInitiationRequest"] = StandardNames.Ncpdp,  // Prior authorization request
        ["PAInitiationResponse"] = StandardNames.Ncpdp, // Prior authorization response

        // NCPDP SCRIPT Standard - Formulary & Benefits
        ["GetMessage"] = StandardNames.Ncpdp,         // Retrieve message request
        ["FormularyRequest"] = StandardNames.Ncpdp,   // Formulary information request
        ["FormularyResponse"] = StandardNames.Ncpdp   // Formulary information response
    };

    /// <summary>
    /// The 145 concrete FHIR R4 (4.0.1) resource type names as published in the base
    /// specification's resource index (spec-constant domain data, CC0). Abstract types
    /// (Resource, DomainResource) are deliberately absent. <see cref="InferStandard"/>
    /// falls back to this set after the curated exact matches, and the FHIR generation
    /// plugin advertises against it, so any R4 resource name routes to the FHIR
    /// standard — whether it resolves to a hand-built builder, the structural
    /// synthesizer, or an honest unsupported/package-required error is decided by
    /// the generation dispatch, not by this vocabulary.
    /// </summary>
    public static readonly IReadOnlyCollection<string> FhirR4ResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Account", "ActivityDefinition", "AdverseEvent", "AllergyIntolerance", "Appointment",
        "AppointmentResponse", "AuditEvent", "Basic", "Binary", "BiologicallyDerivedProduct",
        "BodyStructure", "Bundle", "CapabilityStatement", "CarePlan", "CareTeam", "CatalogEntry",
        "ChargeItem", "ChargeItemDefinition", "Claim", "ClaimResponse", "ClinicalImpression",
        "CodeSystem", "Communication", "CommunicationRequest", "CompartmentDefinition",
        "Composition", "ConceptMap", "Condition", "Consent", "Contract", "Coverage",
        "CoverageEligibilityRequest", "CoverageEligibilityResponse", "DetectedIssue", "Device",
        "DeviceDefinition", "DeviceMetric", "DeviceRequest", "DeviceUseStatement",
        "DiagnosticReport", "DocumentManifest", "DocumentReference", "EffectEvidenceSynthesis",
        "Encounter", "Endpoint", "EnrollmentRequest", "EnrollmentResponse", "EpisodeOfCare",
        "EventDefinition", "Evidence", "EvidenceVariable", "ExampleScenario",
        "ExplanationOfBenefit", "FamilyMemberHistory", "Flag", "Goal", "GraphDefinition",
        "Group", "GuidanceResponse", "HealthcareService", "ImagingStudy", "Immunization",
        "ImmunizationEvaluation", "ImmunizationRecommendation", "ImplementationGuide",
        "InsurancePlan", "Invoice", "Library", "Linkage", "List", "Location", "Measure",
        "MeasureReport", "Media", "Medication", "MedicationAdministration", "MedicationDispense",
        "MedicationKnowledge", "MedicationRequest", "MedicationStatement", "MedicinalProduct",
        "MedicinalProductAuthorization", "MedicinalProductContraindication",
        "MedicinalProductIndication", "MedicinalProductIngredient", "MedicinalProductInteraction",
        "MedicinalProductManufactured", "MedicinalProductPackaged", "MedicinalProductPharmaceutical",
        "MedicinalProductUndesirableEffect", "MessageDefinition", "MessageHeader",
        "MolecularSequence", "NamingSystem", "NutritionOrder", "Observation",
        "ObservationDefinition", "OperationDefinition", "OperationOutcome", "Organization",
        "OrganizationAffiliation", "Parameters", "Patient", "PaymentNotice",
        "PaymentReconciliation", "Person", "PlanDefinition", "Practitioner", "PractitionerRole",
        "Procedure", "Provenance", "Questionnaire", "QuestionnaireResponse", "RelatedPerson",
        "RequestGroup", "ResearchDefinition", "ResearchElementDefinition", "ResearchStudy",
        "ResearchSubject", "RiskAssessment", "RiskEvidenceSynthesis", "Schedule",
        "SearchParameter", "ServiceRequest", "Slot", "Specimen", "SpecimenDefinition",
        "StructureDefinition", "StructureMap", "Subscription", "Substance",
        "SubstanceNucleicAcid", "SubstancePolymer", "SubstanceProtein",
        "SubstanceReferenceInformation", "SubstanceSourceMaterial", "SubstanceSpecification",
        "SupplyDelivery", "SupplyRequest", "Task", "TerminologyCapabilities", "TestReport",
        "TestScript", "ValueSet", "VerificationResult", "VisionPrescription",
    };

    private static readonly Regex _hl7Pattern = new(@"^[A-Z]{3}\^[A-Z]\d{2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Infers the healthcare standard from a message type (case-insensitive).
    /// </summary>
    /// <param name="messageType">Message type (e.g., ADT^A01, ADT-A01, Patient, NewRx, adt^a01, patient, newrx)</param>
    /// <returns>Standard token (<see cref="StandardNames.Hl7"/>/<see cref="StandardNames.Fhir"/>/<see cref="StandardNames.Ncpdp"/>) or null if cannot be inferred</returns>
    public static string? InferStandard(string messageType)
    {
        if (string.IsNullOrWhiteSpace(messageType))
            return null;

        // Normalize shell-safe alternatives to canonical format
        var normalizedMessageType = Normalize(messageType);

        // Try exact match first (case-insensitive dictionary handles this)
        if (_exactMatches.TryGetValue(normalizedMessageType, out var standard))
            return standard;

        // Any remaining concrete R4 resource name is FHIR (the curated table above
        // keeps its per-type notes; this fallback covers the structural-tier long tail).
        if (FhirR4ResourceTypes.Contains(normalizedMessageType))
            return StandardNames.Fhir;

        // Try pattern matching for HL7 (case-insensitive regex handles custom message types)
        if (_hl7Pattern.IsMatch(normalizedMessageType))
            return StandardNames.Hl7;

        // No inference possible
        return null;
    }

    /// <summary>
    /// Normalizes shell-safe message type alternatives to canonical HL7 format.
    /// Converts ADT-A01, ADT_A01, ADT.A01 → ADT^A01
    /// </summary>
    /// <param name="messageType">Message type to normalize (e.g., ADT-A01, ADT_A01, ADT.A01)</param>
    /// <returns>Normalized message type (e.g., ADT^A01)</returns>
    public static string Normalize(string messageType)
    {
        // Handle shell-safe alternatives: ADT-A01, ADT_A01, ADT.A01 → ADT^A01
        return messageType
            .Replace("-", "^")
            .Replace("_", "^")
            .Replace(".", "^");
    }

    /// <summary>
    /// Validates that a message type is supported for the given standard.
    /// </summary>
    /// <param name="messageType">Message type to validate</param>
    /// <param name="standard">Target standard</param>
    /// <returns>True if the combination is valid</returns>
    public static bool IsValidForStandard(string messageType, string standard)
    {
        if (string.IsNullOrWhiteSpace(messageType) || string.IsNullOrWhiteSpace(standard))
            return false;

        var inferredStandard = InferStandard(messageType);
        return string.Equals(inferredStandard, standard, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets all supported message types for a standard.
    /// </summary>
    /// <param name="standard">Standard token (hl7, fhir, ncpdp)</param>
    /// <returns>List of supported message types</returns>
    public static IEnumerable<string> GetMessageTypesForStandard(string standard)
    {
        if (string.IsNullOrWhiteSpace(standard))
            return Enumerable.Empty<string>();

        return _exactMatches
            .Where(kvp => string.Equals(kvp.Value, standard, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .OrderBy(type => type);
    }

    /// <summary>
    /// Gets all supported standards.
    /// </summary>
    /// <returns>List of supported standard tokens</returns>
    public static IEnumerable<string> GetSupportedStandards()
    {
        return _exactMatches.Values.Distinct().OrderBy(s => s);
    }
}
