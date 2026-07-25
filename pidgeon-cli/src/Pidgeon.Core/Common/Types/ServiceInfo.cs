// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Generation.Types;

/// <summary>
/// Information about generation service capabilities and current tier.
/// Provides transparency about available features and limitations.
/// </summary>
public record GenerationServiceInfo
{
    /// <summary>
    /// Gets the current service tier (Core, Professional, Team, Enterprise).
    /// </summary>
    public required string ServiceTier { get; init; }

    /// <summary>
    /// Gets whether AI enhancement is available in this tier.
    /// </summary>
    public bool AIAvailable { get; init; }

    /// <summary>
    /// Gets the available patient types for this service tier.
    /// </summary>
    public IReadOnlyList<PatientType> AvailablePatientTypes { get; init; } = Array.Empty<PatientType>();

    /// <summary>
    /// Gets the available vendor profiles for this service tier.
    /// </summary>
    public IReadOnlyList<VendorProfile> AvailableVendorProfiles { get; init; } = Array.Empty<VendorProfile>();

    /// <summary>
    /// Gets the dataset information including size and freshness.
    /// </summary>
    public required DatasetInfo Dataset { get; init; }

    /// <summary>
    /// Gets usage limits for this service tier.
    /// </summary>
    public UsageLimits? Limits { get; init; }

    /// <summary>
    /// Gets information about available AI providers and modes.
    /// </summary>
    public AICapabilities? AI { get; init; }
}

/// <summary>
/// Information about available datasets for generation.
/// Varies by service tier (free vs subscription).
/// </summary>
public record DatasetInfo
{
    /// <summary>
    /// Gets the number of available medications in the dataset.
    /// Free: 25, Professional: 500+, Enterprise: 1000+
    /// </summary>
    public int MedicationCount { get; init; }

    /// <summary>
    /// Gets the number of available first names in the dataset.
    /// Free: 50, Professional: 500+, Enterprise: 1000+
    /// </summary>
    public int FirstNameCount { get; init; }

    /// <summary>
    /// Gets the number of available surnames in the dataset.
    /// Free: 50, Professional: 500+, Enterprise: 1000+
    /// </summary>
    public int SurnameCount { get; init; }

    /// <summary>
    /// Gets the dataset freshness (e.g., "Real-time", "6 months old", "Static").
    /// Free/One-time: Static, Subscriptions: Real-time
    /// </summary>
    public required string Freshness { get; init; }

    /// <summary>
    /// Gets the coverage percentage for common healthcare scenarios.
    /// Free: 70%, Professional: 90%+, Enterprise: 95%+
    /// </summary>
    public int CoveragePercentage { get; init; }

    /// <summary>
    /// Gets additional dataset categories available (specialty drugs, regional patterns).
    /// </summary>
    public IReadOnlyList<string> SpecialtyCategories { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Usage limits for different service tiers.
/// Helps users understand current tier capabilities and upgrade benefits.
/// </summary>
public record UsageLimits
{
    /// <summary>
    /// Gets the maximum number of generations per time period.
    /// Free: 10/session, Professional: 10,000/month, Enterprise: Unlimited
    /// </summary>
    public int? MaxGenerationsPerPeriod { get; init; }

    /// <summary>
    /// Gets the period for the generation limit (Session, Day, Month).
    /// </summary>
    public string? LimitPeriod { get; init; }

    /// <summary>
    /// Gets the maximum number of AI-enhanced generations (if applicable).
    /// Professional: 1,000/month (BYOK), Enterprise: Unlimited
    /// </summary>
    public int? MaxAIGenerations { get; init; }

    /// <summary>
    /// Gets whether batch processing is available.
    /// Free: No, Professional+: Yes
    /// </summary>
    public bool BatchProcessingAvailable { get; init; }

    /// <summary>
    /// Gets whether cloud API access is available.
    /// Free: No, Professional+: Yes
    /// </summary>
    public bool CloudAPIAvailable { get; init; }

    /// <summary>
    /// Gets the maximum team size for collaborative features.
    /// Individual: 1, Team: 5, Enterprise: Unlimited
    /// </summary>
    public int? MaxTeamSize { get; init; }
}

/// <summary>
/// Information about AI capabilities available in the current tier.
/// </summary>
public record AICapabilities
{
    /// <summary>
    /// Gets the available AI providers for this tier.
    /// </summary>
    public IReadOnlyList<AIProvider> AvailableProviders { get; init; } = Array.Empty<AIProvider>();

    /// <summary>
    /// Gets the available generation modes for this tier.
    /// </summary>
    public IReadOnlyList<AIGenerationMode> AvailableModes { get; init; } = Array.Empty<AIGenerationMode>();

    /// <summary>
    /// Gets whether BYOK (Bring Your Own Key) is required.
    /// Professional: Yes, Enterprise: No
    /// </summary>
    public bool RequiresByok { get; init; }

    /// <summary>
    /// Gets the monthly token/usage limits for AI generation.
    /// </summary>
    public int? MonthlyTokenLimit { get; init; }

    /// <summary>
    /// Gets estimated cost per 1000 tokens (for BYOK tiers).
    /// </summary>
    public decimal? EstimatedCostPer1000Tokens { get; init; }
}