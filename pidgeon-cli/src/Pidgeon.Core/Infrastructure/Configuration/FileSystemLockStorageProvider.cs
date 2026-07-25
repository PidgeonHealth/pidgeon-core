// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Domain.Configuration.Entities;
using Pidgeon.Core;
using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Configuration;

/// <summary>
/// File system-based storage provider for lock sessions.
/// Stores sessions as JSON files in a designated directory structure.
/// </summary>
public class FileSystemLockStorageProvider : ILockStorageProvider
{
    private readonly ILogger<FileSystemLockStorageProvider> _logger;
    private readonly string _storageDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public FileSystemLockStorageProvider(
        ILogger<FileSystemLockStorageProvider> logger,
        string? storageDirectory = null)
    {
        _logger = logger;
        _storageDirectory = storageDirectory ?? GetDefaultStorageDirectory();
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public Task<Result> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(_storageDirectory))
            {
                Directory.CreateDirectory(_storageDirectory);
                _logger.LogInformation("Created lock storage directory: {Directory}", _storageDirectory);
            }

            var lockSessionsDir = Path.Combine(_storageDirectory, "sessions");
            if (!Directory.Exists(lockSessionsDir))
            {
                Directory.CreateDirectory(lockSessionsDir);
                _logger.LogInformation("Created lock sessions directory: {Directory}", lockSessionsDir);
            }

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize lock storage provider");
            return Task.FromResult(Result.Failure($"Failed to initialize storage: {ex.Message}"));
        }
    }

    public async Task<Result> SaveSessionAsync(LockSession session, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure directories exist before saving
            var initResult = await InitializeAsync(cancellationToken);
            if (initResult.IsFailure)
            {
                return initResult;
            }

            var sessionPath = GetSessionFilePath(session.Name);
            var sessionJson = JsonSerializer.Serialize(session, _jsonOptions);

            await File.WriteAllTextAsync(sessionPath, sessionJson, cancellationToken);

            _logger.LogDebug("Saved lock session: {SessionName} to {Path}", session.Name, sessionPath);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save lock session: {SessionName}", session.Name);
            return Result.Failure($"Failed to save session: {ex.Message}");
        }
    }

    public async Task<Result<LockSession>> LoadSessionAsync(string sessionName, CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionPath = GetSessionFilePath(sessionName);

            if (!File.Exists(sessionPath))
            {
                return Result<LockSession>.Failure($"Lock session '{sessionName}' not found");
            }

            var sessionJson = await File.ReadAllTextAsync(sessionPath, cancellationToken);
            var session = JsonSerializer.Deserialize<LockSession>(sessionJson, _jsonOptions);

            if (session == null)
            {
                return Result<LockSession>.Failure($"Failed to deserialize session '{sessionName}'");
            }

            _logger.LogDebug("Loaded lock session: {SessionName}", sessionName);
            return Result<LockSession>.Success(session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load lock session: {SessionName}", sessionName);
            return Result<LockSession>.Failure($"Failed to load session: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<string>>> ListSessionNamesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Yield();

            var sessionsDir = Path.Combine(_storageDirectory, "sessions");

            if (!Directory.Exists(sessionsDir))
            {
                return Result<IReadOnlyList<string>>.Success([]);
            }

            var sessionFiles = Directory.GetFiles(sessionsDir, "*.json");
            var sessionNames = sessionFiles
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .ToList();

            return Result<IReadOnlyList<string>>.Success(sessionNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list lock sessions");
            return Result<IReadOnlyList<string>>.Failure($"Failed to list sessions: {ex.Message}");
        }
    }

    public async Task<Result> DeleteSessionAsync(string sessionName, CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Yield();

            var sessionPath = GetSessionFilePath(sessionName);

            if (!File.Exists(sessionPath))
            {
                return Result.Failure($"Lock session '{sessionName}' not found");
            }

            File.Delete(sessionPath);

            _logger.LogInformation("Deleted lock session: {SessionName}", sessionName);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete lock session: {SessionName}", sessionName);
            return Result.Failure($"Failed to delete session: {ex.Message}");
        }
    }

    public async Task<Result<bool>> SessionExistsAsync(string sessionName, CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Yield();

            var sessionPath = GetSessionFilePath(sessionName);
            var exists = File.Exists(sessionPath);

            return Result<bool>.Success(exists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if session exists: {SessionName}", sessionName);
            return Result<bool>.Failure($"Failed to check session existence: {ex.Message}");
        }
    }

    public async Task<Result<int>> CleanupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cleanupCount = 0;
            var sessionsDir = Path.Combine(_storageDirectory, "sessions");

            if (!Directory.Exists(sessionsDir))
            {
                return Result<int>.Success(0);
            }

            var sessionFiles = Directory.GetFiles(sessionsDir, "*.json");

            foreach (var sessionFile in sessionFiles)
            {
                try
                {
                    var sessionJson = await File.ReadAllTextAsync(sessionFile, cancellationToken);
                    var session = JsonSerializer.Deserialize<LockSession>(sessionJson, _jsonOptions);

                    if (session?.ExpiresAt.HasValue == true &&
                        session.ExpiresAt.Value < DateTime.UtcNow)
                    {
                        File.Delete(sessionFile);
                        cleanupCount++;
                        _logger.LogInformation("Cleaned up expired session: {SessionName}", session.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process session file during cleanup: {File}", sessionFile);
                }
            }

            _logger.LogInformation("Cleanup completed: {Count} expired sessions removed", cleanupCount);
            return Result<int>.Success(cleanupCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform cleanup");
            return Result<int>.Failure($"Cleanup failed: {ex.Message}");
        }
    }

    private string GetSessionFilePath(string sessionName)
    {
        var sanitizedName = SanitizeFileName(sessionName);
        return Path.Combine(_storageDirectory, "sessions", $"{sanitizedName}.json");
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string GetDefaultStorageDirectory()
    {
        var pidgeonHome = Environment.GetEnvironmentVariable("PIDGEON_HOME");
        if (!string.IsNullOrWhiteSpace(pidgeonHome))
        {
            return Path.Combine(Path.GetFullPath(pidgeonHome), "locks");
        }

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "Pidgeon", "locks");
    }
}
