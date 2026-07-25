// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Pidgeon.Core.Domain.Configuration.Common;

/// <summary>
/// Base record for all configuration entities requiring frequency analysis.
/// Provides common properties for frequency tracking, sample size, and confidence.
/// </summary>
public abstract record FrequencyAnalysisBase
{
    /// <summary>
    /// Number of times this item was populated (non-empty).
    /// </summary>
    [JsonPropertyName("populatedCount")]
    public int PopulatedCount { get; init; }

    /// <summary>
    /// Total number of times this item was observed.
    /// </summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }

    /// <summary>
    /// Frequency rate (0.0 to 1.0) - PopulatedCount / TotalCount.
    /// </summary>
    [JsonPropertyName("frequency")]
    public double Frequency { get; init; }

    /// <summary>
    /// Number of samples analyzed for this item.
    /// </summary>
    [JsonPropertyName("sampleSize")]
    public int SampleSize { get; init; }

    /// <summary>
    /// Confidence score for this analysis (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }
}

/// <summary>
/// Base record for all configuration entities requiring statistical analysis.
/// Extends frequency analysis with data quality metrics and dictionary collections.
/// </summary>
public abstract record StatisticalAnalysisBase : FrequencyAnalysisBase
{
    /// <summary>
    /// Count of unique values observed.
    /// </summary>
    [JsonPropertyName("uniqueValues")]
    public int UniqueValues { get; init; }

    /// <summary>
    /// Average length of values when populated.
    /// </summary>
    [JsonPropertyName("averageLength")]
    public double AverageLength { get; init; }

    /// <summary>
    /// Most common values found with their frequencies.
    /// </summary>
    [JsonPropertyName("commonValues")]
    public Dictionary<string, int> CommonValues { get; init; } = new();

    /// <summary>
    /// Additional context or metadata.
    /// </summary>
    [JsonPropertyName("context")]
    public Dictionary<string, string> Context { get; init; } = new();
}

/// <summary>
/// Base record for all configuration entities with temporal tracking.
/// Provides creation date, update tracking, and versioning.
/// </summary>
public abstract record TemporalConfigurationBase
{
    /// <summary>
    /// When this configuration was created.
    /// </summary>
    [JsonPropertyName("createdDate")]
    public DateTime CreatedDate { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When this configuration was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Configuration version string.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";
}

/// <summary>
/// Base record for all configuration entities with vendor detection results.
/// Provides standard vendor detection properties.
/// </summary>
public abstract record VendorDetectionBase
{
    /// <summary>
    /// Detected vendor/system name.
    /// </summary>
    [JsonPropertyName("detectedVendor")]
    public string DetectedVendor { get; init; } = string.Empty;

    /// <summary>
    /// Confidence in vendor detection (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("vendorConfidence")]
    public double VendorConfidence { get; init; }

    /// <summary>
    /// Detection method used.
    /// </summary>
    [JsonPropertyName("detectionMethod")]
    public string DetectionMethod { get; init; } = string.Empty;
}

/// <summary>
/// Base record for configuration entities needing both frequency analysis and temporal tracking.
/// Combines the most commonly needed configuration properties.
/// </summary>
public abstract record FullAnalysisBase : StatisticalAnalysisBase
{
    /// <summary>
    /// When this configuration was created.
    /// </summary>
    [JsonPropertyName("createdDate")]
    public DateTime CreatedDate { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When this configuration was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Configuration version string.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";
}