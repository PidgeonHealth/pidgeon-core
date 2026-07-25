// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Configuration.Entities;

namespace Pidgeon.Core.Application.Interfaces.Configuration;

/// <summary>
/// Repository for loading and managing vendor detection patterns.
/// Loads patterns from JSON configuration files, not hardcoded classes.
/// Single responsibility: "Load and manage vendor detection configurations."
/// </summary>
public interface IVendorPatternRepository
{
    /// <summary>
    /// Loads all vendor detection patterns from configuration files.
    /// </summary>
    /// <returns>Result containing all available vendor patterns</returns>
    Task<Result<IReadOnlyList<VendorDetectionPattern>>> LoadAllPatternsAsync();

    /// <summary>
    /// Loads patterns for a specific healthcare standard.
    /// </summary>
    /// <param name="standard">Healthcare standard (HL7v23, FHIRv4, etc.)</param>
    /// <returns>Result containing patterns applicable to the standard</returns>
    Task<Result<IReadOnlyList<VendorDetectionPattern>>> LoadPatternsForStandardAsync(string standard);

    /// <summary>
    /// Gets a specific vendor pattern by ID.
    /// </summary>
    /// <param name="patternId">Unique pattern identifier</param>
    /// <returns>Result containing the vendor pattern if found</returns>
    Task<Result<VendorDetectionPattern?>> GetPatternAsync(string patternId);

    /// <summary>
    /// Adds or updates a vendor detection pattern.
    /// Enables runtime addition of new vendor patterns.
    /// </summary>
    /// <param name="pattern">Vendor detection pattern to store</param>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> SavePatternAsync(VendorDetectionPattern pattern);

    /// <summary>
    /// Reloads patterns from configuration files.
    /// Useful for runtime updates without restart.
    /// </summary>
    /// <returns>Result indicating success or failure</returns>
    Task<Result> RefreshPatternsAsync();

    /// <summary>
    /// Gets the configuration directory path.
    /// </summary>
    string ConfigurationPath { get; }
}