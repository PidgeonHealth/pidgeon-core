// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Standards.HL7;
using Pidgeon.Core.Common;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default <see cref="IHL7VersionResolver"/>: resolves version-specific providers and the
/// trigger-event definition, with ascending auto-fallback when the requested version lacks the
/// event (e.g. RDE^O11 is absent in v2.3 but present in v2.4+). Deterministic: no RNG, no
/// clock, so selection depends only on the requested options and the embedded reference data.
/// </summary>
public class HL7VersionResolver : IHL7VersionResolver
{
    private readonly IHL7DataProviderFactory _dataProviderFactory;
    private readonly ILogger<HL7VersionResolver> _logger;

    public HL7VersionResolver(IHL7DataProviderFactory dataProviderFactory, ILogger<HL7VersionResolver> logger)
    {
        _dataProviderFactory = dataProviderFactory ?? throw new ArgumentNullException(nameof(dataProviderFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<Result<HL7VersionResolution>> ResolveAsync(string messageType, string triggerEventCode, GenerationOptions options)
    {
        // Resolve version-specific providers via factory: every path reads the requested version.
        var version = options.Hl7Version;
        var triggerEventProvider = _dataProviderFactory.GetTriggerEventProvider(version);
        var segmentProvider = _dataProviderFactory.GetSegmentProvider(version);
        var dataTypeProvider = _dataProviderFactory.GetDataTypeProvider(version);
        var tableProvider = _dataProviderFactory.GetTableProvider(version);

        // Load trigger event definition from JSON
        var triggerEventResult = await triggerEventProvider.GetTriggerEventAsync(triggerEventCode);
        if (triggerEventResult.IsFailure)
        {
            if (options.Hl7VersionExplicit)
            {
                // User explicitly requested this version — find which version has it for a helpful error
                var availableIn = await FindVersionsWithTriggerEventAsync(triggerEventCode);
                var hint = availableIn.Count > 0
                    ? $" This trigger event is available in: {string.Join(", ", availableIn.Select(v => $"v{v}"))}."
                    : "";
                return Result<HL7VersionResolution>.Failure(
                    $"Trigger event {messageType} does not exist in HL7 v{version}.{hint}");
            }

            // No explicit version — try ascending versions automatically
            // (e.g., RDE^O11 doesn't exist in v2.3 but does in v2.4+)
            var supportedVersions = _dataProviderFactory.GetSupportedVersions();
            foreach (var fallbackVersion in supportedVersions)
            {
                if (string.Compare(fallbackVersion, version, StringComparison.Ordinal) <= 0)
                    continue;

                var fallbackProvider = _dataProviderFactory.GetTriggerEventProvider(fallbackVersion);
                var fallbackResult = await fallbackProvider.GetTriggerEventAsync(triggerEventCode);
                if (fallbackResult.IsSuccess)
                {
                    triggerEventResult = fallbackResult;
                    version = fallbackVersion;
                    segmentProvider = _dataProviderFactory.GetSegmentProvider(fallbackVersion);
                    dataTypeProvider = _dataProviderFactory.GetDataTypeProvider(fallbackVersion);
                    tableProvider = _dataProviderFactory.GetTableProvider(fallbackVersion);
                    _logger.LogInformation("Trigger event {Code} not found in v{Requested}, auto-selected v{Fallback}",
                        triggerEventCode, options.Hl7Version, fallbackVersion);
                    break;
                }
            }

            if (triggerEventResult.IsFailure)
                return Result<HL7VersionResolution>.Failure($"Trigger event {messageType} not found in any supported HL7 version.");
        }

        return Result<HL7VersionResolution>.Success(new HL7VersionResolution(
            version, triggerEventResult.Value, segmentProvider, dataTypeProvider, tableProvider));
    }

    /// <summary>
    /// Searches all supported HL7 versions to find which ones contain a given trigger event.
    /// Used for helpful error messages when an explicit version doesn't have the requested event.
    /// </summary>
    private async Task<List<string>> FindVersionsWithTriggerEventAsync(string triggerEventCode)
    {
        var versions = new List<string>();
        foreach (var v in _dataProviderFactory.GetSupportedVersions())
        {
            var provider = _dataProviderFactory.GetTriggerEventProvider(v);
            var result = await provider.GetTriggerEventAsync(triggerEventCode);
            if (result.IsSuccess)
                versions.Add(v);
        }
        return versions;
    }
}
