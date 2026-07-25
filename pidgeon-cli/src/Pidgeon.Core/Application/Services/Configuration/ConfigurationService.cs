// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core;
using System.Text.Json;

namespace Pidgeon.Core.Application.Services.Configuration;

/// <summary>
/// Service for managing user and application configuration settings.
/// Handles per-standard defaults and preferences with file-based persistence.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private readonly string _configurationPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ConfigurationService(ILogger<ConfigurationService> logger, string? configurationPath = null)
    {
        _logger = logger;
        _configurationPath = configurationPath ?? GetDefaultConfigurationPath();
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<Result<ApplicationConfiguration>> GetCurrentConfigurationAsync()
    {
        try
        {
            if (!File.Exists(_configurationPath))
            {
                // Return default configuration if file doesn't exist
                var defaultConfig = GetDefaultConfiguration();
                await EnsureConfigurationDirectoryExists();
                return Result<ApplicationConfiguration>.Success(defaultConfig);
            }

            var configJson = await File.ReadAllTextAsync(_configurationPath);
            var configuration = JsonSerializer.Deserialize<ApplicationConfiguration>(configJson, _jsonOptions);

            if (configuration == null)
            {
                _logger.LogWarning("Configuration file exists but could not be deserialized, using defaults");
                return Result<ApplicationConfiguration>.Success(GetDefaultConfiguration());
            }

            return Result<ApplicationConfiguration>.Success(configuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration from {Path}", _configurationPath);
            return Result<ApplicationConfiguration>.Failure($"Failed to load configuration: {ex.Message}");
        }
    }

    public async Task<Result> UpdateConfigurationAsync(ApplicationConfiguration configuration)
    {
        try
        {
            await EnsureConfigurationDirectoryExists();

            var updatedConfiguration = configuration with
            {
                LastUpdated = DateTime.UtcNow
            };

            var configJson = JsonSerializer.Serialize(updatedConfiguration, _jsonOptions);
            await File.WriteAllTextAsync(_configurationPath, configJson);

            _logger.LogInformation("Configuration updated successfully");
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration to {Path}", _configurationPath);
            return Result.Failure($"Failed to save configuration: {ex.Message}");
        }
    }

    public async Task<Result<string>> GetDefaultStandardVersionAsync(string standardFamily)
    {
        var configResult = await GetCurrentConfigurationAsync();
        if (configResult.IsFailure)
        {
            return Result<string>.Failure(configResult.Error.Message);
        }

        var config = configResult.Value;
        if (config.StandardDefaults.TryGetValue(standardFamily, out var version))
        {
            return Result<string>.Success(version);
        }

        // Return system default if not configured
        var systemDefault = GetSystemDefaultForStandard(standardFamily);
        return Result<string>.Success(systemDefault);
    }

    public async Task<Result> SetDefaultStandardVersionAsync(string standardFamily, string version)
    {
        var configResult = await GetCurrentConfigurationAsync();
        if (configResult.IsFailure)
        {
            return Result.Failure(configResult.Error);
        }

        var currentConfig = configResult.Value;
        var updatedDefaults = new Dictionary<string, string>(currentConfig.StandardDefaults)
        {
            [standardFamily] = version
        };

        var updatedConfig = currentConfig with
        {
            StandardDefaults = updatedDefaults
        };

        return await UpdateConfigurationAsync(updatedConfig);
    }

    public async Task<Result<string>> GetEffectiveStandardAsync(string messageType, string? explicitStandard = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitStandard))
        {
            return Result<string>.Success(explicitStandard);
        }

        // Infer standard family from message type
        var standardFamily = InferStandardFamilyFromMessageType(messageType);
        if (standardFamily == null)
        {
            return Result<string>.Failure($"Could not determine standard family for message type: {messageType}");
        }

        // Get the configured default version for this standard family
        var versionResult = await GetDefaultStandardVersionAsync(standardFamily);
        if (versionResult.IsFailure)
        {
            return versionResult;
        }

        var effectiveStandard = $"{standardFamily}{versionResult.Value}";
        _logger.LogDebug("Resolved effective standard for {MessageType}: {Standard}",
            messageType, effectiveStandard);

        return Result<string>.Success(effectiveStandard);
    }

    // Intentionally distinct from Domain.Messaging.MessageTypeVocabulary.InferStandard: this
    // resolves a configuration standard FAMILY (always returns one — defaults unknown to "HL7",
    // recognizes X12, uses a broad FHIR list), whereas MessageTypeVocabulary is the agent/judge/
    // generation inference that returns null for unknown types. They are not merged on purpose;
    // collapsing them would regress this method's default-and-X12 semantics.
    private static string InferStandardFamilyFromMessageType(string messageType)
    {
        if (string.IsNullOrWhiteSpace(messageType))
        {
            return "HL7"; // Default fallback
        }

        // HL7 patterns: ADT^A01, ORU^R01, RDE^O11, etc.
        if (messageType.Contains('^'))
        {
            return "HL7";
        }

        // FHIR patterns: Patient, Observation, MedicationRequest, etc.
        if (IsFHIRResourceType(messageType))
        {
            return "FHIR";
        }

        // NCPDP patterns: NewRx, Refill, Cancel, etc.
        if (IsNCPDPMessageType(messageType))
        {
            return "NCPDP";
        }

        // X12 patterns: 270, 271, 835, 837, etc.
        if (messageType.All(char.IsDigit) && messageType.Length == 3)
        {
            return "X12";
        }

        // Default to HL7 for unknown patterns
        return "HL7";
    }

    private static bool IsFHIRResourceType(string messageType)
    {
        // Check if message type follows FHIR resource naming patterns
        // FHIR resources start with uppercase letter and use PascalCase
        if (string.IsNullOrWhiteSpace(messageType))
            return false;

        // FHIR resource pattern: PascalCase without special characters
        if (!char.IsUpper(messageType[0]) || messageType.Contains("^") || messageType.All(char.IsDigit))
            return false;

        // Common FHIR resource prefixes/patterns
        var fhirPatterns = new[]
        {
            "Patient", "Practitioner", "Organization", "Location", "Person",
            "Observation", "DiagnosticReport", "Condition", "Procedure", "Specimen",
            "MedicationRequest", "MedicationDispense", "MedicationStatement", "MedicationAdministration", "Medication",
            "Encounter", "EpisodeOfCare", "Appointment", "Schedule", "Slot",
            "Bundle", "Composition", "DocumentReference", "DocumentManifest",
            "Device", "DeviceRequest", "DeviceUseStatement",
            "Coverage", "ExplanationOfBenefit", "Claim", "Account",
            "CarePlan", "CareTeam", "Goal", "ServiceRequest", "Task",
            "Communication", "CommunicationRequest", "Consent", "Contract",
            "AllergyIntolerance", "FamilyMemberHistory", "ImagingStudy"
        };

        // Check if messageType starts with any known FHIR resource pattern
        return fhirPatterns.Any(pattern =>
            messageType.StartsWith(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNCPDPMessageType(string messageType)
    {
        var ncpdpTypes = new[]
        {
            "NewRx", "Refill", "Cancel", "RxFill", "RxHistoryRequest",
            "Error", "Status", "Verify", "RxChangeRequest"
        };

        return ncpdpTypes.Contains(messageType, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetSystemDefaultForStandard(string standardFamily)
    {
        return standardFamily.ToUpperInvariant() switch
        {
            "HL7" => "v23",
            "FHIR" => "v4",
            "NCPDP" => "v2017071",
            "X12" => "v5010",
            _ => "v1" // Generic fallback
        };
    }

    private static ApplicationConfiguration GetDefaultConfiguration()
    {
        return new ApplicationConfiguration
        {
            StandardDefaults = new Dictionary<string, string>
            {
                ["HL7"] = "v23",
                ["FHIR"] = "v4",
                ["NCPDP"] = "v2017071",
                ["X12"] = "v5010"
            },
            DefaultOutputFormat = "json",
            DefaultValidationMode = "Compatibility",
            EnableVerboseLogging = false,
            DefaultWorkingDirectory = Environment.CurrentDirectory,
            DefaultExportFormat = "yaml"
        };
    }

    private async Task EnsureConfigurationDirectoryExists()
    {
        var directory = Path.GetDirectoryName(_configurationPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogInformation("Created configuration directory: {Directory}", directory);
        }
        await Task.Yield(); // Make async
    }

    private static string GetDefaultConfigurationPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "Pidgeon", "config.json");
    }
}