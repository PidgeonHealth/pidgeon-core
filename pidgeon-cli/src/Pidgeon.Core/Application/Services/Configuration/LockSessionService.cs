// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Domain.Configuration.Entities;
using Pidgeon.Core;

namespace Pidgeon.Core.Application.Services.Configuration;

/// <summary>
/// Service for managing lock sessions that maintain consistent values across message generations.
/// Implements workflow automation and incremental testing scenarios.
/// </summary>
public class LockSessionService : ILockSessionService
{
    private readonly ILockStorageProvider _storageProvider;
    private readonly ILogger<LockSessionService> _logger;

    public LockSessionService(
        ILockStorageProvider storageProvider,
        ILogger<LockSessionService> logger)
    {
        _storageProvider = storageProvider;
        _logger = logger;
    }

    public async Task<Result<LockSession>> CreateSessionAsync(
        string name,
        LockScope scope,
        string? description = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? initialValues = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Result<LockSession>.Failure("Session name cannot be empty");
            }

            // Check if session already exists
            var existsResult = await _storageProvider.SessionExistsAsync(name, cancellationToken);
            if (existsResult.IsFailure)
            {
                return Result<LockSession>.Failure($"Failed to check session existence: {existsResult.Error.Message}");
            }

            if (existsResult.Value)
            {
                return Result<LockSession>.Failure($"Session '{name}' already exists");
            }

            var session = new LockSession
            {
                SessionId = Guid.NewGuid().ToString(),
                Name = name,
                Description = description,
                Scope = scope,
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30), // Default 30-day TTL
                IsActive = true,
                // "Save current pins as…" seeds the session at creation — one save, no
                // partial-write window from a create followed by N set-value calls.
                LockedValues = initialValues is { Count: > 0 }
                    ? initialValues.Select(kv => new LockValue { FieldPath = kv.Key, Value = kv.Value }).ToList()
                    : [],
                Metadata = new LockMetadata
                {
                    FormatVersion = "1.0",
                    ValueSource = "manual",
                    AuditTrail = [
                        new LockAuditEntry
                        {
                            Action = "created",
                            Context = $"Session created with scope: {scope}"
                        }
                    ]
                }
            };

            var saveResult = await _storageProvider.SaveSessionAsync(session, cancellationToken);
            if (saveResult.IsFailure)
            {
                return Result<LockSession>.Failure($"Failed to save session: {saveResult.Error.Message}");
            }

            _logger.LogInformation("Created lock session: {SessionName} with scope: {Scope}", name, scope);
            return Result<LockSession>.Success(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create lock session: {SessionName}", name);
            return Result<LockSession>.Failure($"Failed to create session: {ex.Message}");
        }
    }

    public async Task<Result<LockSession>> GetSessionAsync(
        string sessionName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var loadResult = await _storageProvider.LoadSessionAsync(sessionName, cancellationToken);
            if (loadResult.IsFailure)
            {
                return loadResult;
            }

            var session = loadResult.Value;

            // Update last accessed time
            var updatedSession = session with
            {
                LastAccessedAt = DateTime.UtcNow
            };

            await _storageProvider.SaveSessionAsync(updatedSession, cancellationToken);

            return Result<LockSession>.Success(updatedSession);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get lock session: {SessionName}", sessionName);
            return Result<LockSession>.Failure($"Failed to get session: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<LockSession>>> ListSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionNamesResult = await _storageProvider.ListSessionNamesAsync(cancellationToken);
            if (sessionNamesResult.IsFailure)
            {
                return Result<IReadOnlyList<LockSession>>.Failure(sessionNamesResult.Error.Message);
            }

            var sessions = new List<LockSession>();

            foreach (var sessionName in sessionNamesResult.Value)
            {
                var sessionResult = await _storageProvider.LoadSessionAsync(sessionName, cancellationToken);
                if (sessionResult.IsSuccess)
                {
                    sessions.Add(sessionResult.Value);
                }
                else
                {
                    _logger.LogWarning("Failed to load session during listing: {SessionName}", sessionName);
                }
            }

            return Result<IReadOnlyList<LockSession>>.Success(sessions.AsReadOnly());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list lock sessions");
            return Result<IReadOnlyList<LockSession>>.Failure($"Failed to list sessions: {ex.Message}");
        }
    }

    public async Task<Result<LockSession>> UpdateSessionAsync(
        string sessionName,
        Func<LockSession, LockSession> updateAction,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionResult = await _storageProvider.LoadSessionAsync(sessionName, cancellationToken);
            if (sessionResult.IsFailure)
            {
                return sessionResult;
            }

            var updatedSession = updateAction(sessionResult.Value);

            var saveResult = await _storageProvider.SaveSessionAsync(updatedSession, cancellationToken);
            if (saveResult.IsFailure)
            {
                return Result<LockSession>.Failure($"Failed to save updated session: {saveResult.Error.Message}");
            }

            return Result<LockSession>.Success(updatedSession);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update lock session: {SessionName}", sessionName);
            return Result<LockSession>.Failure($"Failed to update session: {ex.Message}");
        }
    }

    public async Task<Result<LockSession>> RenameSessionAsync(
        string currentName,
        string newName,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                return Result<LockSession>.Failure("Session name cannot be empty");
            }

            var loadResult = await _storageProvider.LoadSessionAsync(currentName, cancellationToken);
            if (loadResult.IsFailure)
            {
                return loadResult;
            }

            var isRename = !string.Equals(currentName, newName, StringComparison.Ordinal);
            if (isRename)
            {
                // Storage keys by name and SaveSession would happily overwrite a different
                // session that already owns newName — guard before re-keying.
                var existsResult = await _storageProvider.SessionExistsAsync(newName, cancellationToken);
                if (existsResult.IsFailure)
                {
                    return Result<LockSession>.Failure(
                        $"Failed to check session existence: {existsResult.Error.Message}");
                }
                if (existsResult.Value)
                {
                    return Result<LockSession>.Failure(
                        Error.Create("NAME_TAKEN", $"A session named '{newName}' already exists."));
                }
            }

            var renamed = loadResult.Value with
            {
                Name = newName,
                Description = description ?? loadResult.Value.Description,
                LastAccessedAt = DateTime.UtcNow,
            };

            var saveResult = await _storageProvider.SaveSessionAsync(renamed, cancellationToken);
            if (saveResult.IsFailure)
            {
                return Result<LockSession>.Failure($"Failed to save renamed session: {saveResult.Error.Message}");
            }

            if (isRename)
            {
                var deleteResult = await _storageProvider.DeleteSessionAsync(currentName, cancellationToken);
                if (deleteResult.IsFailure)
                {
                    // The new key is saved; a lingering old key is a cleanup nuisance, not a
                    // data-loss failure — surface it in logs rather than failing the rename.
                    _logger.LogWarning(
                        "Renamed session saved as {NewName} but old key {OldName} could not be removed: {Error}",
                        newName, currentName, deleteResult.Error.Message);
                }
            }

            _logger.LogInformation("Renamed lock session {OldName} to {NewName}", currentName, newName);
            return Result<LockSession>.Success(renamed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename lock session: {SessionName}", currentName);
            return Result<LockSession>.Failure($"Failed to rename session: {ex.Message}");
        }
    }

    public async Task<Result> RemoveSessionAsync(
        string sessionName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var deleteResult = await _storageProvider.DeleteSessionAsync(sessionName, cancellationToken);
            if (deleteResult.IsFailure)
            {
                return deleteResult;
            }

            _logger.LogInformation("Removed lock session: {SessionName}", sessionName);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove lock session: {SessionName}", sessionName);
            return Result.Failure($"Failed to remove session: {ex.Message}");
        }
    }

    public async Task<Result> SetValueAsync(
        string sessionName,
        string fieldPath,
        string value,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updateResult = await UpdateSessionAsync(sessionName, session =>
            {
                var existingValues = session.LockedValues.ToList();
                var existingValueIndex = existingValues.FindIndex(v => v.FieldPath == fieldPath);

                var lockValue = new LockValue
                {
                    FieldPath = fieldPath,
                    Value = value,
                    LockedAt = DateTime.UtcNow,
                    Reason = reason,
                    IsExplicit = true
                };

                var auditEntry = new LockAuditEntry
                {
                    Action = existingValueIndex >= 0 ? "value_modified" : "value_set",
                    FieldPath = fieldPath,
                    OldValue = existingValueIndex >= 0 ? existingValues[existingValueIndex].Value : null,
                    NewValue = value,
                    Context = reason
                };

                if (existingValueIndex >= 0)
                {
                    existingValues[existingValueIndex] = lockValue;
                }
                else
                {
                    existingValues.Add(lockValue);
                }

                var auditTrail = session.Metadata.AuditTrail.ToList();
                auditTrail.Add(auditEntry);

                return session with
                {
                    LockedValues = existingValues.AsReadOnly(),
                    LastAccessedAt = DateTime.UtcNow,
                    Metadata = session.Metadata with
                    {
                        ModificationCount = session.Metadata.ModificationCount + 1,
                        AuditTrail = auditTrail.AsReadOnly()
                    }
                };
            }, cancellationToken);

            return updateResult.IsSuccess ? Result.Success() : Result.Failure(updateResult.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set value in lock session: {SessionName}, {FieldPath}", sessionName, fieldPath);
            return Result.Failure($"Failed to set value: {ex.Message}");
        }
    }

    public async Task<Result> RemoveValueAsync(
        string sessionName,
        string fieldPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var updateResult = await UpdateSessionAsync(sessionName, session =>
            {
                var existingValues = session.LockedValues.ToList();
                var removedValue = existingValues.Find(v => v.FieldPath == fieldPath);

                if (removedValue == null)
                {
                    return session; // No change if value doesn't exist
                }

                existingValues.RemoveAll(v => v.FieldPath == fieldPath);

                var auditEntry = new LockAuditEntry
                {
                    Action = "value_removed",
                    FieldPath = fieldPath,
                    OldValue = removedValue.Value,
                    Context = "Value removed by user"
                };

                var auditTrail = session.Metadata.AuditTrail.ToList();
                auditTrail.Add(auditEntry);

                return session with
                {
                    LockedValues = existingValues.AsReadOnly(),
                    LastAccessedAt = DateTime.UtcNow,
                    Metadata = session.Metadata with
                    {
                        ModificationCount = session.Metadata.ModificationCount + 1,
                        AuditTrail = auditTrail.AsReadOnly()
                    }
                };
            }, cancellationToken);

            return updateResult.IsSuccess ? Result.Success() : Result.Failure(updateResult.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove value from lock session: {SessionName}, {FieldPath}", sessionName, fieldPath);
            return Result.Failure($"Failed to remove value: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> GetLockedValuesAsync(
        string sessionName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionResult = await GetSessionAsync(sessionName, cancellationToken);
            if (sessionResult.IsFailure)
            {
                return Result<IReadOnlyDictionary<string, string>>.Failure(sessionResult.Error.Message);
            }

            var lockedValues = sessionResult.Value.LockedValues
                .ToDictionary(v => v.FieldPath, v => v.Value);

            return Result<IReadOnlyDictionary<string, string>>.Success(lockedValues);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get locked values for session: {SessionName}", sessionName);
            return Result<IReadOnlyDictionary<string, string>>.Failure($"Failed to get locked values: {ex.Message}");
        }
    }

    public async Task<Result<int>> CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cleanupResult = await _storageProvider.CleanupAsync(cancellationToken);
            if (cleanupResult.IsFailure)
            {
                return cleanupResult;
            }

            _logger.LogInformation("Cleaned up {Count} expired lock sessions", cleanupResult.Value);
            return cleanupResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired sessions");
            return Result<int>.Failure($"Failed to cleanup expired sessions: {ex.Message}");
        }
    }
}