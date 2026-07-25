// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Configuration;

/// <summary>
/// Represents field-level constraints for healthcare standards.
/// Standard-agnostic model used across all constraint resolver plugins.
/// </summary>
public record FieldConstraints
{
    /// <summary>
    /// Data type (e.g., ST, NM, DT for HL7; string, integer for FHIR)
    /// </summary>
    public string? DataType { get; init; }

    /// <summary>
    /// Reference to a coded value table (e.g., "0001" for HL7 Admin Sex)
    /// </summary>
    public string? TableReference { get; init; }

    /// <summary>
    /// Maximum field length
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Minimum field length
    /// </summary>
    public int? MinLength { get; init; }

    /// <summary>
    /// Whether the field is required
    /// </summary>
    public bool Required { get; init; }

    /// <summary>
    /// Whether the field can have multiple values
    /// </summary>
    public bool Repeating { get; init; }

    /// <summary>
    /// Regular expression pattern for validation
    /// </summary>
    public string? Pattern { get; init; }

    /// <summary>
    /// Explicit list of allowed values
    /// </summary>
    public List<string>? AllowedValues { get; init; }

    /// <summary>
    /// Numeric constraints
    /// </summary>
    public NumericConstraints? Numeric { get; init; }

    /// <summary>
    /// Date/time constraints
    /// </summary>
    public DateTimeConstraints? DateTime { get; init; }

    /// <summary>
    /// Cross-field dependencies
    /// </summary>
    public List<FieldDependency>? Dependencies { get; init; }

    /// <summary>
    /// Gets a hash code for efficient caching
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(
            DataType,
            TableReference,
            MaxLength,
            MinLength,
            Required,
            Repeating,
            Pattern,
            AllowedValues?.Count);
    }
}

/// <summary>
/// Numeric field constraints
/// </summary>
public record NumericConstraints(
    decimal? MinValue,
    decimal? MaxValue,
    int? Precision,
    int? Scale);

/// <summary>
/// Date/time field constraints
/// </summary>
public record DateTimeConstraints(
    DateTime? MinDate,
    DateTime? MaxDate,
    string? Format);

/// <summary>
/// Cross-field dependency definition
/// </summary>
public record FieldDependency(
    string DependentField,
    string Condition,
    object RequiredValue);