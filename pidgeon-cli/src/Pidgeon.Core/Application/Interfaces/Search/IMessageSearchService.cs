// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Search;

namespace Pidgeon.Core.Application.Interfaces.Search;

/// <summary>
/// Service for searching values within healthcare message files.
/// Scans and parses HL7, FHIR, and other message formats to locate specific values.
/// </summary>
public interface IMessageSearchService
{
    /// <summary>
    /// Finds a specific value within healthcare message files.
    /// </summary>
    Task<List<ValueLocation>> FindValueInFilesAsync(string value, string searchPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a specific value within all messages in a directory.
    /// </summary>
    Task<List<ValueLocation>> FindValueInDirectoryAsync(string value, string directory, CancellationToken cancellationToken = default);
}