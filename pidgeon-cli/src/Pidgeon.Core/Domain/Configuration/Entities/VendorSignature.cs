// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json.Serialization;

namespace Pidgeon.Core.Domain.Configuration.Entities;

/// <summary>
/// Represents a detected vendor signature from HL7 message analysis.
/// Contains identifying information and confidence metrics for vendor detection.
/// </summary>
public record VendorSignature
{
    /// <summary>
    /// Detected vendor name (e.g., "Epic", "Cerner", "AllScripts").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = default!;

    /// <summary>
    /// Optional vendor version information.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>
    /// Confidence level in vendor detection (0.0 to 1.0).
    /// </summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>
    /// Sending application from MSH.3 field.
    /// </summary>
    [JsonPropertyName("sendingApplication")]
    public string SendingApplication { get; init; } = default!;

    /// <summary>
    /// Sending facility from MSH.4 field.
    /// </summary>
    [JsonPropertyName("sendingFacility")]
    public string? SendingFacility { get; init; }

    /// <summary>
    /// HL7 encoding characters from MSH.2 field.
    /// </summary>
    [JsonPropertyName("encodingCharacters")]
    public string EncodingCharacters { get; init; } = @"^~\&";

    /// <summary>
    /// HL7 field separator character.
    /// </summary>
    [JsonPropertyName("fieldSeparator")]
    public char FieldSeparator { get; init; } = '|';

    /// <summary>
    /// List of format deviations from the healthcare standard.
    /// </summary>
    [JsonPropertyName("deviations")]
    public List<FormatDeviation> Deviations { get; init; } = new();

    /// <summary>
    /// Timestamp when this vendor signature was detected.
    /// </summary>
    [JsonPropertyName("detectedTimestamp")]
    public DateTime DetectedTimestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Method used to detect this vendor signature.
    /// </summary>
    [JsonPropertyName("detectionMethod")]
    public string DetectionMethod { get; init; } = string.Empty;

    /// <summary>
    /// Identifier of the specific <c>VendorDetectionPattern</c> that matched
    /// (e.g. <c>epic-er-orm</c>, <c>cerner-pharm-newrx</c>). Null when no
    /// named pattern matched (unknown-vendor fallback path). Downstream
    /// consumers — notably <c>ChannelRecognitionState.MatchedProfile</c> —
    /// surface this so a dashboard can link a recognized channel to the
    /// exact curated profile that claimed it, not just to the vendor name.
    /// </summary>
    [JsonPropertyName("matchedPatternId")]
    public string? MatchedPatternId { get; init; }
}