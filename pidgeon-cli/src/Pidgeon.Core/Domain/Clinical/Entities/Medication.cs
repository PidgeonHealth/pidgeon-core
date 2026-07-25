// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Clinical.Entities;

/// <summary>
/// Represents a medication in the healthcare domain.
/// This is a standards-agnostic representation that can be serialized to HL7, FHIR, or NCPDP.
/// </summary>
public record Medication
{
    /// <summary>
    /// Gets the medication identifier (NDC, RxNorm, etc.).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the medication name (brand or generic).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the generic name if this is a brand medication.
    /// </summary>
    public string? GenericName { get; init; }

    /// <summary>
    /// Gets the medication strength (e.g., "10mg", "250mg/5ml").
    /// </summary>
    public string? Strength { get; init; }

    /// <summary>
    /// Gets the dosage form (tablet, capsule, liquid, etc.).
    /// </summary>
    public DosageForm? DosageForm { get; init; }

    /// <summary>
    /// Gets the route of administration (oral, IV, topical, etc.).
    /// </summary>
    public RouteOfAdministration? Route { get; init; }

    /// <summary>
    /// Gets the NDC (National Drug Code) if available.
    /// </summary>
    public string? NdcCode { get; init; }

    /// <summary>
    /// Gets the RxNorm code if available.
    /// </summary>
    public string? RxNormCode { get; init; }

    /// <summary>
    /// Gets the drug class or therapeutic category.
    /// </summary>
    public string? DrugClass { get; init; }

    /// <summary>
    /// Gets whether this is a controlled substance and its schedule.
    /// </summary>
    public ControlledSubstanceSchedule? ControlledSchedule { get; init; }

    /// <summary>
    /// Gets the manufacturer name.
    /// </summary>
    public string? Manufacturer { get; init; }

    /// <summary>
    /// Gets the drug's expiration date if applicable.
    /// </summary>
    public DateTime? ExpirationDate { get; init; }

    /// <summary>
    /// Gets the full display name combining name and strength.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var parts = new List<string> { Name };
            
            if (!string.IsNullOrWhiteSpace(Strength))
                parts.Add(Strength);
                
            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// Validates that the medication has minimum required information.
    /// </summary>
    /// <returns>A result indicating whether the medication is valid</returns>
    public Result<Medication> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            return Error.Validation("Medication ID is required", nameof(Id));

        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("Medication name is required", nameof(Name));

        // Validate NDC format if provided (should be 11 digits with hyphens)
        if (!string.IsNullOrWhiteSpace(NdcCode) && !IsValidNdcFormat(NdcCode))
            return Error.Validation($"Invalid NDC format: {NdcCode}", nameof(NdcCode));

        return Result<Medication>.Success(this);
    }

    /// <summary>
    /// Determines if this medication requires DEA registration to prescribe.
    /// </summary>
    /// <returns>True if controlled substance, false otherwise</returns>
    public bool RequiresDeaRegistration() => ControlledSchedule.HasValue;

    /// <summary>
    /// Creates a medication with basic information.
    /// </summary>
    /// <param name="id">Medication identifier</param>
    /// <param name="name">Medication name</param>
    /// <param name="strength">Medication strength</param>
    /// <returns>A Medication instance</returns>
    public static Medication Create(string id, string name, string? strength = null) =>
        new() { Id = id, Name = name, Strength = strength };

    private static bool IsValidNdcFormat(string ndc)
    {
        // NDC should be in format XXXXX-XXXX-XX or XXXXX-XXX-XX or XXXX-XXXX-XX
        // Total of 10-11 digits with 2 hyphens
        var digitCount = ndc.Count(char.IsDigit);
        var hyphenCount = ndc.Count(c => c == '-');
        
        return digitCount >= 10 && digitCount <= 11 && hyphenCount == 2;
    }
}

/// <summary>
/// Represents a prescription for a medication.
/// </summary>
public record Prescription
{
    /// <summary>
    /// Gets the prescription identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the patient who is prescribed this medication.
    /// </summary>
    public required Patient Patient { get; init; }

    /// <summary>
    /// Gets the medication being prescribed.
    /// </summary>
    public required Medication Medication { get; init; }

    /// <summary>
    /// Gets the provider who prescribed the medication.
    /// </summary>
    public required Provider Prescriber { get; init; }

    /// <summary>
    /// Gets the dosing instructions.
    /// </summary>
    public required DosageInstructions Dosage { get; init; }

    /// <summary>
    /// Gets the date the prescription was written.
    /// </summary>
    public DateTime DatePrescribed { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the date range during which the prescription is effective.
    /// </summary>
    public DateRange? EffectivePeriod { get; init; }

    /// <summary>
    /// Gets the diagnosis or indication for this prescription.
    /// </summary>
    public string? Indication { get; init; }

    /// <summary>
    /// Gets any special instructions for the patient or pharmacist.
    /// </summary>
    public string? Instructions { get; init; }

    /// <summary>
    /// Gets whether generic substitution is allowed.
    /// </summary>
    public bool AllowGenericSubstitution { get; init; } = true;

    /// <summary>
    /// Gets the priority of this prescription.
    /// </summary>
    public PrescriptionPriority Priority { get; init; } = PrescriptionPriority.Routine;

    /// <summary>
    /// Validates that the prescription has all required information.
    /// </summary>
    /// <returns>A result indicating whether the prescription is valid</returns>
    public Result<Prescription> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            return Error.Validation("Prescription ID is required", nameof(Id));

        var patientValidation = Patient.Validate();
        if (patientValidation.IsFailure)
            return Result<Prescription>.Failure(patientValidation.Error);

        var medicationValidation = Medication.Validate();
        if (medicationValidation.IsFailure)
            return Result<Prescription>.Failure(medicationValidation.Error);

        var prescriberValidation = Prescriber.Validate();
        if (prescriberValidation.IsFailure)
            return Result<Prescription>.Failure(prescriberValidation.Error);

        var dosageValidation = Dosage.Validate();
        if (dosageValidation.IsFailure)
            return Result<Prescription>.Failure(dosageValidation.Error);

        // Validate controlled substance requirements
        if (Medication.RequiresDeaRegistration() && string.IsNullOrWhiteSpace(Prescriber.DeaNumber))
            return Error.Validation("DEA number required for controlled substance", nameof(Prescriber.DeaNumber));

        return Result<Prescription>.Success(this);
    }
}

/// <summary>
/// Represents dosing instructions for a medication.
/// </summary>
public record DosageInstructions
{
    /// <summary>
    /// Gets the dose amount (e.g., "1", "2.5").
    /// </summary>
    public required string Dose { get; init; }

    /// <summary>
    /// Gets the dose unit (e.g., "tablet", "mg", "ml").
    /// </summary>
    public required string DoseUnit { get; init; }

    /// <summary>
    /// Gets the frequency (e.g., "BID", "TID", "QID", "Q6H").
    /// </summary>
    public required string Frequency { get; init; }

    /// <summary>
    /// Gets the route of administration.
    /// </summary>
    public RouteOfAdministration Route { get; init; } = RouteOfAdministration.Oral;

    /// <summary>
    /// Gets the total quantity to dispense.
    /// </summary>
    public int? Quantity { get; init; }

    /// <summary>
    /// Gets the number of days the medication should last.
    /// </summary>
    public int? DaysSupply { get; init; }

    /// <summary>
    /// Gets the number of refills allowed.
    /// </summary>
    public int? Refills { get; init; }

    /// <summary>
    /// Gets additional dosing instructions (e.g., "with food", "at bedtime").
    /// </summary>
    public string? AdditionalInstructions { get; init; }

    /// <summary>
    /// Gets the complete dosing instructions as a human-readable string.
    /// </summary>
    public string Instructions
    {
        get
        {
            var parts = new List<string>();
            
            if (!string.IsNullOrWhiteSpace(Dose) && !string.IsNullOrWhiteSpace(DoseUnit))
                parts.Add($"Take {Dose} {DoseUnit}");
                
            if (!string.IsNullOrWhiteSpace(Frequency))
                parts.Add(Frequency);
                
            if (Route != RouteOfAdministration.Oral)
                parts.Add($"via {Route}");
                
            if (!string.IsNullOrWhiteSpace(AdditionalInstructions))
                parts.Add(AdditionalInstructions);
                
            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// Calculates days supply based on quantity and frequency.
    /// </summary>
    /// <returns>Calculated days supply or null if cannot be determined</returns>
    public int? CalculateDaysSupply()
    {
        if (!Quantity.HasValue || string.IsNullOrWhiteSpace(Frequency))
            return DaysSupply;

        // Simple calculation for common frequencies
        var dailyDoses = Frequency.ToUpper() switch
        {
            "QD" or "DAILY" => (int?)1,
            "BID" or "Q12H" => (int?)2,
            "TID" or "Q8H" => (int?)3,
            "QID" or "Q6H" => (int?)4,
            "Q4H" => (int?)6,
            _ => (int?)null
        };

        if (dailyDoses.HasValue && decimal.TryParse(Dose, out var doseAmount))
        {
            var totalDailyDose = dailyDoses.Value * doseAmount;
            return (int)Math.Ceiling(Quantity.Value / totalDailyDose);
        }

        return DaysSupply;
    }

    /// <summary>
    /// Validates the dosage instructions.
    /// </summary>
    /// <returns>A result indicating whether the dosage instructions are valid</returns>
    public Result<DosageInstructions> Validate()
    {
        if (string.IsNullOrWhiteSpace(Dose))
            return Error.Validation("Dose amount is required", nameof(Dose));

        if (string.IsNullOrWhiteSpace(DoseUnit))
            return Error.Validation("Dose unit is required", nameof(DoseUnit));

        if (string.IsNullOrWhiteSpace(Frequency))
            return Error.Validation("Frequency is required", nameof(Frequency));

        if (Quantity.HasValue && Quantity.Value <= 0)
            return Error.Validation("Quantity must be greater than zero", nameof(Quantity));

        if (DaysSupply.HasValue && DaysSupply.Value <= 0)
            return Error.Validation("Days supply must be greater than zero", nameof(DaysSupply));

        if (Refills.HasValue && Refills.Value < 0)
            return Error.Validation("Refills cannot be negative", nameof(Refills));

        return Result<DosageInstructions>.Success(this);
    }
}

/// <summary>
/// Represents a date range.
/// </summary>
public record DateRange
{
    /// <summary>
    /// Gets the start date.
    /// </summary>
    public DateTime Start { get; init; }

    /// <summary>
    /// Gets the end date.
    /// </summary>
    public DateTime? End { get; init; }

    /// <summary>
    /// Gets whether the date range is currently active.
    /// </summary>
    public bool IsActive(DateTime? asOfDate = null)
    {
        var referenceDate = asOfDate ?? DateTime.UtcNow;
        return referenceDate >= Start && (End == null || referenceDate <= End.Value);
    }
}

/// <summary>
/// Dosage form enumeration.
/// </summary>
public enum DosageForm
{
    Tablet,
    Capsule,
    Liquid,
    Injectable,
    Topical,
    Suppository,
    Inhaler,
    Patch,
    Drops,
    Cream,
    Ointment,
    Gel,
    Powder,
    Other
}

/// <summary>
/// Route of administration enumeration.
/// </summary>
public enum RouteOfAdministration
{
    Oral,
    Intravenous,
    Intramuscular,
    Subcutaneous,
    Topical,
    Inhalation,
    Rectal,
    Vaginal,
    Ophthalmic,
    Otic,
    Nasal,
    Sublingual,
    Other
}

/// <summary>
/// Controlled substance schedule enumeration.
/// </summary>
public enum ControlledSubstanceSchedule
{
    /// <summary>
    /// Schedule I - No accepted medical use, high abuse potential.
    /// </summary>
    ScheduleI = 1,

    /// <summary>
    /// Schedule II - Accepted medical use, high abuse potential.
    /// </summary>
    ScheduleII = 2,

    /// <summary>
    /// Schedule III - Accepted medical use, moderate abuse potential.
    /// </summary>
    ScheduleIII = 3,

    /// <summary>
    /// Schedule IV - Accepted medical use, low abuse potential.
    /// </summary>
    ScheduleIV = 4,

    /// <summary>
    /// Schedule V - Accepted medical use, very low abuse potential.
    /// </summary>
    ScheduleV = 5
}

/// <summary>
/// Prescription priority enumeration.
/// </summary>
public enum PrescriptionPriority
{
    Routine,
    Urgent,
    Stat,
    Emergency
}