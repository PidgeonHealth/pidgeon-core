// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core;
using Pidgeon.Core.Application.Common;

namespace Pidgeon.Core.Application.Interfaces.Standards;

/// <summary>
/// Represents a message in a healthcare standard.
/// This is the base interface that all standard-specific messages implement.
/// </summary>
public interface IStandardMessage
{
    /// <summary>
    /// Gets the message type (e.g., "ADT^A01", "Patient", "NewRx").
    /// </summary>
    string MessageType { get; }

    /// <summary>
    /// Gets or sets the standard this message belongs to.
    /// </summary>
    string Standard { get; set; }

    /// <summary>
    /// Gets the version of the standard.
    /// </summary>
    Version StandardVersion { get; }

    /// <summary>
    /// Gets or sets the message control ID for tracking purposes.
    /// </summary>
    string MessageControlId { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the message was created.
    /// </summary>
    DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets or sets the sending system/application name.
    /// </summary>
    string SendingSystem { get; set; }

    /// <summary>
    /// Gets or sets the receiving system/application name.
    /// </summary>
    string ReceivingSystem { get; set; }

    /// <summary>
    /// Gets or sets the version of the message format (separate from StandardVersion).
    /// </summary>
    string Version { get; set; }

    /// <summary>
    /// Serializes the message to its string representation.
    /// </summary>
    /// <param name="options">Serialization options</param>
    /// <returns>A result containing the serialized message or an error</returns>
    Result<string> Serialize(SerializationOptions? options = null);

    /// <summary>
    /// Validates the message structure and content.
    /// </summary>
    /// <param name="validationMode">The validation mode to use</param>
    /// <returns>A result containing validation results</returns>
    Result<ValidationResult> Validate(ValidationMode validationMode = ValidationMode.Strict);

    /// <summary>
    /// Gets metadata about the message.
    /// </summary>
    /// <returns>Message metadata</returns>
    MessageMetadata GetMetadata();
}