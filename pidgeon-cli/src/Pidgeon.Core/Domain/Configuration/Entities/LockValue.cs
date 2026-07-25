// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Configuration.Entities;

/// <summary>
/// Represents a single locked field value within a lock session.
/// Maintains the path, value, and constraints for consistent message generation.
/// </summary>
public record LockValue
{
    /// <summary>
    /// The field path this value is locked for (e.g., "patient.mrn", "PID.3.1", "Patient.identifier[0].value").
    /// </summary>
    public string FieldPath { get; init; } = "";

    /// <summary>
    /// The locked value for this field.
    /// </summary>
    public string Value { get; init; } = "";

    /// <summary>
    /// The data type of this field for validation purposes.
    /// </summary>
    public string? DataType { get; init; }

    /// <summary>
    /// Timestamp when this value was locked.
    /// </summary>
    public DateTime LockedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Optional reason or context for why this value was locked.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Whether this value was explicitly set by the user or derived from a template.
    /// </summary>
    public bool IsExplicit { get; init; } = true;

    /// <summary>
    /// Source template or pattern this value came from, if applicable.
    /// </summary>
    public string? SourceTemplate { get; init; }

    /// <summary>
    /// Additional constraints or validation rules for this field.
    /// </summary>
    public Dictionary<string, object> Constraints { get; init; } = new();

    /// <summary>
    /// Whether this value should propagate to related fields automatically.
    /// </summary>
    public bool AutoPropagate { get; init; } = true;

    /// <summary>
    /// Priority level for value resolution conflicts (higher wins).
    /// </summary>
    public int Priority { get; init; } = 100;
}