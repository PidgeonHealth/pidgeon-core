// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Shared helpers used across FHIR resource builders so every builder reuses
/// the same code-system mappings without duplicating them.
///
/// Kept <c>internal static</c> because these are pure lookup functions with
/// no state; no need for DI. Each method documents the FHIR / terminology
/// source it mirrors.
/// </summary>
internal static class FHIRBuilderSupport
{
    /// <summary>
    /// Shared JSON serializer options for all resource builders. Matches
    /// <c>FHIRResourceFactory</c>'s formatting so output is byte-equivalent.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Derives a stable FHIR resource id from a coordinate key. The id is a pure function of the
    /// key, so a seeded generation emits the same id every time; the key's own <c>resource-id</c>
    /// sub-coordinate keeps the id stream independent of the builder's value RNG. The output keeps the
    /// <c>{prefix}-{32 hex}</c> shape callers and references expect.
    /// </summary>
    public static string GenerateDeterministicId(string resourcePrefix, GenerationKey key)
    {
        var bytes = new byte[16];
        // Fold the prefix into the coordinate so a builder that mints more than one id from the same
        // context key (e.g. MedicationAdministration) gets distinct ids per prefix without collisions.
        key.Derive("resource-id").Derive(resourcePrefix).AsRandom().NextBytes(bytes);
        return $"{resourcePrefix}-{new Guid(bytes):N}";
    }

    /// <summary>
    /// Resolve the patient reference id used by every subject/patient field
    /// across builders. Prefer the bundle-scoped registered Patient, fall
    /// back to the MRN-derived form, final fallback to the Patient.Id guid.
    /// </summary>
    public static string ResolvePatientId(FHIRBuildContext context, Patient patient)
    {
        return context.References.GetId("Patient")
            ?? (patient.MedicalRecordNumber != null
                ? $"patient-{patient.MedicalRecordNumber}"
                : $"patient-{patient.Id}");
    }

    /// <summary>NUCC provider taxonomy code for a specialty name.</summary>
    public static string GetNUCCCodeForSpecialty(string? specialty)
    {
        return specialty?.ToLowerInvariant() switch
        {
            "emergency medicine" or "emergency" => "207P00000X",
            "internal medicine" or "internist" => "207R00000X",
            "family medicine" or "family practice" => "207Q00000X",
            "pediatrics" or "pediatrician" => "208000000X",
            "cardiology" or "cardiologist" => "207RC0000X",
            "orthopedic surgery" or "orthopedics" => "207X00000X",
            "radiology" or "radiologist" => "2085R0202X",
            "anesthesiology" or "anesthesiologist" => "207L00000X",
            "psychiatry" or "psychiatrist" => "2084P0800X",
            "general surgery" or "surgeon" => "208600000X",
            _ => "207Q00000X"
        };
    }

    /// <summary>SNOMED CT code for a medication dosage form.</summary>
    public static string GetSnomedFormCode(string form)
    {
        return form switch
        {
            "tablet" => "385055001",
            "capsule" => "385049006",
            "liquid" => "385023001",
            "injectable" => "385219001",
            "inhaler" => "385207009",
            "cream" => "385099005",
            "ointment" => "385101003",
            _ => "421026006"
        };
    }

    /// <summary>HL7 v3 GTS timing abbreviation for a dosing frequency string.</summary>
    public static string MapFrequencyToTimingCode(string frequency)
    {
        return frequency.ToUpperInvariant() switch
        {
            "QD" or "DAILY" => "QD",
            "BID" => "BID",
            "TID" => "TID",
            "QID" => "QID",
            "Q4H" => "Q4H",
            "Q6H" => "Q6H",
            "Q8H" => "Q8H",
            "Q12H" => "Q12H",
            "QHS" => "QHS",
            "PRN" => "PRN",
            _ => frequency
        };
    }

    /// <summary>SNOMED CT route of administration code.</summary>
    public static string GetRouteCode(RouteOfAdministration route)
    {
        return route switch
        {
            RouteOfAdministration.Oral => "26643006",
            RouteOfAdministration.Intravenous => "47625008",
            RouteOfAdministration.Intramuscular => "78421000",
            RouteOfAdministration.Subcutaneous => "34206005",
            RouteOfAdministration.Topical => "6064005",
            RouteOfAdministration.Inhalation => "18679011000001101",
            RouteOfAdministration.Rectal => "37161004",
            RouteOfAdministration.Sublingual => "37839007",
            RouteOfAdministration.Nasal => "46713006",
            RouteOfAdministration.Ophthalmic => "54485002",
            _ => "26643006"
        };
    }
}
