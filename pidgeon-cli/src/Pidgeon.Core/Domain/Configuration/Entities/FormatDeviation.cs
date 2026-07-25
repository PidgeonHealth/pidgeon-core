// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;
using Pidgeon.Core.Domain.Configuration.Common;

namespace Pidgeon.Core.Domain.Configuration.Entities;

/// <summary>
/// Represents a deviation from standard healthcare message format.
/// Captures vendor-specific formatting choices and non-standard implementations.
/// </summary>
public record FormatDeviation
{
    /// <summary>
    /// Type of format deviation detected.
    /// </summary>
    [JsonPropertyName("type")]
    public FormatDeviationType Type { get; init; }

    /// <summary>
    /// Human-readable description of the deviation.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// The actual value found in the message.
    /// </summary>
    [JsonPropertyName("detectedValue")]
    public string DetectedValue { get; init; } = string.Empty;

    /// <summary>
    /// The expected standard value.
    /// </summary>
    [JsonPropertyName("standardValue")]
    public string StandardValue { get; init; } = string.Empty;

    /// <summary>
    /// Location where the deviation was found (segment, field, etc.).
    /// </summary>
    [JsonPropertyName("location")]
    public string Location { get; init; } = string.Empty;

    /// <summary>
    /// Severity of the deviation's impact on interoperability.
    /// </summary>
    [JsonPropertyName("severity")]
    public DeviationSeverity Severity { get; init; }

    /// <summary>
    /// Frequency of this deviation in the analyzed sample.
    /// </summary>
    [JsonPropertyName("frequency")]
    public int Frequency { get; init; } = 1;

    /// <summary>
    /// Additional context or metadata about the deviation.
    /// </summary>
    [JsonPropertyName("context")]
    public Dictionary<string, string> Context { get; init; } = new();
}

/// <summary>
/// Types of format deviations that can be detected.
/// </summary>
public enum FormatDeviationType
{
    /// <summary>
    /// Non-standard encoding characters (field separators, escape sequences).
    /// </summary>
    EncodingVariation,

    /// <summary>
    /// Extra fields beyond the standard specification.
    /// </summary>
    ExtraFields,

    /// <summary>
    /// Missing required fields or segments.
    /// </summary>
    MissingFields,

    /// <summary>
    /// Non-standard segment ordering.
    /// </summary>
    SegmentOrdering,

    /// <summary>
    /// Custom or vendor-specific segments.
    /// </summary>
    CustomSegments,

    /// <summary>
    /// Non-standard data formats or value representations.
    /// </summary>
    DataFormatVariation,

    /// <summary>
    /// Message length or size variations.
    /// </summary>
    MessageStructure
}

/// <summary>
/// Severity levels for format deviations.
/// </summary>
public enum DeviationSeverity
{
    /// <summary>
    /// Informational - deviation noted but no impact on processing.
    /// </summary>
    Info,

    /// <summary>
    /// Warning - deviation may cause issues but message is processable.
    /// </summary>
    Warning,

    /// <summary>
    /// Error - deviation prevents proper message processing.
    /// </summary>
    Error,

    /// <summary>
    /// Critical - deviation breaks interoperability completely.
    /// </summary>
    Critical
}

/// <summary>
/// Analysis of the impact of format deviations on system interoperability.
/// </summary>
public record DeviationImpactAnalysis : TemporalConfigurationBase
{
    /// <summary>
    /// Overall impact score (0.0 = no impact, 1.0 = critical impact).
    /// </summary>
    [JsonPropertyName("overallImpactScore")]
    public double OverallImpactScore { get; init; }

    /// <summary>
    /// Number of deviations by severity level.
    /// </summary>
    [JsonPropertyName("severityCounts")]
    public Dictionary<DeviationSeverity, int> SeverityCounts { get; init; } = new();

    /// <summary>
    /// Most common deviation types found.
    /// </summary>
    [JsonPropertyName("commonDeviationTypes")]
    public Dictionary<FormatDeviationType, int> CommonDeviationTypes { get; init; } = new();

    /// <summary>
    /// Interoperability risk assessment.
    /// </summary>
    [JsonPropertyName("riskAssessment")]
    public InteroperabilityRisk RiskAssessment { get; init; }

    /// <summary>
    /// Recommendations for handling the detected deviations.
    /// </summary>
    [JsonPropertyName("recommendations")]
    public List<string> Recommendations { get; init; } = new();
}

/// <summary>
/// Risk levels for interoperability impact.
/// </summary>
public enum InteroperabilityRisk
{
    /// <summary>
    /// Low risk - deviations unlikely to affect message processing.
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk - some processing adjustments may be needed.
    /// </summary>
    Medium,

    /// <summary>
    /// High risk - significant compatibility issues likely.
    /// </summary>
    High,

    /// <summary>
    /// Critical risk - interoperability severely compromised.
    /// </summary>
    Critical
}

/// <summary>
/// Criteria used for vendor detection analysis.
/// </summary>
public record VendorDetectionCriteria : VendorDetectionBase
{
    /// <summary>
    /// Fields analyzed for vendor detection.
    /// </summary>
    [JsonPropertyName("analyzedFields")]
    public List<string> AnalyzedFields { get; init; } = new();

    /// <summary>
    /// Detection patterns that matched.
    /// </summary>
    [JsonPropertyName("matchedPatterns")]
    public List<string> MatchedPatterns { get; init; } = new();

    /// <summary>
    /// Confidence threshold used for detection.
    /// </summary>
    [JsonPropertyName("confidenceThreshold")]
    public double ConfidenceThreshold { get; init; } = 0.7;
}

/// <summary>
/// Statistical information about field patterns.
/// </summary>
public record FieldStatistics
{
    /// <summary>
    /// Total number of fields analyzed.
    /// </summary>
    [JsonPropertyName("totalFields")]
    public int TotalFields { get; init; }

    /// <summary>
    /// Number of fields with consistent population patterns.
    /// </summary>
    [JsonPropertyName("consistentFields")]
    public int ConsistentFields { get; init; }

    /// <summary>
    /// Average field frequency rate across all fields.
    /// </summary>
    [JsonPropertyName("averageFrequency")]
    public double AverageFrequency { get; init; }

    /// <summary>
    /// Fields with highest frequency rates.
    /// </summary>
    [JsonPropertyName("mostPopulatedFields")]
    public Dictionary<string, double> MostPopulatedFields { get; init; } = new();

    /// <summary>
    /// Fields with lowest frequency rates.
    /// </summary>
    [JsonPropertyName("leastPopulatedFields")]
    public Dictionary<string, double> LeastPopulatedFields { get; init; } = new();

    /// <summary>
    /// Data quality score (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("dataQualityScore")]
    public double DataQualityScore { get; init; }
}