// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Standards;

/// <summary>
/// Registry for managing standard plugins and analysis plugins.
/// Supports both message generation/validation plugins and configuration analysis plugins.
/// </summary>
public interface IStandardPluginRegistry
{
    /// <summary>
    /// Gets all registered standard plugins.
    /// </summary>
    /// <returns>A list of all registered standard plugins</returns>
    IReadOnlyList<IStandardPlugin> GetAllPlugins();

    /// <summary>
    /// Gets a standard plugin by standard name and version.
    /// </summary>
    /// <param name="standardName">The canonical lowercase standard token (a <c>StandardNames</c>
    /// value, e.g. "hl7"). The lookup is an exact ordinal match — plugins register under the
    /// canonical token, so a mixed-case value (e.g. "HL7") will not resolve.</param>
    /// <param name="standardVersion">The standard version (optional)</param>
    /// <returns>The plugin if found, null otherwise</returns>
    IStandardPlugin? GetPlugin(string standardName, Version? standardVersion = null);

    /// <summary>
    /// Gets standard plugins that can handle the specified message content.
    /// </summary>
    /// <param name="messageContent">The message content</param>
    /// <returns>A list of plugins that can handle the message</returns>
    IReadOnlyList<IStandardPlugin> GetCapablePlugins(string messageContent);

    /// <summary>
    /// Gets supported message types across all standard plugins.
    /// </summary>
    /// <returns>A dictionary mapping standards to their supported message types</returns>
    IReadOnlyDictionary<string, IReadOnlyList<string>> GetSupportedMessageTypes();

    /// <summary>
    /// Gets a format analysis plugin that can handle the specified standard.
    /// </summary>
    /// <param name="standard">Healthcare standard name (HL7v23, FHIRv4, etc.)</param>
    /// <returns>The format analysis plugin if found, null otherwise</returns>
    IStandardFormatAnalysisPlugin? GetFormatAnalysisPlugin(string standard);

    /// <summary>
    /// Gets a vendor detection plugin that can handle the specified standard.
    /// </summary>
    /// <param name="standard">Healthcare standard name (HL7v23, FHIRv4, etc.)</param>
    /// <returns>The vendor detection plugin if found, null otherwise</returns>
    IStandardVendorDetectionPlugin? GetVendorDetectionPlugin(string standard);

    /// <summary>
    /// Gets a field analysis plugin that can handle the specified standard.
    /// </summary>
    /// <param name="standard">Healthcare standard name (HL7v23, FHIRv4, etc.)</param>
    /// <returns>The field analysis plugin if found, null otherwise</returns>
    IStandardFieldAnalysisPlugin? GetFieldAnalysisPlugin(string standard);
}