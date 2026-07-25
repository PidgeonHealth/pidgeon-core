// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Configuration.Entities;

/// <summary>
/// Metadata associated with a lock session, including usage statistics and configuration.
/// </summary>
public record LockMetadata
{
    /// <summary>
    /// Total number of messages generated using this lock session.
    /// </summary>
    public int MessagesGenerated { get; init; } = 0;

    /// <summary>
    /// Healthcare standard this session is primarily used with (e.g., "hl7v23", "fhir-r4").
    /// </summary>
    public string? PrimaryStandard { get; init; }

    /// <summary>
    /// Message types that have been generated with this session.
    /// </summary>
    public IReadOnlyList<string> MessageTypes { get; init; } = [];

    /// <summary>
    /// Last message type generated with this session.
    /// </summary>
    public string? LastMessageType { get; init; }

    /// <summary>
    /// Number of times values have been modified in this session.
    /// </summary>
    public int ModificationCount { get; init; } = 0;

    /// <summary>
    /// Source of the initial values (template, manual, imported).
    /// </summary>
    public string? ValueSource { get; init; }

    /// <summary>
    /// Version of the lock format for migration compatibility.
    /// </summary>
    public string FormatVersion { get; init; } = "1.0";

    /// <summary>
    /// Custom user-defined properties for extensibility.
    /// </summary>
    public Dictionary<string, object> CustomProperties { get; init; } = new();

    /// <summary>
    /// Audit trail of significant changes to this session.
    /// </summary>
    public IReadOnlyList<LockAuditEntry> AuditTrail { get; init; } = [];
}

/// <summary>
/// Represents an audit entry for tracking changes to lock sessions.
/// </summary>
public record LockAuditEntry
{
    /// <summary>
    /// Timestamp of the action.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Type of action performed (created, modified, value_set, value_removed, etc.).
    /// </summary>
    public string Action { get; init; } = "";

    /// <summary>
    /// Field path affected by this action, if applicable.
    /// </summary>
    public string? FieldPath { get; init; }

    /// <summary>
    /// Previous value before the change, if applicable.
    /// </summary>
    public string? OldValue { get; init; }

    /// <summary>
    /// New value after the change, if applicable.
    /// </summary>
    public string? NewValue { get; init; }

    /// <summary>
    /// Additional context or reason for the change.
    /// </summary>
    public string? Context { get; init; }
}