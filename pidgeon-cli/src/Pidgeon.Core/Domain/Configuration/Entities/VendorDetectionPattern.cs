// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Pidgeon.Core.Domain.Configuration.Entities;

/// <summary>
/// JSON-configurable vendor detection pattern.
/// Similar to MCP tool definitions - configuration-driven, not code-driven.
/// Stored in vendor/*.json files for runtime configuration.
/// </summary>
public record VendorDetectionPattern
{
    /// <summary>
    /// Unique identifier for this vendor pattern.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name for the vendor (e.g., "Epic Systems", "Cerner Corporation").
    /// </summary>
    [JsonPropertyName("vendorName")]
    public string VendorName { get; init; } = string.Empty;

    /// <summary>
    /// Description of this vendor and their typical patterns.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Healthcare standards this pattern applies to.
    /// </summary>
    [JsonPropertyName("supportedStandards")]
    public List<string> SupportedStandards { get; init; } = new();

    /// <summary>
    /// Patterns to match in sending application field (MSH.3 for HL7).
    /// Regular expressions or exact string matches.
    /// </summary>
    [JsonPropertyName("applicationPatterns")]
    public List<DetectionRule> ApplicationPatterns { get; init; } = new();

    /// <summary>
    /// Patterns to match in sending facility field (MSH.4 for HL7).
    /// Regular expressions or exact string matches.
    /// </summary>
    [JsonPropertyName("facilityPatterns")]
    public List<DetectionRule> FacilityPatterns { get; init; } = new();

    /// <summary>
    /// Patterns to match in message type field.
    /// </summary>
    [JsonPropertyName("messageTypePatterns")]
    public List<DetectionRule> MessageTypePatterns { get; init; } = new();

    /// <summary>
    /// Additional field patterns for advanced detection.
    /// </summary>
    [JsonPropertyName("additionalPatterns")]
    public Dictionary<string, DetectionRule> AdditionalPatterns { get; init; } = new();

    /// <summary>
    /// Base confidence when this pattern matches.
    /// </summary>
    [JsonPropertyName("baseConfidence")]
    public double BaseConfidence { get; init; } = 0.8;

    /// <summary>
    /// Whether this pattern is officially validated by the vendor.
    /// </summary>
    [JsonPropertyName("vendorValidated")]
    public bool VendorValidated { get; init; }

    /// <summary>
    /// Version of this pattern definition.
    /// </summary>
    [JsonPropertyName("patternVersion")]
    public string PatternVersion { get; init; } = "1.0";

    /// <summary>
    /// Common format deviations for this vendor.
    /// </summary>
    [JsonPropertyName("commonDeviations")]
    public List<CommonDeviation> CommonDeviations { get; init; } = new();
}

/// <summary>
/// Detection rule for pattern matching.
/// </summary>
public record DetectionRule
{
    /// <summary>
    /// Type of matching to perform.
    /// </summary>
    [JsonPropertyName("matchType")]
    public MatchType MatchType { get; init; }

    /// <summary>
    /// Pattern or value to match against.
    /// </summary>
    [JsonPropertyName("pattern")]
    public string Pattern { get; init; } = string.Empty;

    /// <summary>
    /// Whether this match is case sensitive.
    /// </summary>
    [JsonPropertyName("caseSensitive")]
    public bool CaseSensitive { get; init; }

    /// <summary>
    /// Confidence boost when this rule matches.
    /// </summary>
    [JsonPropertyName("confidenceBoost")]
    public double ConfidenceBoost { get; init; } = 0.0;

    /// <summary>
    /// Description of what this rule detects.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Types of pattern matching.
/// </summary>
public enum MatchType
{
    /// <summary>
    /// Exact string match.
    /// </summary>
    [JsonPropertyName("exact")]
    Exact,

    /// <summary>
    /// Regular expression match.
    /// </summary>
    [JsonPropertyName("regex")]
    Regex,

    /// <summary>
    /// String contains match.
    /// </summary>
    [JsonPropertyName("contains")]
    Contains,

    /// <summary>
    /// String starts with match.
    /// </summary>
    [JsonPropertyName("startsWith")]
    StartsWith,

    /// <summary>
    /// String ends with match.
    /// </summary>
    [JsonPropertyName("endsWith")]
    EndsWith
}

/// <summary>
/// Common format deviation pattern for a vendor.
/// </summary>
public record CommonDeviation
{
    /// <summary>
    /// Type of deviation commonly seen.
    /// </summary>
    [JsonPropertyName("deviationType")]
    public FormatDeviationType DeviationType { get; init; }

    /// <summary>
    /// Location where this deviation typically occurs.
    /// </summary>
    [JsonPropertyName("location")]
    public string Location { get; init; } = string.Empty;

    /// <summary>
    /// Description of the deviation pattern.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Typical severity of this deviation.
    /// </summary>
    [JsonPropertyName("severity")]
    public DeviationSeverity Severity { get; init; }

    /// <summary>
    /// How frequently this deviation occurs (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("frequency")]
    public double Frequency { get; init; }
}