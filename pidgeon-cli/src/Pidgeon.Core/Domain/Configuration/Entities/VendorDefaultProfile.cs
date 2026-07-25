// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Pidgeon.Core.Domain.Configuration.Entities;

/// <summary>
/// Vendor-specific default profile loaded from embedded JSON resources.
/// Captures field-level dialect rules, MRN formats, coding preferences, and Z-segment expectations.
/// </summary>
public record VendorDefaultProfile
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("vendor")]
    public string Vendor { get; init; } = "";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "vendor_patterns";

    [JsonPropertyName("applicable_standards")]
    public List<string> ApplicableStandards { get; init; } = new();

    [JsonPropertyName("field_defaults")]
    public Dictionary<string, string> FieldDefaults { get; init; } = new();

    [JsonPropertyName("field_patterns")]
    public Dictionary<string, FieldPatternRule> FieldPatterns { get; init; } = new();

    [JsonPropertyName("message_control_patterns")]
    public Dictionary<string, object> MessageControlPatterns { get; init; } = new();

    [JsonPropertyName("segment_ordering")]
    public Dictionary<string, List<string>> SegmentOrdering { get; init; } = new();

    [JsonPropertyName("time_formatting")]
    public TimeFormattingRules TimeFormatting { get; init; } = new();

    [JsonPropertyName("coding_systems")]
    public CodingSystemPreferences CodingSystems { get; init; } = new();

    [JsonPropertyName("z_segments")]
    public ZSegmentExpectations ZSegments { get; init; } = new();

    [JsonPropertyName("vendor_specific_features")]
    public Dictionary<string, object> VendorSpecificFeatures { get; init; } = new();

    [JsonPropertyName("smart_randomization")]
    public Dictionary<string, object> SmartRandomization { get; init; } = new();

    [JsonPropertyName("usage_notes")]
    public List<string> UsageNotes { get; init; } = new();
}

/// <summary>
/// Regex-based field validation pattern for vendor-specific field formatting.
/// </summary>
public record FieldPatternRule
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; init; } = "";

    [JsonPropertyName("example")]
    public string Example { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
}

/// <summary>
/// Vendor-specific time formatting preferences.
/// </summary>
public record TimeFormattingRules
{
    [JsonPropertyName("timestamp_format")]
    public string TimestampFormat { get; init; } = "YYYYMMDDHHMMSS";

    [JsonPropertyName("date_format")]
    public string DateFormat { get; init; } = "YYYYMMDD";

    [JsonPropertyName("time_format")]
    public string TimeFormat { get; init; } = "HHMM";

    [JsonPropertyName("timezone_handling")]
    public string TimezoneHandling { get; init; } = "local_no_offset";
}

/// <summary>
/// Vendor-preferred coding systems for diagnoses, procedures, and medications.
/// </summary>
public record CodingSystemPreferences
{
    [JsonPropertyName("diagnosis_codes")]
    public string DiagnosisCodes { get; init; } = "ICD10";

    [JsonPropertyName("procedure_codes")]
    public string ProcedureCodes { get; init; } = "CPT4";

    [JsonPropertyName("drug_codes")]
    public string DrugCodes { get; init; } = "NDC";

    [JsonPropertyName("provider_taxonomy")]
    public string ProviderTaxonomy { get; init; } = "NUCC";
}

/// <summary>
/// Vendor-specific Z-segment usage expectations.
/// </summary>
public record ZSegmentExpectations
{
    [JsonPropertyName("expected")]
    public List<string> Expected { get; init; } = new();

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
}
