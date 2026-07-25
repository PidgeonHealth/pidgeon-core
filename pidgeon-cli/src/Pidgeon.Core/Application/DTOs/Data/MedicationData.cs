// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Pidgeon.Core.Application.DTOs.Data;

/// <summary>
/// Medication data from free tier datasets.
/// Standard-agnostic format for use across HL7, FHIR, and NCPDP.
/// </summary>
public record MedicationData
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("genericName")]
    public required string GenericName { get; init; }

    [JsonPropertyName("brandName")]
    public required string BrandName { get; init; }

    [JsonPropertyName("labelerName")]
    public required string LabelerName { get; init; }

    [JsonPropertyName("ndc")]
    public required string Ndc { get; init; }

    [JsonPropertyName("strength")]
    public required string Strength { get; init; }

    [JsonPropertyName("unit")]
    public required string Unit { get; init; }

    [JsonPropertyName("dosageForm")]
    public required string DosageForm { get; init; }

    [JsonPropertyName("routeName")]
    public required string RouteName { get; init; }

    [JsonPropertyName("packageDescription")]
    public required string PackageDescription { get; init; }
}
