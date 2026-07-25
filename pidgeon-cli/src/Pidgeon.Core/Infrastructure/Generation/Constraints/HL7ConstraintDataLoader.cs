// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;

namespace Pidgeon.Core.Infrastructure.Generation.Constraints;

/// <summary>
/// Loads HL7 v2.3 segment and table JSON through the registered
/// <see cref="IDataResourceResolver"/> composition for
/// <see cref="HL7ConstraintResolverPlugin"/>. Absent data degrades to null,
/// which the constraint extractor treats as "no constraint data" — the solver
/// then generates unconstrained rather than failing.
/// </summary>
public sealed partial class HL7ConstraintDataLoader
{
    private readonly ILogger<HL7ConstraintDataLoader> _logger;
    private readonly IDataResourceResolver _resourceResolver;
    private readonly string _dataPathPrefix;

    public HL7ConstraintDataLoader(
        ILogger<HL7ConstraintDataLoader> logger,
        IDataResourceResolver resourceResolver,
        string dataPathPrefix = "standards/hl7/v23")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
        _dataPathPrefix = dataPathPrefix ?? throw new ArgumentNullException(nameof(dataPathPrefix));
    }

    /// <summary>
    /// Loads the JSON body of a segment definition (e.g., <c>pid</c>,
    /// <c>msh</c>). Returns null when the resource is not installed.
    /// </summary>
    public Task<string?> LoadSegmentDataAsync(string segmentName)
        => LoadResourceTextAsync($"{_dataPathPrefix}/segments/{segmentName}.json");

    /// <summary>
    /// Loads the JSON body of a coded table (e.g., <c>0001</c>). Returns null
    /// when the resource is not installed.
    /// </summary>
    public Task<string?> LoadTableDataAsync(string tableId)
        => LoadResourceTextAsync($"{_dataPathPrefix}/tables/{tableId}.json");

    private async Task<string?> LoadResourceTextAsync(string resourcePath)
    {
        try
        {
            var streamResult = await _resourceResolver.OpenReadAsync(resourcePath);
            if (streamResult.IsFailure)
            {
                _logger.LogDebug("Constraint data resource unavailable: {ResourcePath}", resourcePath);
                return null;
            }

            using var stream = streamResult.Value;
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading constraint data resource {ResourcePath}", resourcePath);
            return null;
        }
    }
}
