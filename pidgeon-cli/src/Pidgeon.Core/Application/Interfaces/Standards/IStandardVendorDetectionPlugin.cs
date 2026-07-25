// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Domain.Configuration.Entities;

namespace Pidgeon.Core.Application.Interfaces.Standards;

/// <summary>
/// Plugin interface for standard-specific vendor detection and header extraction.
/// Each healthcare standard (HL7, FHIR, NCPDP) implements this interface
/// to provide specialized logic for extracting vendor signatures and message headers.
/// Follows plugin architecture sacred principle: standard logic isolated in plugins.
/// </summary>
public interface IStandardVendorDetectionPlugin
{
    /// <summary>
    /// The healthcare standard name this plugin handles (HL7v23, FHIRv4, etc.).
    /// </summary>
    string StandardName { get; }

    /// <summary>
    /// Determines if this plugin can handle vendor detection for the given standard.
    /// </summary>
    /// <param name="standard">Healthcare standard name</param>
    /// <returns>True if this plugin can handle the standard</returns>
    bool CanHandle(string standard);

    /// <summary>
    /// Extracts message headers from raw message content specific to this standard.
    /// For HL7: MSH segment fields (Sending Application, Sending Facility, Message Type).
    /// For FHIR: Bundle metadata, sender information from MessageHeader resource.
    /// For NCPDP: Message header fields, sender identification.
    /// </summary>
    /// <param name="message">Raw message content</param>
    /// <returns>Result containing extracted message headers</returns>
    Task<Result<MessageHeaders>> ExtractMessageHeadersAsync(string message);

    /// <summary>
    /// Validates that a message appears to be in the correct format for this standard.
    /// Quick format check before attempting full header extraction.
    /// </summary>
    /// <param name="message">Raw message content</param>
    /// <returns>True if the message appears to be in this standard's format</returns>
    bool IsValidMessageFormat(string message);

    /// <summary>
    /// Detects vendor signature from message headers using standard-specific logic.
    /// Delegates to the shared vendor pattern repository but provides standard-specific header extraction.
    /// </summary>
    /// <param name="message">Raw message content</param>
    /// <param name="patternRepository">Shared vendor pattern repository</param>
    /// <returns>Result containing detected vendor signature</returns>
    Task<Result<VendorSignature>> DetectVendorSignatureAsync(
        string message,
        IVendorPatternRepository patternRepository);
}