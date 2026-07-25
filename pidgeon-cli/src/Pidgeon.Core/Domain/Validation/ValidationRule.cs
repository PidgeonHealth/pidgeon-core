// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Pidgeon.Core.Domain.Validation;

/// <summary>
/// Represents a single validation rule that can be applied to healthcare messages.
/// </summary>
public record ValidationRule
{
    public required string RuleId { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ValidationSeverity Severity { get; init; }
    public required ValidationType Type { get; init; }
    public string? Category { get; init; }
    public Dictionary<string, object> Parameters { get; init; } = new();
}

public enum ValidationSeverity
{
    Error,      // Must fix - message is invalid
    Warning,    // Should fix - potential issues
    Info,       // Consider - best practice suggestions
    Advisory    // Clinical-sense observation - never affects validity (semantic channel ceiling)
}

public enum ValidationType
{
    Required,           // Field/segment must be present
    Format,            // Field must match pattern
    ValueSet,          // Field must be from allowed values
    Cardinality,       // Segment/field repeat limits
    Length,            // Field length constraints
    DataType,          // Field data type validation
    Conditional,       // If X then Y rules
    CrossReference,    // Inter-field dependencies
    Custom            // Plugin-specific rules
}