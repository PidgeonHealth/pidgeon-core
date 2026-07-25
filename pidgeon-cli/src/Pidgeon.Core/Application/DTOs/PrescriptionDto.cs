// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.DTOs;

/// <summary>
/// Data transfer object for prescription information used in Clinical→Messaging transformations.
/// Decouples Messaging domain from Clinical domain while preserving pharmaceutical semantics.
/// </summary>
public record PrescriptionDto
{
    public required string Id { get; init; }
    public required PatientDto Patient { get; init; }
    public required MedicationDto Medication { get; init; }
    public required DosageDto Dosage { get; init; }
    public required ProviderDto Prescriber { get; init; }
    public string? Instructions { get; init; }
    public DateTime DatePrescribed { get; init; }
    public int RefillsAllowed { get; init; }
    public int QuantityPrescribed { get; init; }
}

/// <summary>
/// Medication information for messaging domain usage.
/// </summary>
public record MedicationDto
{
    public required string GenericName { get; init; }
    public string? BrandName { get; init; }
    public string? NdcCode { get; init; }
    public string? DisplayName { get; init; }
    public DosageFormDto? DosageForm { get; init; }
    public string? Strength { get; init; }
}

/// <summary>
/// Dosage information for messaging domain usage.
/// </summary>
public record DosageDto
{
    public required string Dose { get; init; }
    public string? DoseUnit { get; init; }
    public string? Frequency { get; init; }
    public string? Route { get; init; }
}

/// <summary>
/// Provider information for messaging domain usage.
/// </summary>
public record ProviderDto
{
    public required string Id { get; init; }
    public required PersonNameDto Name { get; init; }
    public string? LicenseNumber { get; init; }
    public string? DeaNumber { get; init; }
    public string? Specialty { get; init; }
    public AddressDto? Address { get; init; }
    public string? PhoneNumber { get; init; }
}

/// <summary>
/// Dosage form enumeration for messaging domain usage.
/// </summary>
public enum DosageFormDto
{
    Tablet,
    Capsule,
    Liquid,
    Injection,
    Cream,
    Ointment,
    Patch,
    Inhaler,
    Suppository,
    Other
}