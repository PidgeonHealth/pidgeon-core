// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Standards;

/// <summary>
/// Defines the contract for healthcare standard plugins.
/// Each standard (HL7, FHIR, NCPDP) implements this interface to provide
/// a consistent API for message generation, parsing, and validation.
/// </summary>
public interface IStandardPlugin
{
    /// <summary>
    /// Gets the name of the healthcare standard.
    /// </summary>
    string StandardName { get; }

    /// <summary>
    /// Gets the version of the healthcare standard supported.
    /// </summary>
    Version StandardVersion { get; }

    /// <summary>
    /// Gets a human-readable description of what this plugin supports.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the supported message types for this standard.
    /// </summary>
    IReadOnlyList<string> SupportedMessageTypes { get; }

    /// <summary>
    /// Gets the message factory for creating healthcare messages.
    /// </summary>
    IStandardMessageFactory MessageFactory { get; }

    /// <summary>
    /// Creates a message builder for the specified message type.
    /// </summary>
    /// <param name="messageType">The type of message to create (e.g., "ADT", "RDE", "Patient")</param>
    /// <returns>A result containing the message builder or an error</returns>
    Result<IStandardMessage> CreateMessage(string messageType);

    /// <summary>
    /// Parses a message string into a structured message object.
    /// </summary>
    /// <param name="messageContent">The raw message content</param>
    /// <returns>A result containing the parsed message or an error</returns>
    Result<IStandardMessage> ParseMessage(string messageContent);

    /// <summary>
    /// Determines if this plugin can handle the given message content.
    /// </summary>
    /// <param name="messageContent">The message content to examine</param>
    /// <returns>True if this plugin can handle the message, false otherwise</returns>
    bool CanHandle(string messageContent);
}