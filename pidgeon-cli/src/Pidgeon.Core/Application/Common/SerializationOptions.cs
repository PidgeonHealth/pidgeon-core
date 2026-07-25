// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Common;

/// <summary>
/// Contains options for message serialization.
/// </summary>
public record SerializationOptions
{
    /// <summary>
    /// Gets whether to include optional fields.
    /// </summary>
    public bool IncludeOptionalFields { get; init; } = true;

    /// <summary>
    /// Gets whether to format the output for readability.
    /// </summary>
    public bool FormatForReadability { get; init; } = false;

    /// <summary>
    /// Gets the encoding to use for the output.
    /// </summary>
    public string Encoding { get; init; } = "UTF-8";

    /// <summary>
    /// Gets custom serialization options specific to the standard.
    /// </summary>
    public Dictionary<string, object> CustomOptions { get; init; } = new();

    /// <summary>
    /// Creates default serialization options.
    /// </summary>
    public static SerializationOptions Default => new();

    /// <summary>
    /// Creates options optimized for compatibility with specific systems.
    /// </summary>
    /// <param name="system">The target system (e.g., "Epic", "Cerner")</param>
    public static SerializationOptions ForSystem(string system) => new()
    {
        CustomOptions = new Dictionary<string, object> { ["TargetSystem"] = system }
    };
}