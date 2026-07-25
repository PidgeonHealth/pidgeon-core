// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Common;

/// <summary>
/// Contains the results of a validation operation.
/// </summary>
public record ValidationResult
{
    /// <summary>
    /// Gets whether the validation passed.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the list of validation errors.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; init; } = Array.Empty<ValidationError>();

    /// <summary>
    /// Gets the list of validation warnings.
    /// </summary>
    public IReadOnlyList<ValidationWarning> Warnings { get; init; } = Array.Empty<ValidationWarning>();

    /// <summary>
    /// Gets information about which validation rules were applied.
    /// </summary>
    public ValidationContext Context { get; init; } = new();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ValidationResult Success(ValidationContext? context = null) => new()
    {
        IsValid = true,
        Context = context ?? new()
    };

    /// <summary>
    /// Creates a failed validation result with errors.
    /// </summary>
    public static ValidationResult Failure(IEnumerable<ValidationError> errors, ValidationContext? context = null) => new()
    {
        IsValid = false,
        Errors = errors.ToList(),
        Context = context ?? new()
    };
}

/// <summary>
/// Represents a validation error.
/// </summary>
public record ValidationError
{
    /// <summary>
    /// Gets the error code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the severity level of the error.
    /// </summary>
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;

    /// <summary>
    /// Gets the location where the error occurred.
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Gets the field or element that caused the error.
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    /// Gets the expected value or format.
    /// </summary>
    public string? Expected { get; init; }

    /// <summary>
    /// Gets the actual value that caused the error.
    /// </summary>
    public string? Actual { get; init; }
}

/// <summary>
/// Represents a validation warning.
/// </summary>
public record ValidationWarning
{
    /// <summary>
    /// Gets the warning code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the warning message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the location where the warning occurred.
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Gets the field or element that triggered the warning.
    /// </summary>
    public string? Field { get; init; }

    /// <summary>
    /// Gets a suggestion for resolving the warning.
    /// </summary>
    public string? Suggestion { get; init; }
}