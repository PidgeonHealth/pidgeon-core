// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Common;

/// <summary>
/// Message metadata container.
/// </summary>
public record MessageMetadata
{
    /// <summary>
    /// Gets the message type.
    /// </summary>
    public string MessageType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the healthcare standard.
    /// </summary>
    public string Standard { get; init; } = string.Empty;

    /// <summary>
    /// Gets the standard version.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Gets the number of segments in the message.
    /// </summary>
    public int SegmentCount { get; init; }

    /// <summary>
    /// Gets the message control ID.
    /// </summary>
    public string ControlId { get; init; } = string.Empty;

    /// <summary>
    /// Gets when the message was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets who or what created the message.
    /// </summary>
    public string? CreatedBy { get; init; }

    /// <summary>
    /// Gets the message size in bytes.
    /// </summary>
    public int SizeInBytes { get; init; }

    /// <summary>
    /// Gets custom metadata properties.
    /// </summary>
    public Dictionary<string, object> Properties { get; init; } = new();
}