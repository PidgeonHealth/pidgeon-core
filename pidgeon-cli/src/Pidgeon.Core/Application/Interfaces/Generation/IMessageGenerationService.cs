// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Interfaces.Generation;

/// <summary>
/// Message generation service for converting clinical entities to standard-specific messages.
/// </summary>
public interface IMessageGenerationService
{
    /// <summary>
    /// Generates synthetic test data for a given standard and message type.
    /// </summary>
    /// <param name="standard">The target standard</param>
    /// <param name="messageType">The message type</param>
    /// <param name="count">Number of messages to generate</param>
    /// <param name="options">Generation options</param>
    /// <returns>A result containing the generated messages or an error</returns>
    Task<Result<IReadOnlyList<string>>> GenerateSyntheticDataAsync(string standard, string messageType, int count = 1, Pidgeon.Core.Generation.GenerationOptions? options = null);
}