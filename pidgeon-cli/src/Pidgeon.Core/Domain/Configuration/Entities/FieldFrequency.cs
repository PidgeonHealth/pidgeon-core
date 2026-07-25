// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;
using Pidgeon.Core.Domain.Configuration.Common;

namespace Pidgeon.Core.Domain.Configuration.Entities;

/// <summary>
/// Represents frequency analysis for a specific field in healthcare messages.
/// Tracks how often a field is populated and common values found.
/// </summary>
public record FieldFrequency : StatisticalAnalysisBase
{
    /// <summary>
    /// Field position index (1-based for HL7).
    /// </summary>
    [JsonPropertyName("fieldIndex")]
    public int FieldIndex { get; set; }

    /// <summary>
    /// Field name for identification.
    /// </summary>
    [JsonPropertyName("fieldName")]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Component patterns found in this field (for composite fields).
    /// </summary>
    [JsonPropertyName("componentPatterns")]
    public Dictionary<string, ComponentPattern> ComponentPatterns { get; set; } = new();
}

/// <summary>
/// Represents frequency analysis for a component within a composite field.
/// </summary>
public record ComponentFrequency : StatisticalAnalysisBase
{
    /// <summary>
    /// Component position index (0-based).
    /// </summary>
    [JsonPropertyName("componentIndex")]
    public int ComponentIndex { get; set; }

    /// <summary>
    /// Component name for identification.
    /// </summary>
    [JsonPropertyName("componentName")]
    public string ComponentName { get; set; } = string.Empty;
}

/// <summary>
/// Represents component structure patterns within composite fields.
/// </summary>
public record ComponentPattern : FrequencyAnalysisBase
{
    /// <summary>
    /// Type of field being analyzed (e.g., "XPN", "XAD", "CE").
    /// </summary>
    [JsonPropertyName("fieldType")]
    public string FieldType { get; init; } = string.Empty;

    /// <summary>
    /// Component frequency analysis by position.
    /// </summary>
    [JsonPropertyName("componentFrequencies")]
    public Dictionary<int, ComponentFrequency> ComponentFrequencies { get; init; } = new();

    /// <summary>
    /// Standard name for this pattern.
    /// </summary>
    [JsonPropertyName("standardName")]
    public string StandardName { get; init; } = string.Empty;
}

/// <summary>
/// Represents a segment's field population patterns.
/// </summary>
public record SegmentPattern : FrequencyAnalysisBase
{
    /// <summary>
    /// Segment identifier (e.g., "PID", "MSH", "OBR").
    /// </summary>
    [JsonPropertyName("segmentId")]
    public string SegmentId { get; init; } = string.Empty;

    /// <summary>
    /// Segment type name.
    /// </summary>
    [JsonPropertyName("segmentType")]
    public string SegmentType { get; init; } = string.Empty;

    /// <summary>
    /// Field frequency analysis by field position.
    /// </summary>
    [JsonPropertyName("fields")]
    public Dictionary<int, FieldFrequency> Fields { get; init; } = new();

    /// <summary>
    /// Field frequencies by field position (duplicate - kept for compatibility).
    /// </summary>
    [JsonPropertyName("fieldFrequencies")]
    public Dictionary<int, FieldFrequency> FieldFrequencies { get; set; } = new();
}