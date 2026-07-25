// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Clinical.Entities;

/// <summary>
/// Represents a healthcare encounter or visit.
/// </summary>
public record Encounter
{
    /// <summary>
    /// Gets the unique encounter identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the patient for this encounter.
    /// </summary>
    public required Patient Patient { get; init; }

    /// <summary>
    /// Gets the primary provider for this encounter.
    /// </summary>
    public required Provider Provider { get; init; }

    /// <summary>
    /// Gets the encounter type (inpatient, outpatient, emergency, etc.).
    /// </summary>
    public EncounterType Type { get; init; } = EncounterType.Outpatient;

    /// <summary>
    /// Gets the encounter status.
    /// </summary>
    public EncounterStatus Status { get; init; } = EncounterStatus.Planned;

    /// <summary>
    /// Gets the encounter class (ambulatory, inpatient, etc.).
    /// </summary>
    public string? EncounterClass { get; init; }

    /// <summary>
    /// Gets the date and time the encounter started.
    /// </summary>
    public DateTime? StartTime { get; init; }

    /// <summary>
    /// Gets the date and time the encounter ended.
    /// </summary>
    public DateTime? EndTime { get; init; }

    /// <summary>
    /// Gets the facility or location where the encounter occurred.
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Gets the department within the facility.
    /// </summary>
    public string? Department { get; init; }

    /// <summary>
    /// Gets the room or bed identifier.
    /// </summary>
    public string? Room { get; init; }

    /// <summary>
    /// Gets the primary diagnosis for this encounter.
    /// </summary>
    public Diagnosis? PrimaryDiagnosis { get; init; }

    /// <summary>
    /// Gets additional diagnoses for this encounter.
    /// </summary>
    public IReadOnlyList<Diagnosis> SecondaryDiagnoses { get; init; } = Array.Empty<Diagnosis>();

    /// <summary>
    /// Gets the reason for the visit or chief complaint.
    /// </summary>
    public string? ReasonForVisit { get; init; }

    /// <summary>
    /// Gets the priority of the encounter.
    /// </summary>
    public EncounterPriority Priority { get; init; } = EncounterPriority.Routine;

    /// <summary>
    /// Gets the admission source for inpatient encounters.
    /// </summary>
    public string? AdmissionSource { get; init; }

    /// <summary>
    /// Gets the discharge disposition.
    /// </summary>
    public string? DischargeDisposition { get; init; }

    /// <summary>
    /// Calculates the duration of the encounter.
    /// </summary>
    /// <returns>The duration of the encounter, or null if times are not available</returns>
    public TimeSpan? GetDuration()
    {
        if (StartTime.HasValue && EndTime.HasValue)
            return EndTime.Value - StartTime.Value;
            
        return null;
    }

    /// <summary>
    /// Determines if the encounter is currently in progress.
    /// </summary>
    /// <returns>True if encounter is in progress, false otherwise</returns>
    public bool IsInProgress() => Status == EncounterStatus.InProgress;

    /// <summary>
    /// Validates that the encounter has minimum required information.
    /// </summary>
    /// <returns>A result indicating whether the encounter is valid</returns>
    public Result<Encounter> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            return Error.Validation("Encounter ID is required", nameof(Id));

        var patientValidation = Patient.Validate();
        if (patientValidation.IsFailure)
            return Result<Encounter>.Failure(patientValidation.Error);

        var providerValidation = Provider.Validate();
        if (providerValidation.IsFailure)
            return Result<Encounter>.Failure(providerValidation.Error);

        // Validate time logic
        if (StartTime.HasValue && EndTime.HasValue && EndTime.Value < StartTime.Value)
            return Error.Validation("End time cannot be before start time", nameof(EndTime));

        return Result<Encounter>.Success(this);
    }
}

/// <summary>
/// Represents a diagnosis with coding information.
/// </summary>
public record Diagnosis
{
    /// <summary>
    /// Gets the diagnosis code (ICD-10, ICD-9, etc.).
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the coding system used (ICD10CM, ICD9CM, etc.).
    /// </summary>
    public string? CodingSystem { get; init; }

    /// <summary>
    /// Gets the diagnosis description.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the diagnosis type (primary, secondary, etc.).
    /// </summary>
    public DiagnosisType Type { get; init; } = DiagnosisType.Primary;

    /// <summary>
    /// Gets the date the diagnosis was made.
    /// </summary>
    public DateTime? DiagnosisDate { get; init; }

    /// <summary>
    /// Gets the provider who made the diagnosis.
    /// </summary>
    public Provider? DiagnosingProvider { get; init; }

    /// <summary>
    /// Gets the severity of the diagnosis if applicable.
    /// </summary>
    public string? Severity { get; init; }

    /// <summary>
    /// Gets whether this is a working or confirmed diagnosis.
    /// </summary>
    public DiagnosisStatus Status { get; init; } = DiagnosisStatus.Confirmed;

    /// <summary>
    /// Creates a diagnosis with basic information.
    /// </summary>
    /// <param name="code">Diagnosis code</param>
    /// <param name="description">Diagnosis description</param>
    /// <param name="codingSystem">Coding system used</param>
    /// <returns>A Diagnosis instance</returns>
    public static Diagnosis Create(string code, string description, string? codingSystem = null) =>
        new() { Code = code, Description = description, CodingSystem = codingSystem };
}

/// <summary>
/// Encounter type enumeration.
/// </summary>
public enum EncounterType
{
    /// <summary>
    /// Inpatient admission.
    /// </summary>
    Inpatient,

    /// <summary>
    /// Outpatient visit.
    /// </summary>
    Outpatient,

    /// <summary>
    /// Emergency department visit.
    /// </summary>
    Emergency,

    /// <summary>
    /// Observation stay.
    /// </summary>
    Observation,

    /// <summary>
    /// Day surgery or procedure.
    /// </summary>
    DaySurgery,

    /// <summary>
    /// Telemedicine encounter.
    /// </summary>
    Telemedicine,

    /// <summary>
    /// Home health visit.
    /// </summary>
    HomeHealth,

    /// <summary>
    /// Pre-admission testing.
    /// </summary>
    PreAdmission
}

/// <summary>
/// Encounter status enumeration.
/// </summary>
public enum EncounterStatus
{
    /// <summary>
    /// Encounter is planned but not yet started.
    /// </summary>
    Planned,

    /// <summary>
    /// Patient has arrived and encounter is about to begin.
    /// </summary>
    Arrived,

    /// <summary>
    /// Encounter is currently in progress.
    /// </summary>
    InProgress,

    /// <summary>
    /// Encounter is temporarily suspended.
    /// </summary>
    OnHold,

    /// <summary>
    /// Encounter has been completed.
    /// </summary>
    Finished,

    /// <summary>
    /// Encounter was cancelled before completion.
    /// </summary>
    Cancelled,

    /// <summary>
    /// Patient did not show up for scheduled encounter.
    /// </summary>
    NoShow
}

/// <summary>
/// Encounter priority enumeration.
/// </summary>
public enum EncounterPriority
{
    /// <summary>
    /// Routine, non-urgent encounter.
    /// </summary>
    Routine,

    /// <summary>
    /// Urgent encounter requiring prompt attention.
    /// </summary>
    Urgent,

    /// <summary>
    /// Emergency encounter requiring immediate attention.
    /// </summary>
    Emergency,

    /// <summary>
    /// Critical encounter, life-threatening situation.
    /// </summary>
    Critical
}

/// <summary>
/// Diagnosis type enumeration.
/// </summary>
public enum DiagnosisType
{
    /// <summary>
    /// Primary diagnosis.
    /// </summary>
    Primary,

    /// <summary>
    /// Secondary diagnosis.
    /// </summary>
    Secondary,

    /// <summary>
    /// Admitting diagnosis.
    /// </summary>
    Admitting,

    /// <summary>
    /// Discharge diagnosis.
    /// </summary>
    Discharge,

    /// <summary>
    /// Working diagnosis, not yet confirmed.
    /// </summary>
    Working,

    /// <summary>
    /// Rule out diagnosis being investigated.
    /// </summary>
    RuleOut
}

/// <summary>
/// Diagnosis status enumeration.
/// </summary>
public enum DiagnosisStatus
{
    /// <summary>
    /// Confirmed diagnosis.
    /// </summary>
    Confirmed,

    /// <summary>
    /// Provisional or working diagnosis.
    /// </summary>
    Provisional,

    /// <summary>
    /// Differential diagnosis being considered.
    /// </summary>
    Differential,

    /// <summary>
    /// Ruled out diagnosis.
    /// </summary>
    RuledOut,

    /// <summary>
    /// Resolved diagnosis, no longer active.
    /// </summary>
    Resolved
}