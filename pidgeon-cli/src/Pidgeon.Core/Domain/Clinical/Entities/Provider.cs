// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Clinical.Entities;

/// <summary>
/// Represents a healthcare provider in the healthcare domain.
/// This is a standards-agnostic representation that can be serialized to HL7, FHIR, or NCPDP.
/// </summary>
public record Provider
{
    /// <summary>
    /// Gets the unique provider identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the provider's name information.
    /// </summary>
    public required PersonName Name { get; init; }

    /// <summary>
    /// Gets the provider's National Provider Identifier (NPI).
    /// </summary>
    public string? NpiNumber { get; init; }

    /// <summary>
    /// Gets the provider's DEA registration number for controlled substances.
    /// </summary>
    public string? DeaNumber { get; init; }

    /// <summary>
    /// Gets the provider's state medical license number.
    /// </summary>
    public string? LicenseNumber { get; init; }

    /// <summary>
    /// Gets the state where the provider is licensed.
    /// </summary>
    public string? LicenseState { get; init; }

    /// <summary>
    /// Gets the provider's specialty or field of practice.
    /// </summary>
    public string? Specialty { get; init; }

    /// <summary>
    /// Gets the provider's subspecialty if applicable.
    /// </summary>
    public string? Subspecialty { get; init; }

    /// <summary>
    /// Gets the provider's primary phone number.
    /// </summary>
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// Gets the provider's fax number.
    /// </summary>
    public string? FaxNumber { get; init; }

    /// <summary>
    /// Gets the provider's email address.
    /// </summary>
    public string? EmailAddress { get; init; }

    /// <summary>
    /// Gets the provider's practice or facility address.
    /// </summary>
    public Address? Address { get; init; }

    /// <summary>
    /// Gets the provider's organization or facility name.
    /// </summary>
    public string? Organization { get; init; }

    /// <summary>
    /// Gets the provider's department within the organization.
    /// </summary>
    public string? Department { get; init; }

    /// <summary>
    /// Gets the provider type (e.g., physician, nurse practitioner, pharmacist).
    /// </summary>
    public ProviderType? ProviderType { get; init; }

    /// <summary>
    /// Gets the provider's degree or credentials (e.g., MD, DO, PharmD, RN).
    /// </summary>
    public string? Degree { get; init; }

    /// <summary>
    /// Gets whether the provider is currently active and able to practice.
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Gets the provider's taxonomy code for billing and classification.
    /// </summary>
    public string? TaxonomyCode { get; init; }

    /// <summary>
    /// Determines if the provider can prescribe controlled substances.
    /// </summary>
    /// <returns>True if provider has DEA registration, false otherwise</returns>
    public bool CanPrescribeControlledSubstances() => !string.IsNullOrWhiteSpace(DeaNumber);

    /// <summary>
    /// Validates that the provider has minimum required information.
    /// </summary>
    /// <returns>A result indicating whether the provider is valid</returns>
    public Result<Provider> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            return Error.Validation("Provider ID is required", nameof(Id));

        if (Name?.IsEmpty() != false)
            return Error.Validation("Provider name is required", nameof(Name));

        // Validate NPI format if provided (should be 10 digits)
        if (!string.IsNullOrWhiteSpace(NpiNumber) && !IsValidNpiFormat(NpiNumber))
            return Error.Validation($"Invalid NPI format: {NpiNumber}", nameof(NpiNumber));

        // Validate DEA format if provided
        if (!string.IsNullOrWhiteSpace(DeaNumber) && !IsValidDeaFormat(DeaNumber))
            return Error.Validation($"Invalid DEA format: {DeaNumber}", nameof(DeaNumber));

        return Result<Provider>.Success(this);
    }

    /// <summary>
    /// Gets the provider's display name with credentials.
    /// </summary>
    public string DisplayNameWithCredentials
    {
        get
        {
            var parts = new List<string> { Name.DisplayName };
            
            if (!string.IsNullOrWhiteSpace(Degree))
                parts.Add(Degree);
                
            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Creates a provider with basic information.
    /// </summary>
    /// <param name="id">Provider identifier</param>
    /// <param name="name">Provider name</param>
    /// <param name="npiNumber">NPI number</param>
    /// <returns>A Provider instance</returns>
    public static Provider Create(string id, PersonName name, string? npiNumber = null) =>
        new() { Id = id, Name = name, NpiNumber = npiNumber };

    private static bool IsValidNpiFormat(string npi)
    {
        // NPI should be exactly 10 digits
        return npi.Length == 10 && npi.All(char.IsDigit);
    }

    private static bool IsValidDeaFormat(string dea)
    {
        // DEA format: 2 letters + 7 digits (e.g., AB1234567)
        return dea.Length == 9 && 
               dea.Take(2).All(char.IsLetter) && 
               dea.Skip(2).All(char.IsDigit);
    }
}

/// <summary>
/// Provider type enumeration.
/// </summary>
public enum ProviderType
{
    /// <summary>
    /// Medical doctor or physician.
    /// </summary>
    Physician,

    /// <summary>
    /// Nurse practitioner.
    /// </summary>
    NursePractitioner,

    /// <summary>
    /// Physician assistant.
    /// </summary>
    PhysicianAssistant,

    /// <summary>
    /// Registered nurse.
    /// </summary>
    RegisteredNurse,

    /// <summary>
    /// Licensed practical nurse.
    /// </summary>
    LicensedPracticalNurse,

    /// <summary>
    /// Pharmacist.
    /// </summary>
    Pharmacist,

    /// <summary>
    /// Pharmacy technician.
    /// </summary>
    PharmacyTechnician,

    /// <summary>
    /// Physical therapist.
    /// </summary>
    PhysicalTherapist,

    /// <summary>
    /// Occupational therapist.
    /// </summary>
    OccupationalTherapist,

    /// <summary>
    /// Respiratory therapist.
    /// </summary>
    RespiratoryTherapist,

    /// <summary>
    /// Social worker.
    /// </summary>
    SocialWorker,

    /// <summary>
    /// Other provider type.
    /// </summary>
    Other
}
