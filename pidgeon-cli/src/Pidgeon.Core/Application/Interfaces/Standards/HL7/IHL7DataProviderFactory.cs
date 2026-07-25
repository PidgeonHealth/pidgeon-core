// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Application.Interfaces.Standards.HL7;

/// <summary>
/// Factory for creating version-specific HL7 data providers.
/// Maps HL7 version strings to the correct embedded resource path prefixes
/// and caches provider instances per version.
/// </summary>
public interface IHL7DataProviderFactory
{
    /// <summary>
    /// Gets a trigger event provider for the specified HL7 version.
    /// </summary>
    IHL7TriggerEventProvider GetTriggerEventProvider(string version);

    /// <summary>
    /// Gets a segment provider for the specified HL7 version.
    /// </summary>
    IHL7SegmentProvider GetSegmentProvider(string version);

    /// <summary>
    /// Gets a data type provider for the specified HL7 version.
    /// </summary>
    IHL7DataTypeProvider GetDataTypeProvider(string version);

    /// <summary>
    /// Gets a table provider for the specified HL7 version.
    /// </summary>
    IHL7TableProvider GetTableProvider(string version);

    /// <summary>
    /// Gets all supported HL7 version strings (e.g., "2.3", "2.3.1", "2.4", ...).
    /// </summary>
    IReadOnlyList<string> GetSupportedVersions();

    /// <summary>
    /// Returns true if the specified version has embedded reference data.
    /// </summary>
    bool IsVersionSupported(string version);
}
