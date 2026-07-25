// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Pidgeon.Core.Application.DTOs.Data;

/// <summary>
/// RxNorm prescribable drug data.
/// Standard-agnostic format for use across HL7, FHIR, and other standards.
/// </summary>
public record RxNormDrugData
{
    [JsonPropertyName("rxcui")]
    public required string Rxcui { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("termType")]
    public required string TermType { get; init; }

    [JsonPropertyName("ingredientName")]
    public required string IngredientName { get; init; }

    [JsonPropertyName("strength")]
    public string Strength { get; init; } = "";

    [JsonPropertyName("dosageForm")]
    public string DosageForm { get; init; } = "";

    [JsonPropertyName("ndc")]
    public string Ndc { get; init; } = "";
}
