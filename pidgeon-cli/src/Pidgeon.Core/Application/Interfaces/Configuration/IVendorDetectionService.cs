// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Configuration.Entities;

namespace Pidgeon.Core.Application.Interfaces.Configuration;

/// <summary>
/// Service responsible for detecting vendor signatures from healthcare messages.
/// Analyzes vendor-specific identifiers and patterns to determine message origin.
/// Single responsibility: "Who sent this message?"
/// </summary>
public interface IVendorDetectionService
{
    /// <summary>
    /// Detects vendor signature from message headers and identifying fields.
    /// For HL7: Analyzes MSH.3 (Sending Application) and MSH.4 (Sending Facility).
    /// For FHIR: Analyzes sender information in message bundles.
    /// For NCPDP: Analyzes sender identification in message headers.
    /// </summary>
    /// <param name="messageHeaders">Extracted header information from the message</param>
    /// <returns>Result containing detected vendor signature with confidence score</returns>
    Task<Result<VendorSignature>> DetectFromHeadersAsync(MessageHeaders messageHeaders);

    /// <summary>
    /// Attempts to detect vendor signature from raw message content.
    /// Extracts headers internally and delegates to header-based detection.
    /// </summary>
    /// <param name="message">Raw message content</param>
    /// <param name="standard">Healthcare standard (HL7v23, FHIRv4, etc.)</param>
    /// <returns>Result containing detected vendor signature</returns>
    Task<Result<VendorSignature>> DetectFromMessageAsync(string message, string standard);

    /// <summary>
    /// Gets all available vendor detection patterns for a given standard.
    /// Useful for testing and validation purposes.
    /// </summary>
    /// <param name="standard">Healthcare standard name</param>
    /// <returns>Collection of available detection patterns</returns>
    Task<Result<IReadOnlyList<VendorDetectionPattern>>> GetPatternsForStandardAsync(string standard);
}

/// <summary>
/// Standard-agnostic representation of message headers used for vendor detection.
/// Abstracts header extraction logic from vendor detection logic.
/// </summary>
public record MessageHeaders
{
    public string SendingApplication { get; init; } = string.Empty;
    public string SendingFacility { get; init; } = string.Empty;
    public string ReceivingApplication { get; init; } = string.Empty;
    public string ReceivingFacility { get; init; } = string.Empty;
    public string MessageType { get; init; } = string.Empty;
    public string Standard { get; init; } = string.Empty;
    public Dictionary<string, string> AdditionalFields { get; init; } = new();
}