// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core;

namespace Pidgeon.Core.Application.Interfaces.Configuration;

/// <summary>
/// Service for managing user and application configuration settings.
/// Provides access to per-standard defaults, preferences, and CLI settings.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Gets the current application configuration including user preferences.
    /// </summary>
    /// <returns>Result containing current configuration</returns>
    Task<Result<ApplicationConfiguration>> GetCurrentConfigurationAsync();

    /// <summary>
    /// Updates application configuration settings.
    /// </summary>
    /// <param name="configuration">Configuration to save</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> UpdateConfigurationAsync(ApplicationConfiguration configuration);

    /// <summary>
    /// Gets the default version for a specific healthcare standard family.
    /// </summary>
    /// <param name="standardFamily">Standard family (e.g., "HL7", "FHIR", "NCPDP", "X12")</param>
    /// <returns>Result containing the default version for that standard family</returns>
    Task<Result<string>> GetDefaultStandardVersionAsync(string standardFamily);

    /// <summary>
    /// Sets the default version for a specific healthcare standard family.
    /// </summary>
    /// <param name="standardFamily">Standard family (e.g., "HL7", "FHIR", "NCPDP", "X12")</param>
    /// <param name="version">Version to set as default (e.g., "v23", "v4", "v2017071")</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> SetDefaultStandardVersionAsync(string standardFamily, string version);

    /// <summary>
    /// Gets the effective standard for a message type, using configured defaults.
    /// </summary>
    /// <param name="messageType">Message type to infer standard for</param>
    /// <param name="explicitStandard">Explicit standard override</param>
    /// <returns>Result containing the effective standard (e.g., "HL7v23", "FHIRv4")</returns>
    Task<Result<string>> GetEffectiveStandardAsync(string messageType, string? explicitStandard = null);
}

/// <summary>
/// Application configuration containing user preferences and per-standard defaults.
/// </summary>
public record ApplicationConfiguration
{
    /// <summary>
    /// Default versions for each healthcare standard family.
    /// Key: Standard family (HL7, FHIR, NCPDP, X12)
    /// Value: Default version (v23, v4, v2017071, etc.)
    /// </summary>
    public IReadOnlyDictionary<string, string> StandardDefaults { get; init; } = new Dictionary<string, string>
    {
        ["HL7"] = "v23",
        ["FHIR"] = "v4",
        ["NCPDP"] = "v2017071",
        ["X12"] = "v5010"
    };

    /// <summary>
    /// Default output format for CLI commands.
    /// </summary>
    public string? DefaultOutputFormat { get; init; }

    /// <summary>
    /// Default validation mode (Strict, Compatibility).
    /// </summary>
    public string? DefaultValidationMode { get; init; }

    /// <summary>
    /// Enable detailed logging output.
    /// </summary>
    public bool EnableVerboseLogging { get; init; }

    /// <summary>
    /// Default working directory for file operations.
    /// </summary>
    public string? DefaultWorkingDirectory { get; init; }

    /// <summary>
    /// Default template author for session exports.
    /// </summary>
    public string? TemplateAuthor { get; init; }

    /// <summary>
    /// Default template organization/company.
    /// </summary>
    public string? TemplateOrganization { get; init; }

    /// <summary>
    /// Default template category for session exports.
    /// </summary>
    public string? DefaultTemplateCategory { get; init; }

    /// <summary>
    /// Default export format for session exports.
    /// </summary>
    public string? DefaultExportFormat { get; init; }

    /// <summary>
    /// Configuration version for schema evolution.
    /// </summary>
    public string ConfigVersion { get; init; } = "1.0";

    /// <summary>
    /// Last updated timestamp.
    /// </summary>
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}