// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Pidgeon.Core.Application.DTOs.Data;

/// <summary>
/// Metadata for an installed data package (e.g., LOINC, ICD-10, NDC).
/// </summary>
public record PackageManifest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("license")]
    public required string License { get; init; }

    [JsonPropertyName("recordCount")]
    public required int RecordCount { get; init; }

    [JsonPropertyName("dataType")]
    public required string DataType { get; init; }

    [JsonPropertyName("schema")]
    public required string Schema { get; init; }

    [JsonPropertyName("generatedAt")]
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>
    /// NPM-style package dependencies (name → exact version) captured from a FHIR IG
    /// package's <c>package/package.json</c> at fetch time. Null for non-FHIR packages
    /// and for installs that predate dependency capture (reinstall to populate).
    /// Drives transitive dependency resolution so ValueSets bound from dependency
    /// packages (hl7.terminology.r4 etc.) can be installed and loaded.
    /// </summary>
    [JsonPropertyName("dependencies")]
    public IReadOnlyDictionary<string, string>? Dependencies { get; init; }
}
