// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Configuration.Entities;

/// <summary>
/// Represents a lock session for maintaining consistent values across multiple message generations.
/// Enables workflow automation and incremental testing scenarios.
/// </summary>
public record LockSession
{
    /// <summary>
    /// Unique identifier for this lock session.
    /// </summary>
    public string SessionId { get; init; } = "";

    /// <summary>
    /// User-friendly name for the session (e.g., "patient_workflow", "john_doe_journey").
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Optional description of the session's purpose.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Timestamp when the session was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the session was last accessed or modified.
    /// </summary>
    public DateTime LastAccessedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the session expires (for TTL cleanup).
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// The scope of values locked in this session.
    /// </summary>
    public LockScope Scope { get; init; } = LockScope.Patient;

    /// <summary>
    /// Collection of locked values in this session.
    /// </summary>
    public IReadOnlyList<LockValue> LockedValues { get; init; } = [];

    /// <summary>
    /// Metadata associated with this session.
    /// </summary>
    public LockMetadata Metadata { get; init; } = new();

    /// <summary>
    /// Whether this session is currently active.
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Tags for organizing and filtering sessions.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}