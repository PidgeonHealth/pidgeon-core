// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core;

namespace Pidgeon.Core.Application.Interfaces.Configuration;

/// <summary>
/// Resolves semantic field paths to actual message field locations across healthcare standards.
/// Delegates to standard-specific plugins for path resolution logic.
/// </summary>
public interface IFieldPathResolver
{
    /// <summary>
    /// Resolves a semantic path to standard-specific field location.
    /// Uses user configuration default standard if not explicitly specified.
    /// </summary>
    /// <param name="semanticPath">User-friendly path (e.g., "patient.mrn", "encounter.location")</param>
    /// <param name="messageType">Target message type (e.g., "ADT^A01", "Patient", "NewRx")</param>
    /// <param name="standard">Healthcare standard override, or null to use configuration default</param>
    /// <returns>Result containing the resolved field path or validation error</returns>
    Task<Result<string>> ResolvePathAsync(
        string semanticPath,
        string messageType,
        string? standard = null);

    /// <summary>
    /// Validates that a semantic path is supported for the given message type and standard.
    /// </summary>
    /// <param name="semanticPath">Path to validate</param>
    /// <param name="messageType">Target message type</param>
    /// <param name="standard">Healthcare standard override, or null to use configuration default</param>
    /// <returns>Result indicating if path is valid with details</returns>
    Task<Result<bool>> ValidatePathAsync(
        string semanticPath,
        string messageType,
        string? standard = null);

    /// <summary>
    /// Gets all available semantic paths for a message type and standard.
    /// </summary>
    /// <param name="messageType">Message type to get paths for</param>
    /// <param name="standard">Healthcare standard override, or null to use configuration default</param>
    /// <returns>Result containing available paths with descriptions</returns>
    Task<Result<IReadOnlyDictionary<string, string>>> GetAvailablePathsAsync(
        string messageType,
        string? standard = null);

    /// <summary>
    /// Validates that a value is appropriate for the given field path.
    /// </summary>
    /// <param name="semanticPath">Field path</param>
    /// <param name="value">Value to validate</param>
    /// <param name="messageType">Target message type</param>
    /// <param name="standard">Healthcare standard override, or null to use configuration default</param>
    /// <returns>Result with validation outcome and suggestions</returns>
    Task<Result<FieldValidationResult>> ValidateValueAsync(
        string semanticPath,
        string value,
        string messageType,
        string? standard = null);
}

/// <summary>
/// Standard-specific field path resolution plugin interface.
/// Each healthcare standard (HL7, FHIR, NCPDP) implements this interface.
/// </summary>
public interface IStandardFieldPathPlugin
{
    /// <summary>
    /// Healthcare standard this plugin handles (e.g., "HL7v23", "FHIRv4", "NCPDPv2017071").
    /// </summary>
    string Standard { get; }

    /// <summary>
    /// Resolves semantic path to standard-specific field location.
    /// </summary>
    Task<Result<string>> ResolvePathAsync(string semanticPath, string messageType);

    /// <summary>
    /// Validates semantic path support for message type.
    /// </summary>
    Task<Result<bool>> ValidatePathAsync(string semanticPath, string messageType);

    /// <summary>
    /// Gets available semantic paths for message type.
    /// </summary>
    Task<Result<IReadOnlyDictionary<string, string>>> GetAvailablePathsAsync(string messageType);

    /// <summary>
    /// Validates field value appropriateness.
    /// </summary>
    Task<Result<FieldValidationResult>> ValidateValueAsync(string semanticPath, string value, string messageType);
}

/// <summary>
/// Result of field value validation across healthcare standards.
/// </summary>
public record FieldValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> Suggestions { get; init; } = [];
    public string? FieldType { get; init; }
    public string? TableReference { get; init; }
    public IReadOnlyList<string> ValidValues { get; init; } = [];
    public string? StandardFieldPath { get; init; }
}