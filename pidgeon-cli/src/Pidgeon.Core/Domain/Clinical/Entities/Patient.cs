// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Clinical.Entities;

/// <summary>
/// Represents a patient in the healthcare domain.
/// This is a standards-agnostic representation that can be serialized to HL7, FHIR, or NCPDP.
/// </summary>
public record Patient
{
    /// <summary>
    /// Gets the unique patient identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the medical record number (MRN).
    /// </summary>
    public string? MedicalRecordNumber { get; init; }

    /// <summary>
    /// Gets the patient's name information.
    /// </summary>
    public required PersonName Name { get; init; }

    /// <summary>
    /// Gets the patient's date of birth.
    /// </summary>
    public DateTime? BirthDate { get; init; }

    /// <summary>
    /// Gets the patient's gender.
    /// </summary>
    public Gender? Gender { get; init; }

    /// <summary>
    /// Gets the patient's address information.
    /// </summary>
    public Address? Address { get; init; }

    /// <summary>
    /// Gets the patient's primary phone number.
    /// </summary>
    public string? PhoneNumber { get; init; }

    /// <summary>
    /// Gets the patient's email address.
    /// </summary>
    public string? EmailAddress { get; init; }

    /// <summary>
    /// Gets the patient's race information.
    /// </summary>
    public string? Race { get; init; }

    /// <summary>
    /// Gets the patient's ethnicity information.
    /// </summary>
    public string? Ethnicity { get; init; }

    /// <summary>
    /// Gets the patient's primary language.
    /// </summary>
    public string? PrimaryLanguage { get; init; }

    /// <summary>
    /// Gets the patient's marital status.
    /// </summary>
    public MaritalStatus? MaritalStatus { get; init; }

    /// <summary>
    /// Gets the patient's social security number.
    /// Note: Handle with extreme care for HIPAA compliance.
    /// </summary>
    public string? SocialSecurityNumber { get; init; }

    /// <summary>
    /// Calculates the patient's current age based on birth date.
    /// </summary>
    /// <param name="asOfDate">The date to calculate age as of (defaults to today)</param>
    /// <returns>The patient's age in years, or null if birth date is not available</returns>
    public int? GetAge(DateTime? asOfDate = null)
    {
        if (BirthDate == null) return null;
        
        var referenceDate = asOfDate ?? DateTime.Today;
        var age = referenceDate.Year - BirthDate.Value.Year;
        
        // Subtract one year if birthday hasn't occurred yet this year
        if (referenceDate < BirthDate.Value.AddYears(age))
            age--;
            
        return age >= 0 ? age : null;
    }

    /// <summary>
    /// Determines if the patient is a pediatric patient (under 18 years old).
    /// </summary>
    /// <returns>True if pediatric, false if adult, null if age cannot be determined</returns>
    public bool? IsPediatric() => GetAge() switch
    {
        int age when age < 18 => true,
        int age when age >= 18 => false,
        _ => null
    };

    /// <summary>
    /// Determines if the patient is a geriatric patient (65 years or older).
    /// </summary>
    /// <returns>True if geriatric, false if not, null if age cannot be determined</returns>
    public bool? IsGeriatric() => GetAge() switch
    {
        int age when age >= 65 => true,
        int age when age < 65 => false,
        _ => null
    };

    /// <summary>
    /// Validates that the patient has minimum required information.
    /// </summary>
    /// <returns>A result indicating whether the patient is valid</returns>
    public Result<Patient> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            return Error.Validation("Patient ID is required", nameof(Id));

        if (Name?.IsEmpty() != false)
            return Error.Validation("Patient name is required", nameof(Name));

        // Additional validation can be added here
        return Result<Patient>.Success(this);
    }
}

/// <summary>
/// Represents a person's name with components.
/// </summary>
public record PersonName
{
    /// <summary>
    /// Gets the person's family name (last name).
    /// </summary>
    public string? Family { get; init; }

    /// <summary>
    /// Gets the person's given name (first name).
    /// </summary>
    public string? Given { get; init; }

    /// <summary>
    /// Gets the person's middle name or initial.
    /// </summary>
    public string? Middle { get; init; }

    /// <summary>
    /// Gets the person's name prefix (Mr., Ms., Dr., etc.).
    /// </summary>
    public string? Prefix { get; init; }

    /// <summary>
    /// Gets the person's name suffix (Jr., Sr., III, etc.).
    /// </summary>
    public string? Suffix { get; init; }

    /// <summary>
    /// Gets the full display name in "Last, First Middle" format.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var parts = new List<string>();
            
            if (!string.IsNullOrWhiteSpace(Family))
                parts.Add(Family);
                
            var givenParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Given))
                givenParts.Add(Given);
            if (!string.IsNullOrWhiteSpace(Middle))
                givenParts.Add(Middle);
                
            if (givenParts.Any())
                parts.Add(string.Join(" ", givenParts));
                
            return parts.Any() ? string.Join(", ", parts) : "Unknown";
        }
    }

    /// <summary>
    /// Determines if the name is effectively empty (no meaningful components).
    /// </summary>
    /// <returns>True if the name has no meaningful components</returns>
    public bool IsEmpty() => 
        string.IsNullOrWhiteSpace(Family) && 
        string.IsNullOrWhiteSpace(Given) && 
        string.IsNullOrWhiteSpace(Middle);

    /// <summary>
    /// Creates a PersonName from separate components.
    /// </summary>
    /// <param name="family">Family name (last name)</param>
    /// <param name="given">Given name (first name)</param>
    /// <param name="middle">Middle name or initial</param>
    /// <returns>A PersonName instance</returns>
    public static PersonName Create(string? family, string? given, string? middle = null) =>
        new() { Family = family, Given = given, Middle = middle };
}

/// <summary>
/// Represents an address.
/// </summary>
public record Address
{
    /// <summary>
    /// Gets the street address line 1.
    /// </summary>
    public string? Street1 { get; init; }

    /// <summary>
    /// Gets the street address line 2 (apartment, suite, etc.).
    /// </summary>
    public string? Street2 { get; init; }

    /// <summary>
    /// Gets the city.
    /// </summary>
    public string? City { get; init; }

    /// <summary>
    /// Gets the state or province.
    /// </summary>
    public string? State { get; init; }

    /// <summary>
    /// Gets the postal code (ZIP code).
    /// </summary>
    public string? PostalCode { get; init; }

    /// <summary>
    /// Gets the country.
    /// </summary>
    public string? Country { get; init; }

    /// <summary>
    /// Gets the full display address.
    /// </summary>
    public string DisplayAddress
    {
        get
        {
            var parts = new List<string>();
            
            if (!string.IsNullOrWhiteSpace(Street1))
                parts.Add(Street1);
            if (!string.IsNullOrWhiteSpace(Street2))
                parts.Add(Street2);
                
            var cityStateZip = new List<string>();
            if (!string.IsNullOrWhiteSpace(City))
                cityStateZip.Add(City);
            if (!string.IsNullOrWhiteSpace(State))
                cityStateZip.Add(State);
            if (!string.IsNullOrWhiteSpace(PostalCode))
                cityStateZip.Add(PostalCode);
                
            if (cityStateZip.Any())
                parts.Add(string.Join(", ", cityStateZip));
                
            if (!string.IsNullOrWhiteSpace(Country))
                parts.Add(Country);
                
            return parts.Any() ? string.Join(", ", parts) : "Unknown";
        }
    }
}

/// <summary>
/// Represents patient gender values.
/// </summary>
public enum Gender
{
    /// <summary>
    /// Male gender.
    /// </summary>
    Male,

    /// <summary>
    /// Female gender.
    /// </summary>
    Female,

    /// <summary>
    /// Other gender.
    /// </summary>
    Other,

    /// <summary>
    /// Unknown gender.
    /// </summary>
    Unknown
}

/// <summary>
/// Represents marital status values.
/// </summary>
public enum MaritalStatus
{
    /// <summary>
    /// Single/never married.
    /// </summary>
    Single,

    /// <summary>
    /// Married.
    /// </summary>
    Married,

    /// <summary>
    /// Divorced.
    /// </summary>
    Divorced,

    /// <summary>
    /// Widowed.
    /// </summary>
    Widowed,

    /// <summary>
    /// Separated.
    /// </summary>
    Separated,

    /// <summary>
    /// Unknown marital status.
    /// </summary>
    Unknown
}