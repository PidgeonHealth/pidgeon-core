// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Clinical.Entities;

/// <summary>
/// Represents a clinical observation result such as lab values, vital signs, or diagnostic findings.
/// Domain entity for healthcare observations independent of message format.
/// </summary>
public class ObservationResult
{
    /// <summary>
    /// Unique identifier for this observation result.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Patient who is the subject of this observation.
    /// </summary>
    public Patient? Patient { get; set; }

    /// <summary>
    /// Code identifying what was observed (e.g., LOINC code).
    /// </summary>
    public string? ObservationCode { get; set; }

    /// <summary>
    /// Human-readable description of what was observed.
    /// </summary>
    public string? ObservationDescription { get; set; }

    /// <summary>
    /// The actual result value as a string.
    /// Can represent numeric, text, or coded values.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// HL7 value type code (NM=Numeric, TX=Text, CE=Coded Element, etc.).
    /// </summary>
    public string? ValueType { get; set; }

    /// <summary>
    /// Units of measurement for the value (e.g., "mg/dL", "mmHg").
    /// </summary>
    public string? Units { get; set; }

    /// <summary>
    /// Reference range for normal values (e.g., "70-110").
    /// </summary>
    public string? ReferenceRange { get; set; }

    /// <summary>
    /// Abnormal flags (e.g., "H" for High, "L" for Low, "A" for Abnormal).
    /// </summary>
    public string? AbnormalFlags { get; set; }

    /// <summary>
    /// Status of the result (F=Final, P=Preliminary, C=Corrected, etc.).
    /// </summary>
    public string? ResultStatus { get; set; }

    /// <summary>
    /// When the observation was made.
    /// </summary>
    public DateTime? ObservationDateTime { get; set; }

    /// <summary>
    /// Provider who performed or ordered the observation.
    /// </summary>
    public Provider? Provider { get; set; }

    /// <summary>
    /// Coding system used for the observation code (e.g., "LN" for LOINC).
    /// </summary>
    public string? CodingSystem { get; set; }

    /// <summary>
    /// Category of observation (e.g., "LAB", "VITAL", "IMAGING").
    /// </summary>
    public string? Category { get; set; }
}