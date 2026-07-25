// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Configuration.Entities;
using Pidgeon.Core;

namespace Pidgeon.Core.Application.Interfaces.Configuration;

/// <summary>
/// Storage provider interface for persisting lock sessions.
/// Enables pluggable storage backends (file system, database, cloud, etc.).
/// </summary>
public interface ILockStorageProvider
{
    /// <summary>
    /// Saves a lock session to persistent storage.
    /// </summary>
    /// <param name="session">Session to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> SaveSessionAsync(
        LockSession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a lock session from persistent storage by name.
    /// </summary>
    /// <param name="sessionName">Name of the session to load</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the loaded session if found</returns>
    Task<Result<LockSession>> LoadSessionAsync(
        string sessionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all session names available in storage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the list of session names</returns>
    Task<Result<IReadOnlyList<string>>> ListSessionNamesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a lock session from persistent storage.
    /// </summary>
    /// <param name="sessionName">Name of the session to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> DeleteSessionAsync(
        string sessionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a session exists in storage.
    /// </summary>
    /// <param name="sessionName">Name of the session to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating whether the session exists</returns>
    Task<Result<bool>> SessionExistsAsync(
        string sessionName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes the storage provider (creates directories, tables, etc.).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> InitializeAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs cleanup operations (expired sessions, orphaned files, etc.).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing the number of items cleaned up</returns>
    Task<Result<int>> CleanupAsync(
        CancellationToken cancellationToken = default);
}