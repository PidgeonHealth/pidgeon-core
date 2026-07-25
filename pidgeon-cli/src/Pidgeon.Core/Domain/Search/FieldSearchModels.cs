// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Search;

/// <summary>
/// Result of a field search operation across healthcare standards.
/// </summary>
public class FieldSearchResult
{
    /// <summary>
    /// Field locations found across different standards.
    /// </summary>
    public List<FieldLocation> Locations { get; set; } = new();

    /// <summary>
    /// Related fields that might also be relevant.
    /// </summary>
    public List<CrossReference> RelatedFields { get; set; } = new();

    /// <summary>
    /// Suggestions for alternative searches if no results found.
    /// </summary>
    public List<string> Suggestions { get; set; } = new();

    /// <summary>
    /// Search performance metrics.
    /// </summary>
    public SearchMetrics Metrics { get; set; } = new();
}

/// <summary>
/// Represents a field's location within a specific healthcare standard.
/// </summary>
public class FieldLocation
{
    /// <summary>
    /// Healthcare standard (HL7v23, FHIR-R4, NCPDP).
    /// </summary>
    public string Standard { get; init; } = "";

    /// <summary>
    /// Field path (PID.5, Patient.name, etc).
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// Human-readable field description.
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// Message or resource type containing this field.
    /// </summary>
    public string MessageType { get; init; } = "";

    /// <summary>
    /// Data type of the field.
    /// </summary>
    public string DataType { get; init; } = "";

    /// <summary>
    /// Usage requirement (Required/Optional/Conditional).
    /// </summary>
    public string Usage { get; init; } = "";

    /// <summary>
    /// Example values for this field.
    /// </summary>
    public List<string> Examples { get; init; } = new();
}

/// <summary>
/// Mapping of a field across different healthcare standards.
/// </summary>
public class CrossStandardMapping
{
    /// <summary>
    /// Source field being mapped.
    /// </summary>
    public FieldLocation SourceField { get; set; } = new();

    /// <summary>
    /// Target fields in other standards.
    /// </summary>
    public List<MappedField> TargetFields { get; set; } = new();

    /// <summary>
    /// Overall quality of the mapping.
    /// </summary>
    public MappingQuality Quality { get; set; }
}

/// <summary>
/// A field mapped to another standard.
/// </summary>
public class MappedField : FieldLocation
{
    /// <summary>
    /// Type of mapping relationship.
    /// </summary>
    public MappingType Type { get; init; }

    /// <summary>
    /// Confidence level of the mapping (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Notes about the mapping relationship.
    /// </summary>
    public string Notes { get; init; } = "";
}

/// <summary>
/// Cross-reference to a related field.
/// </summary>
public class CrossReference
{
    /// <summary>
    /// Standard containing the referenced field.
    /// </summary>
    public string Standard { get; init; } = "";

    /// <summary>
    /// Path to the referenced field.
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// Relationship to the source field.
    /// </summary>
    public string Relationship { get; init; } = "";
}

/// <summary>
/// Location where a value was found in a message file.
/// </summary>
public class ValueLocation
{
    /// <summary>
    /// File containing the value.
    /// </summary>
    public string FileName { get; init; } = "";

    /// <summary>
    /// Field path where value was found.
    /// </summary>
    public string FieldPath { get; init; } = "";

    /// <summary>
    /// Description of the field containing the value.
    /// </summary>
    public string FieldDescription { get; init; } = "";

    /// <summary>
    /// Healthcare standard of the message.
    /// </summary>
    public string Standard { get; init; } = "";

    /// <summary>
    /// Message type (ADT^A01, Patient, etc).
    /// </summary>
    public string MessageType { get; init; } = "";

    /// <summary>
    /// Data type of the field.
    /// </summary>
    public string DataType { get; init; } = "";

    /// <summary>
    /// Line number in the file (for text formats).
    /// </summary>
    public int? LineNumber { get; init; }
}

/// <summary>
/// Search performance metrics.
/// </summary>
public class SearchMetrics
{
    /// <summary>
    /// Time taken to execute the search.
    /// </summary>
    public TimeSpan SearchTime { get; init; }

    /// <summary>
    /// Number of standards searched.
    /// </summary>
    public int StandardsSearched { get; init; }

    /// <summary>
    /// Number of fields examined.
    /// </summary>
    public int FieldsExamined { get; init; }

    /// <summary>
    /// Number of results found.
    /// </summary>
    public int ResultsFound { get; init; }

    /// <summary>
    /// Whether results were served from cache.
    /// </summary>
    public bool FromCache { get; init; }
}

/// <summary>
/// Type of pattern matching to use.
/// </summary>
public enum PatternType
{
    /// <summary>
    /// Simple wildcard matching (* and ?).
    /// </summary>
    Wildcard,

    /// <summary>
    /// Regular expression matching.
    /// </summary>
    Regex,

    /// <summary>
    /// Fuzzy matching for typo tolerance.
    /// </summary>
    Fuzzy
}

/// <summary>
/// Type of field mapping relationship.
/// </summary>
public enum MappingType
{
    /// <summary>
    /// Direct 1:1 mapping.
    /// </summary>
    Direct,

    /// <summary>
    /// Requires filtering or conditions.
    /// </summary>
    Filtered,

    /// <summary>
    /// Conceptually similar but not exact.
    /// </summary>
    Conceptual,

    /// <summary>
    /// No equivalent field.
    /// </summary>
    None
}

/// <summary>
/// Quality of cross-standard mapping.
/// </summary>
public enum MappingQuality
{
    /// <summary>
    /// Perfect mapping with no data loss.
    /// </summary>
    Excellent,

    /// <summary>
    /// Good mapping with minimal differences.
    /// </summary>
    Good,

    /// <summary>
    /// Fair mapping with some differences.
    /// </summary>
    Fair,

    /// <summary>
    /// Poor mapping with significant differences.
    /// </summary>
    Poor
}