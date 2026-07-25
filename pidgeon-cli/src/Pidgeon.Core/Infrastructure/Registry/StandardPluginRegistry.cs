// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Standards;

namespace Pidgeon.Core.Infrastructure.Registry;

/// <summary>
/// Implementation of the standard plugin registry supporting both message generation
/// and configuration analysis plugins.
/// </summary>
internal class StandardPluginRegistry : IStandardPluginRegistry
{
    private readonly IEnumerable<IStandardPlugin> _plugins;
    private readonly IEnumerable<IStandardFormatAnalysisPlugin> _formatAnalysisPlugins;
    private readonly IEnumerable<IStandardVendorDetectionPlugin> _vendorDetectionPlugins;
    private readonly IEnumerable<IStandardFieldAnalysisPlugin> _fieldAnalysisPlugins;
    private readonly ILogger<StandardPluginRegistry> _logger;

    public StandardPluginRegistry(
        IEnumerable<IStandardPlugin> plugins,
        IEnumerable<IStandardFormatAnalysisPlugin> formatAnalysisPlugins,
        IEnumerable<IStandardVendorDetectionPlugin> vendorDetectionPlugins,
        IEnumerable<IStandardFieldAnalysisPlugin> fieldAnalysisPlugins,
        ILogger<StandardPluginRegistry> logger)
    {
        _plugins = plugins;
        _formatAnalysisPlugins = formatAnalysisPlugins;
        _vendorDetectionPlugins = vendorDetectionPlugins;
        _fieldAnalysisPlugins = fieldAnalysisPlugins;
        _logger = logger;
        
        LogRegisteredPlugins();
    }

    public IReadOnlyList<IStandardPlugin> GetAllPlugins()
    {
        return _plugins.ToList();
    }

    public IStandardPlugin? GetPlugin(string standardName, Version? standardVersion = null)
    {
        // Plugins return the canonical lowercase StandardNames token, so the lookup is an exact
        // ordinal match with no case-folding compensation needed.
        var candidates = _plugins.Where(p =>
            string.Equals(p.StandardName, standardName, StringComparison.Ordinal))
            .ToList();

        if (!candidates.Any())
            return null;

        if (standardVersion == null)
        {
            // Return the plugin with the highest version
            return candidates.OrderByDescending(p => p.StandardVersion).First();
        }

        // Return exact version match
        return candidates.FirstOrDefault(p => p.StandardVersion == standardVersion);
    }

    public IReadOnlyList<IStandardPlugin> GetCapablePlugins(string messageContent)
    {
        if (string.IsNullOrWhiteSpace(messageContent))
            return Array.Empty<IStandardPlugin>();

        var capablePlugins = new List<IStandardPlugin>();

        foreach (var plugin in _plugins)
        {
            try
            {
                if (plugin.CanHandle(messageContent))
                {
                    capablePlugins.Add(plugin);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, 
                    "Plugin {PluginName} threw exception when checking if it can handle message", 
                    plugin.StandardName);
            }
        }

        return capablePlugins;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetSupportedMessageTypes()
    {
        return _plugins.ToDictionary(
            p => $"{p.StandardName} v{p.StandardVersion}",
            p => p.SupportedMessageTypes);
    }

    public IStandardFormatAnalysisPlugin? GetFormatAnalysisPlugin(string standard)
    {
        return _formatAnalysisPlugins.FirstOrDefault(p => p.CanHandle(standard));
    }

    public IStandardVendorDetectionPlugin? GetVendorDetectionPlugin(string standard)
    {
        return _vendorDetectionPlugins.FirstOrDefault(p => p.CanHandle(standard));
    }

    public IStandardFieldAnalysisPlugin? GetFieldAnalysisPlugin(string standard)
    {
        return _fieldAnalysisPlugins.FirstOrDefault(p => p.CanHandle(standard));
    }

    private void LogRegisteredPlugins()
    {
        var totalPlugins = _plugins.Count() + _formatAnalysisPlugins.Count() + 
                          _vendorDetectionPlugins.Count() + _fieldAnalysisPlugins.Count();

        if (totalPlugins == 0)
        {
            _logger.LogWarning("No plugins are registered");
            return;
        }

        _logger.LogInformation("Registered {PluginCount} total plugins:", totalPlugins);
        
        // Log standard plugins
        foreach (var plugin in _plugins)
        {
            _logger.LogInformation("  - Standard: {StandardName} v{StandardVersion}: {MessageTypeCount} message types", 
                plugin.StandardName, 
                plugin.StandardVersion, 
                plugin.SupportedMessageTypes.Count);
        }

        // Log analysis plugins
        foreach (var plugin in _formatAnalysisPlugins)
        {
            _logger.LogInformation("  - Format Analysis: {StandardName}", plugin.StandardName);
        }

        foreach (var plugin in _vendorDetectionPlugins)
        {
            _logger.LogInformation("  - Vendor Detection: {StandardName}", plugin.StandardName);
        }

        foreach (var plugin in _fieldAnalysisPlugins)
        {
            _logger.LogInformation("  - Field Analysis: {StandardName}", plugin.StandardName);
        }
    }
}