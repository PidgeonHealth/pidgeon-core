// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.DeIdentification;

/// <summary>
/// Maps FHIR R4 resource fields to HIPAA Safe Harbor identifier categories.
/// Provides field-level classification for de-identification of FHIR JSON resources.
/// </summary>
public class FHIRSafeHarborFieldMapper
{
    private readonly Dictionary<string, FHIRPhiFieldMapping> _mappings;

    public FHIRSafeHarborFieldMapper()
    {
        _mappings = BuildMappings();
    }

    /// <summary>
    /// Gets the PHI field mapping for a given resource type and JSON path.
    /// </summary>
    public FHIRPhiFieldMapping? GetFieldMapping(string resourceType, string jsonPath)
    {
        var key = $"{resourceType}.{jsonPath}";
        return _mappings.TryGetValue(key, out var mapping) ? mapping : null;
    }

    /// <summary>
    /// Gets all PHI field mappings for a given resource type.
    /// </summary>
    public Dictionary<string, FHIRPhiFieldMapping> GetPhiFieldsForResourceType(string resourceType)
    {
        return _mappings
            .Where(kvp => kvp.Key.StartsWith($"{resourceType}.", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Gets all field mappings across all resource types.
    /// </summary>
    public IReadOnlyDictionary<string, FHIRPhiFieldMapping> GetAllMappings() => _mappings;

    /// <summary>
    /// Returns true if the given resource type and path is a known PHI field.
    /// </summary>
    public bool IsPhiField(string resourceType, string jsonPath)
    {
        return _mappings.ContainsKey($"{resourceType}.{jsonPath}");
    }

    /// <summary>
    /// Returns the set of resource types that contain PHI fields requiring de-identification.
    /// </summary>
    public static HashSet<string> GetPhiResourceTypes() => new()
    {
        "Patient", "Practitioner", "RelatedPerson", "Person"
    };

    private static Dictionary<string, FHIRPhiFieldMapping> BuildMappings()
    {
        var mappings = new Dictionary<string, FHIRPhiFieldMapping>();

        // Patient resource PHI fields
        AddMapping(mappings, "Patient", "name", 1, IdentifierType.PatientName, true, "Patient name (given, family, text)");
        AddMapping(mappings, "Patient", "birthDate", 3, IdentifierType.Other, true, "Date of birth");
        AddMapping(mappings, "Patient", "identifier", 8, IdentifierType.MedicalRecordNumber, true, "Patient identifiers (MRN, SSN, DL)");
        AddMapping(mappings, "Patient", "telecom", 4, IdentifierType.PhoneNumber, true, "Phone numbers and email addresses");
        AddMapping(mappings, "Patient", "address", 2, IdentifierType.Address, true, "Patient address");
        AddMapping(mappings, "Patient", "photo", 17, IdentifierType.Other, true, "Full face photographs");
        AddMapping(mappings, "Patient", "text", 18, IdentifierType.Other, true, "Narrative text may contain PHI");
        AddMapping(mappings, "Patient", "contact", 1, IdentifierType.PatientName, true, "Emergency contact details");

        // Practitioner resource PHI fields
        AddMapping(mappings, "Practitioner", "name", 1, IdentifierType.ProviderName, true, "Practitioner name");
        AddMapping(mappings, "Practitioner", "identifier", 18, IdentifierType.Other, true, "Practitioner identifiers (NPI, DEA)");
        AddMapping(mappings, "Practitioner", "telecom", 4, IdentifierType.PhoneNumber, true, "Practitioner contact information");
        AddMapping(mappings, "Practitioner", "address", 2, IdentifierType.Address, true, "Practitioner address");
        AddMapping(mappings, "Practitioner", "birthDate", 3, IdentifierType.Other, true, "Practitioner date of birth");
        AddMapping(mappings, "Practitioner", "photo", 17, IdentifierType.Other, true, "Practitioner photographs");
        AddMapping(mappings, "Practitioner", "text", 18, IdentifierType.Other, true, "Narrative text may contain PHI");

        // RelatedPerson resource PHI fields
        AddMapping(mappings, "RelatedPerson", "name", 1, IdentifierType.PatientName, true, "Related person name");
        AddMapping(mappings, "RelatedPerson", "identifier", 18, IdentifierType.Other, true, "Related person identifiers");
        AddMapping(mappings, "RelatedPerson", "telecom", 4, IdentifierType.PhoneNumber, true, "Related person contact information");
        AddMapping(mappings, "RelatedPerson", "address", 2, IdentifierType.Address, true, "Related person address");
        AddMapping(mappings, "RelatedPerson", "birthDate", 3, IdentifierType.Other, true, "Related person date of birth");
        AddMapping(mappings, "RelatedPerson", "photo", 17, IdentifierType.Other, true, "Related person photographs");
        AddMapping(mappings, "RelatedPerson", "text", 18, IdentifierType.Other, true, "Narrative text may contain PHI");

        // Person resource PHI fields
        AddMapping(mappings, "Person", "name", 1, IdentifierType.PatientName, true, "Person name");
        AddMapping(mappings, "Person", "identifier", 18, IdentifierType.Other, true, "Person identifiers");
        AddMapping(mappings, "Person", "telecom", 4, IdentifierType.PhoneNumber, true, "Person contact information");
        AddMapping(mappings, "Person", "address", 2, IdentifierType.Address, true, "Person address");
        AddMapping(mappings, "Person", "birthDate", 3, IdentifierType.Other, true, "Person date of birth");
        AddMapping(mappings, "Person", "photo", 17, IdentifierType.Other, true, "Person photographs");
        AddMapping(mappings, "Person", "text", 18, IdentifierType.Other, true, "Narrative text may contain PHI");

        return mappings;
    }

    private static void AddMapping(
        Dictionary<string, FHIRPhiFieldMapping> mappings,
        string resourceType,
        string jsonPath,
        int hipaaCategory,
        IdentifierType identifierType,
        bool requiresRemoval,
        string description)
    {
        mappings[$"{resourceType}.{jsonPath}"] = new FHIRPhiFieldMapping
        {
            HipaaCategory = hipaaCategory,
            IdentifierType = identifierType,
            RequiresRemoval = requiresRemoval,
            Description = description,
            ResourceType = resourceType,
            JsonPath = jsonPath
        };
    }
}

/// <summary>
/// Mapping of a FHIR resource field to HIPAA Safe Harbor identifier category.
/// </summary>
public record FHIRPhiFieldMapping
{
    public required int HipaaCategory { get; init; }
    public required IdentifierType IdentifierType { get; init; }
    public required bool RequiresRemoval { get; init; }
    public required string Description { get; init; }
    public required string ResourceType { get; init; }
    public required string JsonPath { get; init; }
}
