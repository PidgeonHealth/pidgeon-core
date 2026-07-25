// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Interfaces.Generation;

/// <summary>
/// Plugin interface for standard-specific message generation.
/// Each healthcare standard plugin implements this to handle its own message type universe.
/// </summary>
public interface IMessageGenerationPlugin
{
    /// <summary>
    /// The healthcare standard this plugin handles (e.g., "hl7", "fhir", "ncpdp").
    /// </summary>
    string StandardName { get; }

    /// <summary>
    /// Checks if this plugin can handle the specified message type.
    /// </summary>
    /// <param name="messageType">The message type to check</param>
    /// <returns>True if this plugin can generate the message type</returns>
    bool CanHandleMessageType(string messageType);

    /// <summary>
    /// Generates synthetic messages for the specified type using standard-specific logic.
    /// </summary>
    /// <param name="messageType">The message type to generate</param>
    /// <param name="count">Number of messages to generate</param>
    /// <param name="options">Generation options</param>
    /// <returns>Result containing generated message strings or error</returns>
    Task<Result<IReadOnlyList<string>>> GenerateMessagesAsync(string messageType, int count, GenerationOptions? options = null);

    /// <summary>
    /// Gets all message types supported by this plugin.
    /// </summary>
    /// <returns>Collection of supported message types</returns>
    IReadOnlyList<string> GetSupportedMessageTypes();

    /// <summary>
    /// Provides standard-specific intelligence for unsupported message types.
    /// </summary>
    /// <param name="messageType">The unsupported message type</param>
    /// <returns>Helpful error message with suggestions</returns>
    string GetUnsupportedMessageTypeError(string messageType);
}