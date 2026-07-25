// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.DTOs;

/// <summary>
/// Data transfer object for patient information used in Clinical→Messaging transformations.
/// Decouples Messaging domain from Clinical domain while preserving healthcare semantics.
/// </summary>
public record PatientDto
{
    public required string Id { get; init; }
    public string? MedicalRecordNumber { get; init; }
    public required PersonNameDto Name { get; init; }
    public DateTime? BirthDate { get; init; }
    public GenderDto? Gender { get; init; }
    public AddressDto? Address { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Race { get; init; }
    public string? PrimaryLanguage { get; init; }
    public MaritalStatusDto? MaritalStatus { get; init; }
    public string? SocialSecurityNumber { get; init; }
    public string? Ethnicity { get; init; }
}

/// <summary>
/// Person name information for messaging domain usage.
/// </summary>
public record PersonNameDto
{
    public required string LastName { get; init; }
    public required string FirstName { get; init; }
    public string? MiddleName { get; init; }
    public string? Suffix { get; init; }
    public string? Prefix { get; init; }
    
    public string DisplayName => $"{FirstName} {MiddleName} {LastName}".Replace("  ", " ").Trim();
    public bool IsEmpty() => string.IsNullOrEmpty(FirstName) && string.IsNullOrEmpty(LastName);
}

/// <summary>
/// Address information for messaging domain usage.
/// </summary>
public record AddressDto
{
    public string? Street1 { get; init; }
    public string? Street2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public string? Country { get; init; }
}

/// <summary>
/// Gender enumeration for messaging domain usage.
/// </summary>
public enum GenderDto
{
    Male,
    Female,
    Other,
    Unknown
}

/// <summary>
/// Marital status enumeration for messaging domain usage.
/// </summary>
public enum MaritalStatusDto
{
    Single,
    Married,
    Divorced,
    Widowed,
    Separated,
    Unknown
}

/// <summary>
/// Encounter information for messaging domain usage.
/// </summary>
public record EncounterDto
{
    public required string Id { get; init; }
    public EncounterTypeDto Type { get; init; }
    public string? Location { get; init; }
    public DateTime? StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public ProviderDto? Provider { get; init; }
}

/// <summary>
/// Encounter type enumeration for messaging domain usage.
/// </summary>
public enum EncounterTypeDto
{
    Inpatient,
    Outpatient,
    Emergency,
    Observation,
    DaySurgery,
    Telemedicine,
    HomeHealth,
    PreAdmission
}