// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP.DeIdentification;

/// <summary>
/// Maps NCPDP SCRIPT 2017071 XML element paths to HIPAA Safe Harbor identifier categories.
/// Provides element-level classification for de-identification of NCPDP XML messages.
/// </summary>
public class NCPDPSafeHarborFieldMapper
{
    private readonly Dictionary<string, NCPDPPhiFieldMapping> _mappings;

    public NCPDPSafeHarborFieldMapper()
    {
        _mappings = BuildMappings();
    }

    /// <summary>
    /// Gets the PHI field mapping for a given XML element path.
    /// </summary>
    public NCPDPPhiFieldMapping? GetFieldMapping(string xmlPath)
    {
        return _mappings.TryGetValue(xmlPath, out var mapping) ? mapping : null;
    }

    /// <summary>
    /// Gets all PHI field mappings for a given parent element.
    /// </summary>
    public Dictionary<string, NCPDPPhiFieldMapping> GetPhiFieldsForElement(string parentElement)
    {
        return _mappings
            .Where(kvp => kvp.Key.StartsWith($"{parentElement}/", StringComparison.Ordinal)
                       || kvp.Key.StartsWith($"{parentElement}.", StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Gets all field mappings.
    /// </summary>
    public IReadOnlyDictionary<string, NCPDPPhiFieldMapping> GetAllMappings() => _mappings;

    /// <summary>
    /// Returns true if the given XML path is a known PHI field.
    /// </summary>
    public bool IsPhiField(string xmlPath)
    {
        return _mappings.ContainsKey(xmlPath);
    }

    private static Dictionary<string, NCPDPPhiFieldMapping> BuildMappings()
    {
        var mappings = new Dictionary<string, NCPDPPhiFieldMapping>();

        // Patient PHI fields
        AddMapping(mappings, "Patient/Name/LastName", 1, IdentifierType.PatientName, true, "Patient last name");
        AddMapping(mappings, "Patient/Name/FirstName", 1, IdentifierType.PatientName, true, "Patient first name");
        AddMapping(mappings, "Patient/DateOfBirth", 3, IdentifierType.Other, true, "Patient date of birth");
        AddMapping(mappings, "Patient/Phone", 4, IdentifierType.PhoneNumber, true, "Patient phone number");
        AddMapping(mappings, "Patient/Address/Street", 2, IdentifierType.Address, true, "Patient street address");
        AddMapping(mappings, "Patient/Address/City", 2, IdentifierType.Address, true, "Patient city");
        AddMapping(mappings, "Patient/Address/PostalCode", 2, IdentifierType.Address, true, "Patient postal code");
        AddMapping(mappings, "Patient/Identification/SocialSecurity", 7, IdentifierType.SocialSecurityNumber, true, "Patient SSN");
        AddMapping(mappings, "Patient/Identification/CardholderID", 8, IdentifierType.InsuranceId, true, "Insurance cardholder ID");

        // Prescriber PHI fields
        AddMapping(mappings, "Prescriber/Name/LastName", 1, IdentifierType.ProviderName, true, "Prescriber last name");
        AddMapping(mappings, "Prescriber/Name/FirstName", 1, IdentifierType.ProviderName, true, "Prescriber first name");
        AddMapping(mappings, "Prescriber/DEA", 11, IdentifierType.LicenseNumber, true, "DEA registration number");
        AddMapping(mappings, "Prescriber/NPI", 18, IdentifierType.Other, true, "National Provider Identifier");
        AddMapping(mappings, "Prescriber/Phone", 4, IdentifierType.PhoneNumber, true, "Prescriber phone number");
        AddMapping(mappings, "Prescriber/Address/Street", 2, IdentifierType.Address, true, "Prescriber street address");
        AddMapping(mappings, "Prescriber/Address/City", 2, IdentifierType.Address, true, "Prescriber city");
        AddMapping(mappings, "Prescriber/Address/PostalCode", 2, IdentifierType.Address, true, "Prescriber postal code");

        // Pharmacy PHI fields
        AddMapping(mappings, "Pharmacy/NCPDPID", 18, IdentifierType.Other, true, "NCPDP pharmacy identifier");
        AddMapping(mappings, "Pharmacy/Phone", 4, IdentifierType.PhoneNumber, true, "Pharmacy phone number");
        AddMapping(mappings, "Pharmacy/Address/Street", 2, IdentifierType.Address, true, "Pharmacy street address");
        AddMapping(mappings, "Pharmacy/Address/City", 2, IdentifierType.Address, true, "Pharmacy city");
        AddMapping(mappings, "Pharmacy/Address/PostalCode", 2, IdentifierType.Address, true, "Pharmacy postal code");

        return mappings;
    }

    private static void AddMapping(
        Dictionary<string, NCPDPPhiFieldMapping> mappings,
        string xmlPath,
        int hipaaCategory,
        IdentifierType identifierType,
        bool requiresRemoval,
        string description)
    {
        mappings[xmlPath] = new NCPDPPhiFieldMapping
        {
            HipaaCategory = hipaaCategory,
            IdentifierType = identifierType,
            RequiresRemoval = requiresRemoval,
            Description = description,
            XmlPath = xmlPath
        };
    }
}

/// <summary>
/// Mapping of an NCPDP SCRIPT XML element to HIPAA Safe Harbor identifier category.
/// </summary>
public record NCPDPPhiFieldMapping
{
    public required int HipaaCategory { get; init; }
    public required IdentifierType IdentifierType { get; init; }
    public required bool RequiresRemoval { get; init; }
    public required string Description { get; init; }
    public required string XmlPath { get; init; }
}
