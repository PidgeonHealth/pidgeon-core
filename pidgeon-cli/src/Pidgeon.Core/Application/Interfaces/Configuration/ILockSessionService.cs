// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Configuration.Entities;
using Pidgeon.Core;

namespace Pidgeon.Core.Application.Interfaces.Configuration;

/// <summary>
/// Service for managing lock sessions that maintain consistent values across message generations.
/// Core interface for implementing workflow automation and incremental testing scenarios.
/// </summary>
public interface ILockSessionService
{
    /// <summary>
    /// Creates a new lock session with the specified name and scope.
    /// </summary>
    /// <param name="name">User-friendly name for the session</param>
    /// <param name="scope">Scope of values to be locked</param>
    /// <param name="description">Optional description of the session's purpose</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="initialValues">Optional field values to seed the session with at creation, keyed by field path.
    /// Placed after the token to keep existing positional callers source-compatible.</param>
    /// <returns>Result containing the created lock session</returns>
    Task<Result<LockSession>> CreateSessionAsync(
        string name,
        LockScope scope,
        string? description = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? initialValues = null);

    /// <summary>
    /// Retrieves an existing lock session by name.
    /// </summary>
    /// <param name="sessionName">Name of the session to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the lock session if found</returns>
    Task<Result<LockSession>> GetSessionAsync(
        string sessionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all active lock sessions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the list of active sessions</returns>
    Task<Result<IReadOnlyList<LockSession>>> ListSessionsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the metadata or properties of an existing session.
    /// </summary>
    /// <param name="sessionName">Name of the session to update</param>
    /// <param name="updateAction">Action to perform on the session</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the updated session</returns>
    Task<Result<LockSession>> UpdateSessionAsync(
        string sessionName,
        Func<LockSession, LockSession> updateAction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a session, optionally updating its description. When the new name differs
    /// from the current one the session is re-keyed in storage (saved under the new name,
    /// old name removed), guarding against clobbering an existing session of that name.
    /// A new name equal to the current name is a description-only update.
    /// </summary>
    /// <param name="currentName">Existing session name</param>
    /// <param name="newName">Desired name; equal to currentName ⇒ description-only update</param>
    /// <param name="description">Optional new description; null keeps the existing one</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the renamed session</returns>
    Task<Result<LockSession>> RenameSessionAsync(
        string currentName,
        string newName,
        string? description = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a lock session and all its associated values.
    /// </summary>
    /// <param name="sessionName">Name of the session to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> RemoveSessionAsync(
        string sessionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a locked value in the specified session.
    /// </summary>
    /// <param name="sessionName">Name of the session</param>
    /// <param name="fieldPath">Path to the field to lock</param>
    /// <param name="value">Value to lock for this field</param>
    /// <param name="reason">Optional reason for locking this value</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> SetValueAsync(
        string sessionName,
        string fieldPath,
        string value,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a locked value from the specified session.
    /// </summary>
    /// <param name="sessionName">Name of the session</param>
    /// <param name="fieldPath">Path to the field to unlock</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> RemoveValueAsync(
        string sessionName,
        string fieldPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all locked values for a session in a format suitable for generation.
    /// </summary>
    /// <param name="sessionName">Name of the session</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the locked values dictionary</returns>
    Task<Result<IReadOnlyDictionary<string, string>>> GetLockedValuesAsync(
        string sessionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired sessions based on TTL settings.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the number of sessions cleaned up</returns>
    Task<Result<int>> CleanupExpiredSessionsAsync(
        CancellationToken cancellationToken = default);
}