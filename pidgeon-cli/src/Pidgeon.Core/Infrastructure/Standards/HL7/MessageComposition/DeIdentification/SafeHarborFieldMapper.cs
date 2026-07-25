// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.DeIdentification;

/// <summary>
/// Maps HL7 v2.3 fields to HIPAA Safe Harbor identifiers.
/// Provides the authoritative mapping of which fields contain PHI and how they should be handled.
/// </summary>
public class SafeHarborFieldMapper
{
    private readonly Dictionary<string, PhiFieldMapping> _fieldMappings;

    public SafeHarborFieldMapper()
    {
        _fieldMappings = CreateStandardFieldMappings();
    }

    /// <summary>
    /// Gets PHI field mappings for a specific segment type.
    /// </summary>
    /// <param name="segmentType">HL7 segment type (MSH, PID, etc.)</param>
    /// <returns>Dictionary of field paths to PHI mappings</returns>
    public Dictionary<string, PhiFieldMapping> GetPhiFieldsForSegment(string segmentType)
    {
        return _fieldMappings
            .Where(kvp => kvp.Key.StartsWith($"{segmentType}."))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Gets the PHI mapping for a specific field path.
    /// </summary>
    /// <param name="fieldPath">Field path (e.g., "PID.5")</param>
    /// <returns>PHI mapping if field contains PHI, null otherwise</returns>
    public PhiFieldMapping? GetFieldMapping(string fieldPath)
    {
        return _fieldMappings.TryGetValue(fieldPath, out var mapping) ? mapping : null;
    }

    /// <summary>
    /// Checks if a field path contains PHI according to HIPAA Safe Harbor.
    /// </summary>
    /// <param name="fieldPath">Field path to check</param>
    /// <returns>True if field contains PHI</returns>
    public bool IsPhiField(string fieldPath)
    {
        return _fieldMappings.ContainsKey(fieldPath);
    }

    /// <summary>
    /// Gets all PHI field mappings.
    /// </summary>
    /// <returns>Complete dictionary of PHI field mappings</returns>
    public IReadOnlyDictionary<string, PhiFieldMapping> GetAllMappings()
    {
        return _fieldMappings.AsReadOnly();
    }

    /// <summary>
    /// Creates the standard HIPAA Safe Harbor field mappings for HL7 v2.3.
    /// Based on the 18 required identifiers from 45 CFR §164.514(b)(2).
    /// </summary>
    private static Dictionary<string, PhiFieldMapping> CreateStandardFieldMappings()
    {
        var mappings = new Dictionary<string, PhiFieldMapping>();

        // MSH - Message Header
        AddMapping(mappings, "MSH.3", 14, IdentifierType.Other, "Sending Application");
        AddMapping(mappings, "MSH.4", 14, IdentifierType.Other, "Sending Facility");
        AddMapping(mappings, "MSH.5", 14, IdentifierType.Other, "Receiving Application");
        AddMapping(mappings, "MSH.6", 14, IdentifierType.Other, "Receiving Facility");

        // PID - Patient Identification
        AddMapping(mappings, "PID.3", 8, IdentifierType.MedicalRecordNumber, "Patient Identifier List (MRN)");
        AddMapping(mappings, "PID.4", 8, IdentifierType.Other, "Alternate Patient ID");
        AddMapping(mappings, "PID.5", 1, IdentifierType.PatientName, "Patient Name");
        AddMapping(mappings, "PID.6", 1, IdentifierType.PatientName, "Mother's Maiden Name");
        AddMapping(mappings, "PID.7", 3, IdentifierType.Date, "Date/Time of Birth");
        AddMapping(mappings, "PID.9", 1, IdentifierType.Other, "Patient Alias");
        AddMapping(mappings, "PID.11", 2, IdentifierType.Address, "Patient Address");
        AddMapping(mappings, "PID.12", 2, IdentifierType.Other, "County Code");
        AddMapping(mappings, "PID.13", 4, IdentifierType.PhoneNumber, "Phone Number - Home");
        AddMapping(mappings, "PID.14", 4, IdentifierType.PhoneNumber, "Phone Number - Business");
        AddMapping(mappings, "PID.18", 10, IdentifierType.AccountNumber, "Patient Account Number");
        AddMapping(mappings, "PID.19", 7, IdentifierType.SocialSecurityNumber, "SSN Number - Patient");
        AddMapping(mappings, "PID.20", 11, IdentifierType.LicenseNumber, "Driver's License Number");
        AddMapping(mappings, "PID.21", 1, IdentifierType.Other, "Mother's Identifier");
        AddMapping(mappings, "PID.23", 2, IdentifierType.Other, "Birth Place");

        // NK1 - Next of Kin/Associated Parties
        AddMapping(mappings, "NK1.2", 1, IdentifierType.PatientName, "Name");
        AddMapping(mappings, "NK1.4", 2, IdentifierType.Address, "Address");
        AddMapping(mappings, "NK1.5", 4, IdentifierType.PhoneNumber, "Phone Number");
        AddMapping(mappings, "NK1.6", 4, IdentifierType.PhoneNumber, "Business Phone Number");
        AddMapping(mappings, "NK1.12", 7, IdentifierType.SocialSecurityNumber, "Next of Kin/Associated Party's SSN");
        AddMapping(mappings, "NK1.33", 1, IdentifierType.Other, "Next of Kin/Associated Party Identifiers");

        // PV1 - Patient Visit
        AddMapping(mappings, "PV1.7", 1, IdentifierType.ProviderName, "Attending Doctor");
        AddMapping(mappings, "PV1.8", 1, IdentifierType.ProviderName, "Referring Doctor");
        AddMapping(mappings, "PV1.9", 1, IdentifierType.ProviderName, "Consulting Doctor");
        AddMapping(mappings, "PV1.17", 1, IdentifierType.ProviderName, "Admitting Doctor");
        AddMapping(mappings, "PV1.52", 1, IdentifierType.Other, "Other Healthcare Provider");

        // PV2 - Patient Visit Additional Information
        AddMapping(mappings, "PV2.23", 1, IdentifierType.ProviderName, "Clinic Organization Name");

        // OBR - Observation Request
        AddMapping(mappings, "OBR.16", 1, IdentifierType.ProviderName, "Ordering Provider");
        AddMapping(mappings, "OBR.28", 1, IdentifierType.ProviderName, "Result Copies To");
        AddMapping(mappings, "OBR.32", 1, IdentifierType.ProviderName, "Principal Result Interpreter");
        AddMapping(mappings, "OBR.33", 1, IdentifierType.ProviderName, "Assistant Result Interpreter");
        AddMapping(mappings, "OBR.34", 1, IdentifierType.ProviderName, "Technician");
        AddMapping(mappings, "OBR.35", 1, IdentifierType.ProviderName, "Transcriptionist");

        // OBX - Observation/Result
        AddMapping(mappings, "OBX.16", 1, IdentifierType.ProviderName, "Responsible Observer");

        // GT1 - Guarantor
        AddMapping(mappings, "GT1.3", 1, IdentifierType.PatientName, "Guarantor Name");
        AddMapping(mappings, "GT1.5", 2, IdentifierType.Address, "Guarantor Address");
        AddMapping(mappings, "GT1.6", 4, IdentifierType.PhoneNumber, "Guarantor Ph Num - Home");
        AddMapping(mappings, "GT1.7", 4, IdentifierType.PhoneNumber, "Guarantor Ph Num - Business");
        AddMapping(mappings, "GT1.12", 7, IdentifierType.SocialSecurityNumber, "Guarantor SSN");
        AddMapping(mappings, "GT1.15", 2, IdentifierType.Other, "Guarantor Employer Name");
        AddMapping(mappings, "GT1.16", 2, IdentifierType.Address, "Guarantor Employer Address");
        AddMapping(mappings, "GT1.17", 4, IdentifierType.PhoneNumber, "Guarantor Employer Phone Number");
        AddMapping(mappings, "GT1.18", 1, IdentifierType.Other, "Guarantor Employee ID Number");
        AddMapping(mappings, "GT1.19", 2, IdentifierType.Other, "Guarantor Organization Name");

        // IN1 - Insurance
        AddMapping(mappings, "IN1.3", 9, IdentifierType.InsuranceId, "Insurance Company ID");
        AddMapping(mappings, "IN1.4", 1, IdentifierType.Other, "Insurance Company Name");
        AddMapping(mappings, "IN1.5", 2, IdentifierType.Address, "Insurance Company Address");
        AddMapping(mappings, "IN1.7", 4, IdentifierType.PhoneNumber, "Insurance Co Phone Number");
        AddMapping(mappings, "IN1.12", 3, IdentifierType.Date, "Plan Effective Date");
        AddMapping(mappings, "IN1.13", 3, IdentifierType.Date, "Plan Expiration Date");
        AddMapping(mappings, "IN1.16", 1, IdentifierType.PatientName, "Name Of Insured");
        AddMapping(mappings, "IN1.17", 1, IdentifierType.Other, "Insured's Relationship To Patient");
        AddMapping(mappings, "IN1.18", 3, IdentifierType.Date, "Insured's Date Of Birth");
        AddMapping(mappings, "IN1.19", 2, IdentifierType.Address, "Insured's Address");
        AddMapping(mappings, "IN1.36", 9, IdentifierType.InsuranceId, "Policy Number");
        AddMapping(mappings, "IN1.49", 9, IdentifierType.InsuranceId, "Insured's ID Number");

        // IN2 - Insurance Additional Information
        AddMapping(mappings, "IN2.1", 1, IdentifierType.Other, "Insured's Employee ID");
        AddMapping(mappings, "IN2.2", 7, IdentifierType.SocialSecurityNumber, "Insured's Social Security Number");
        AddMapping(mappings, "IN2.3", 1, IdentifierType.PatientName, "Insured's Employer's Name And ID");
        AddMapping(mappings, "IN2.4", 2, IdentifierType.Address, "Employer Information Data");
        AddMapping(mappings, "IN2.5", 2, IdentifierType.Other, "Mail Claim Party");
        AddMapping(mappings, "IN2.7", 1, IdentifierType.Other, "Medicaid Case Name");
        AddMapping(mappings, "IN2.8", 9, IdentifierType.InsuranceId, "Medicaid Case Number");
        AddMapping(mappings, "IN2.9", 1, IdentifierType.Other, "Military Sponsor Name");
        AddMapping(mappings, "IN2.10", 9, IdentifierType.Other, "Military ID Number");
        AddMapping(mappings, "IN2.25", 1, IdentifierType.Other, "Payor Subscriber ID");
        AddMapping(mappings, "IN2.61", 1, IdentifierType.PatientName, "Patient Member Number");
        AddMapping(mappings, "IN2.63", 1, IdentifierType.PatientName, "Dependent Of Military Recipient");

        // ROL - Role
        AddMapping(mappings, "ROL.4", 1, IdentifierType.ProviderName, "Role Person");

        // DB1 - Disability
        AddMapping(mappings, "DB1.5", 3, IdentifierType.Date, "Disability Start Date");
        AddMapping(mappings, "DB1.6", 3, IdentifierType.Date, "Disability End Date");

        // DG1 - Diagnosis
        AddMapping(mappings, "DG1.16", 1, IdentifierType.ProviderName, "Diagnosing Clinician");

        // PR1 - Procedures
        AddMapping(mappings, "PR1.11", 1, IdentifierType.ProviderName, "Surgeon");
        AddMapping(mappings, "PR1.12", 1, IdentifierType.ProviderName, "Procedure Practitioner");

        // ORC - Common Order
        AddMapping(mappings, "ORC.10", 1, IdentifierType.ProviderName, "Entered By");
        AddMapping(mappings, "ORC.11", 1, IdentifierType.ProviderName, "Verified By");
        AddMapping(mappings, "ORC.12", 1, IdentifierType.ProviderName, "Ordering Provider");
        AddMapping(mappings, "ORC.13", 2, IdentifierType.Other, "Enterer's Location");
        AddMapping(mappings, "ORC.14", 4, IdentifierType.PhoneNumber, "Call Back Phone Number");
        AddMapping(mappings, "ORC.19", 1, IdentifierType.ProviderName, "Action By");

        // RXE - Pharmacy/Treatment Encoded Order
        AddMapping(mappings, "RXE.14", 1, IdentifierType.ProviderName, "Pharmacist/Treatment Supplier's Verifier ID");
        AddMapping(mappings, "RXE.28", 2, IdentifierType.Address, "Deliver-To Location");

        // RXD - Pharmacy/Treatment Dispense
        AddMapping(mappings, "RXD.10", 1, IdentifierType.ProviderName, "Dispensing Provider");

        // RXA - Pharmacy/Treatment Administration
        AddMapping(mappings, "RXA.10", 1, IdentifierType.ProviderName, "Administering Provider");

        // Additional fields that may contain web URLs or device identifiers
        AddNarrativeFields(mappings);
        AddFieldsForDeviceIdentifiers(mappings);

        return mappings;
    }

    /// <summary>
    /// Adds mapping for a specific field.
    /// </summary>
    private static void AddMapping(
        Dictionary<string, PhiFieldMapping> mappings,
        string fieldPath,
        int hipaaCategory,
        IdentifierType identifierType,
        string description)
    {
        mappings[fieldPath] = new PhiFieldMapping
        {
            HipaaCategory = hipaaCategory,
            IdentifierType = identifierType,
            RequiresRemoval = true,
            Description = description
        };
    }

    /// <summary>
    /// Marks HL7 narrative fields (OBX-5, NTE-3, TXA-12) for free-text scan-and-scrub: their PHI spans
    /// are scrubbed individually and the clinical prose is retained, rather than whole-field tokenized.
    /// </summary>
    private static void AddNarrativeFields(Dictionary<string, PhiFieldMapping> mappings)
    {
        // Narrative fields scanned span-by-span (names, SSNs, dates, phones, URLs, IPs, addresses).
        var narrativeFields = new[]
        {
            "OBX.5",  // Observation Value (free-text results)
            "NTE.3",  // Notes and Comments
            "TXA.12"  // Transcription document content
        };

        foreach (var field in narrativeFields)
        {
            mappings[field] = new PhiFieldMapping
            {
                HipaaCategory = 18,
                IdentifierType = IdentifierType.FreeText,
                RequiresRemoval = true,
                Description = "Free-text narrative (scan-and-scrub)"
            };
        }
    }

    /// <summary>
    /// Adds mappings for fields that may contain device identifiers (HIPAA category 13).
    /// </summary>
    private static void AddFieldsForDeviceIdentifiers(Dictionary<string, PhiFieldMapping> mappings)
    {
        // Fields that commonly contain device or equipment identifiers
        var deviceFields = new[]
        {
            "OBR.18", // Placer Field 1 (may contain equipment ID)
            "OBR.19", // Placer Field 2 (may contain equipment ID)
            "OBX.18", // Equipment Instance Identifier
            "SAC.3",  // Container Identifier
            "EQU.1",  // Equipment Instance Identifier
            "EQU.2"   // Event Date/Time
        };

        foreach (var field in deviceFields)
        {
            mappings[field] = new PhiFieldMapping
            {
                HipaaCategory = 13,
                IdentifierType = IdentifierType.DeviceId,
                RequiresRemoval = true,
                Description = "Device identifiers and serial numbers"
            };
        }
    }

    /// <summary>
    /// Gets a summary of HIPAA Safe Harbor categories covered by the mappings.
    /// </summary>
    /// <returns>Dictionary mapping HIPAA categories to their descriptions and field count</returns>
    public Dictionary<int, (string Description, int FieldCount)> GetHipaaCategorySummary()
    {
        var categorySummary = new Dictionary<int, (string Description, int FieldCount)>
        {
            { 1, ("Names", 0) },
            { 2, ("Geographic subdivisions smaller than state", 0) },
            { 3, ("Date elements directly related to individual", 0) },
            { 4, ("Phone numbers", 0) },
            { 5, ("Fax numbers", 0) },
            { 6, ("Email addresses", 0) },
            { 7, ("Social security numbers", 0) },
            { 8, ("Medical record numbers", 0) },
            { 9, ("Health plan beneficiary numbers", 0) },
            { 10, ("Account numbers", 0) },
            { 11, ("Certificate/license numbers", 0) },
            { 12, ("Vehicle identifiers and serial numbers", 0) },
            { 13, ("Device identifiers and serial numbers", 0) },
            { 14, ("Web URLs", 0) },
            { 15, ("IP addresses", 0) },
            { 16, ("Biometric identifiers", 0) },
            { 17, ("Full-face photographs", 0) },
            { 18, ("Any other unique identifying number, characteristic, or code", 0) }
        };

        // Count fields by category
        foreach (var mapping in _fieldMappings.Values)
        {
            if (categorySummary.ContainsKey(mapping.HipaaCategory))
            {
                var (description, count) = categorySummary[mapping.HipaaCategory];
                categorySummary[mapping.HipaaCategory] = (description, count + 1);
            }
        }

        return categorySummary;
    }
}