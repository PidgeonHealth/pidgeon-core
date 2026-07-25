// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Common;

/// <summary>
/// Provides context about validation operations.
/// </summary>
public record ValidationContext
{
    /// <summary>
    /// Gets the validation mode that was used.
    /// </summary>
    public ValidationMode Mode { get; init; } = ValidationMode.Strict;

    /// <summary>
    /// Gets the list of validation rules that were applied.
    /// </summary>
    public IReadOnlyList<string> AppliedRules { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the configuration that was used for validation.
    /// </summary>
    public string? ConfigurationUsed { get; init; }

    /// <summary>
    /// Gets the timestamp when validation was performed.
    /// </summary>
    public DateTime ValidationTimestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Information about a validation rule.
/// </summary>
public record ValidationRuleInfo
{
    /// <summary>
    /// Gets the unique identifier for the rule.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the human-readable name of the rule.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the description of what the rule validates.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the category of the rule.
    /// </summary>
    public string Category { get; init; } = "General";

    /// <summary>
    /// Gets the severity level of violations of this rule.
    /// </summary>
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;

    /// <summary>
    /// Gets whether the rule is enabled by default.
    /// </summary>
    public bool EnabledByDefault { get; init; } = true;
}