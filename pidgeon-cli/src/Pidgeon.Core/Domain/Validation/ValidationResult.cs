// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Pidgeon.Core.Domain.Validation;

/// <summary>
/// Result of validating a healthcare message against standards and profiles.
/// </summary>
public record ValidationResult
{
    public required bool IsValid { get; init; }
    public required string Standard { get; init; }
    public string? Profile { get; init; }
    public required ValidationMode Mode { get; init; }
    public required List<ValidationIssue> Issues { get; init; }
    public required ValidationStatistics Statistics { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();

    /// <summary>
    /// Advisory clinical-sense findings (the semantic channel). Additive beside
    /// <see cref="Issues"/>: never consulted by <see cref="IsValid"/> or the
    /// error/warning counts — a semantic finding cannot fail a message.
    /// </summary>
    public List<SemanticFinding> SemanticFindings { get; init; } = new();

    public int ErrorCount => Issues.Count(i => i.Severity == ValidationSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == ValidationSeverity.Warning);
    public int InfoCount => Issues.Count(i => i.Severity == ValidationSeverity.Info);
    public int AdvisoryCount => SemanticFindings.Count + Issues.Count(i => i.Severity == ValidationSeverity.Advisory);
}

/// <summary>
/// Individual validation issue found during message validation.
/// </summary>
public record ValidationIssue
{
    public required string Location { get; init; }  // e.g., "PID.5", "Patient.name"
    public required ValidationSeverity Severity { get; init; }
    public required string Message { get; init; }
    public required string RuleId { get; init; }
    public string? ExpectedValue { get; init; }
    public string? ActualValue { get; init; }
    public string? Suggestion { get; init; }

    /// <summary>
    /// 1-based ordinal of the message this issue belongs to when the validated
    /// input carried multiple messages in one file. Null for single-message
    /// input and for batch-scoped issues that span messages (e.g. duplicate
    /// control-id findings). Additive beside <see cref="Location"/>, whose
    /// format ("PID.5") is parsed by downstream consumers and must not change.
    /// </summary>
    public int? MessageIndex { get; init; }
}

/// <summary>
/// Statistics about the validation process.
/// </summary>
public record ValidationStatistics
{
    public required int TotalRulesChecked { get; init; }
    public required int RulesPassed { get; init; }
    public required int RulesFailed { get; init; }
    public required int FieldsValidated { get; init; }
    public required TimeSpan ValidationTime { get; init; }
    public double ConformanceScore => TotalRulesChecked > 0 
        ? (double)RulesPassed / TotalRulesChecked * 100 
        : 100.0;
}

public enum ValidationMode
{
    Strict,         // Exact standard compliance
    Compatibility   // Real-world vendor patterns
}