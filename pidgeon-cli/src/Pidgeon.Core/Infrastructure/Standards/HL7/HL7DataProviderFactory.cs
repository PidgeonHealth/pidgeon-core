// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Standards.HL7;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Infrastructure.Standards.HL7;

/// <summary>
/// Creates and caches version-specific HL7 data providers.
/// Maps version strings to embedded resource path prefixes.
/// Falls back to v2.3 providers when requested version data is not available.
/// </summary>
public partial class HL7DataProviderFactory : IHL7DataProviderFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IDataResourceResolver _resourceResolver;
    private readonly ConcurrentDictionary<string, IHL7TriggerEventProvider> _triggerEventProviders = new();
    private readonly ConcurrentDictionary<string, IHL7SegmentProvider> _segmentProviders = new();
    private readonly ConcurrentDictionary<string, IHL7DataTypeProvider> _dataTypeProviders = new();
    private readonly ConcurrentDictionary<string, IHL7TableProvider> _tableProviders = new();

    private static readonly Dictionary<string, string> VersionToPrefixMap = new()
    {
        ["2.3"] = "standards/hl7/v23",
        ["2.3.1"] = "standards/hl7/v231",
        ["2.4"] = "standards/hl7/v24",
        ["2.5"] = "standards/hl7/v25",
        ["2.5.1"] = "standards/hl7/v251",
        ["2.6"] = "standards/hl7/v26",
        ["2.7"] = "standards/hl7/v27",
        ["2.8"] = "standards/hl7/v28"
    };

    private static readonly IReadOnlyList<string> SupportedVersions =
        VersionToPrefixMap.Keys.OrderBy(v => v).ToList().AsReadOnly();

    private const string DefaultVersion = "2.3";

    public HL7DataProviderFactory(
        ILoggerFactory loggerFactory,
        IDataResourceResolver resourceResolver)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
    }

    public IHL7TriggerEventProvider GetTriggerEventProvider(string version)
    {
        var prefix = ResolvePrefix(version);
        return _triggerEventProviders.GetOrAdd(prefix, p =>
            new HL7TriggerEventProvider(
                _loggerFactory.CreateLogger<HL7TriggerEventProvider>(), _resourceResolver, p));
    }

    public IHL7SegmentProvider GetSegmentProvider(string version)
    {
        var prefix = ResolvePrefix(version);
        return _segmentProviders.GetOrAdd(prefix, p =>
            new HL7SegmentProvider(
                _loggerFactory.CreateLogger<HL7SegmentProvider>(), _resourceResolver, p));
    }

    public IHL7DataTypeProvider GetDataTypeProvider(string version)
    {
        var prefix = ResolvePrefix(version);
        return _dataTypeProviders.GetOrAdd(prefix, p =>
            new HL7DataTypeProvider(
                _loggerFactory.CreateLogger<HL7DataTypeProvider>(), _resourceResolver, p));
    }

    public IHL7TableProvider GetTableProvider(string version)
    {
        var prefix = ResolvePrefix(version);
        return _tableProviders.GetOrAdd(prefix, p =>
            new HL7TableProvider(
                _loggerFactory.CreateLogger<HL7TableProvider>(), _resourceResolver, p));
    }

    public IReadOnlyList<string> GetSupportedVersions() => SupportedVersions;

    public bool IsVersionSupported(string version) => VersionToPrefixMap.ContainsKey(version);

    private static string ResolvePrefix(string version)
    {
        if (VersionToPrefixMap.TryGetValue(version, out var prefix))
            return prefix;

        // Fall back to v2.3 for unknown versions
        return VersionToPrefixMap[DefaultVersion];
    }
}
