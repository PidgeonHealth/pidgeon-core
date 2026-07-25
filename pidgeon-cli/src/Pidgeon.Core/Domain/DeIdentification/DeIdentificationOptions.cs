// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.DeIdentification;

/// <summary>
/// Configuration options for de-identification operations.
/// Controls how PHI is identified, replaced, and validated.
/// </summary>
public record DeIdentificationOptions
{
    /// <summary>
    /// Team-specific salt for deterministic identifier generation.
    /// Same salt ensures reproducible results across team members.
    /// </summary>
    public string? Salt { get; init; }

    /// <summary>
    /// Date offset to apply to all date fields.
    /// Positive values shift dates forward, negative values shift backward.
    /// </summary>
    public TimeSpan? DateShift { get; init; }

    /// <summary>
    /// Whether to preserve relationships between related messages.
    /// When true, same patient gets same synthetic ID across all messages.
    /// </summary>
    public bool PreserveRelationships { get; init; } = true;

    /// <summary>
    /// Whether to generate a detailed de-identification report.
    /// Includes audit trail, compliance verification, and statistics.
    /// </summary>
    public bool GenerateReport { get; init; } = false;

    /// <summary>
    /// De-identification method to use.
    /// Determines the level of privacy protection and data utility preservation.
    /// </summary>
    public DeIdentificationMethod Method { get; init; } = DeIdentificationMethod.SafeHarborPlus;

    /// <summary>
    /// Whether to preview changes without actually modifying data.
    /// Useful for validation and user confirmation before processing.
    /// </summary>
    public bool PreviewMode { get; init; } = false;

    /// <summary>
    /// Whether to preserve time components when shifting dates.
    /// When false, only date is preserved (time set to 00:00:00).
    /// </summary>
    public bool PreserveDateTimes { get; init; } = true;

    /// <summary>
    /// Custom field mappings for organization-specific PHI fields.
    /// Maps HL7 field paths to identifier types for proper handling.
    /// </summary>
    public Dictionary<string, IdentifierType> CustomFieldMappings { get; init; } = new();

    /// <summary>
    /// Additional free text fields to scan for PHI.
    /// Beyond standard HL7 fields, these will be analyzed for identifiers.
    /// </summary>
    public HashSet<string> AdditionalFreeTextFields { get; init; } = new();

    /// <summary>
    /// File path to save/load ID mappings for consistency across batches.
    /// Enables processing related messages at different times with same mappings.
    /// </summary>
    public string? MappingFilePath { get; init; }

    /// <summary>
    /// Maximum re-identification risk threshold for Expert Determination method.
    /// Must be "very small" risk as defined by HIPAA (typically <0.04%).
    /// </summary>
    public double MaxReIdentificationRisk { get; init; } = 0.04;

    /// <summary>
    /// Minimum k-anonymity value for statistical de-identification.
    /// Each record must be indistinguishable from at least k-1 others.
    /// </summary>
    public int MinimumKAnonymity { get; init; } = 5;

    /// <summary>
    /// Minimum l-diversity value for sensitive attributes.
    /// Each equivalence class must have at least l different sensitive values.
    /// </summary>
    public int MinimumLDiversity { get; init; } = 3;

    /// <summary>
    /// Creates default options with secure defaults.
    /// Generates random salt and reasonable date shift if not specified.
    /// </summary>
    public static DeIdentificationOptions CreateDefault()
    {
        return new DeIdentificationOptions
        {
            Salt = GenerateRandomSalt(),
            DateShift = TimeSpan.FromDays(Random.Shared.Next(-365, 365)),
            PreserveRelationships = true,
            GenerateReport = false,
            Method = DeIdentificationMethod.SafeHarborPlus,
            PreviewMode = false,
            PreserveDateTimes = true
        };
    }

    /// <summary>
    /// Creates options optimized for development and testing.
    /// Uses fixed salt for reproducible results and shorter date shifts.
    /// </summary>
    public static DeIdentificationOptions CreateForTesting()
    {
        return new DeIdentificationOptions
        {
            Salt = "TEST_SALT_2025",
            DateShift = TimeSpan.FromDays(30),
            PreserveRelationships = true,
            GenerateReport = true,
            Method = DeIdentificationMethod.SafeHarborPlus,
            PreviewMode = false,
            PreserveDateTimes = true
        };
    }

    /// <summary>
    /// Validates that the options are consistent and complete.
    /// </summary>
    public IEnumerable<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Salt))
            errors.Add("Salt is required for deterministic de-identification");

        if (!DateShift.HasValue)
            errors.Add("DateShift is required for temporal de-identification");

        if (DateShift.HasValue && Math.Abs(DateShift.Value.TotalDays) > 365 * 10)
            errors.Add("DateShift should not exceed 10 years to maintain data utility");

        if (MaxReIdentificationRisk <= 0 || MaxReIdentificationRisk > 1.0)
            errors.Add("MaxReIdentificationRisk must be between 0 and 1.0");

        if (MinimumKAnonymity < 2)
            errors.Add("MinimumKAnonymity must be at least 2");

        if (MinimumLDiversity < 2)
            errors.Add("MinimumLDiversity must be at least 2");

        if (Method == DeIdentificationMethod.ExpertDetermination && MaxReIdentificationRisk > 0.04)
            errors.Add("Expert Determination method requires re-identification risk ≤ 0.04 (4%)");

        return errors;
    }

    /// <summary>
    /// Generates a cryptographically secure random salt.
    /// </summary>
    private static string GenerateRandomSalt()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}

/// <summary>
/// De-identification methods providing different levels of privacy protection.
/// </summary>
public enum DeIdentificationMethod
{
    /// <summary>
    /// HIPAA Safe Harbor method: Remove all 18 specified identifiers.
    /// Provides strong privacy protection with good data utility.
    /// </summary>
    SafeHarbor,

    /// <summary>
    /// Safe Harbor plus synthetic replacement for removed identifiers.
    /// Pidgeon's enhanced approach providing zero re-identification risk
    /// while maintaining realistic test scenarios.
    /// </summary>
    SafeHarborPlus,

    /// <summary>
    /// Statistical methods (k-anonymity, l-diversity) with risk assessment.
    /// Requires expert analysis to ensure "very small" re-identification risk.
    /// </summary>
    ExpertDetermination,

    /// <summary>
    /// Full synthetic data generation based on original patterns.
    /// Highest privacy protection with potential reduction in data utility.
    /// </summary>
    FullSynthetic
}