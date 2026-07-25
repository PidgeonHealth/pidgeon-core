// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Domain.Reference.Entities;

namespace Pidgeon.Core.Infrastructure.Reference.HL7;

/// <summary>
/// Loads HL7 trigger event definitions (<c>A01</c>, <c>O01</c>) and
/// composite message-type entries (<c>ADT_A01</c>).
/// </summary>
public sealed class TriggerEventLoader
{
    private readonly HL7ReferenceJsonLoader _jsonLoader;
    private readonly ILogger<TriggerEventLoader> _logger;

    public TriggerEventLoader(HL7ReferenceJsonLoader jsonLoader, ILogger<TriggerEventLoader> logger)
    {
        _jsonLoader = jsonLoader;
        _logger = logger;
    }

    public async Task<StandardElement?> LoadTriggerEventAsync(
        HL7ResourceContext context,
        string triggerCode,
        CancellationToken cancellationToken)
    {
        var relative = $"trigger_events/{triggerCode.ToLowerInvariant()}.json";
        if (!_jsonLoader.Exists(context, relative))
            return null;

        try
        {
            var json = await _jsonLoader.LoadAsync(context, relative, cancellationToken);

            var triggerData = JsonSerializer.Deserialize<JsonElement>(json, HL7ReferenceJsonLoader.JsonOptions);

            return new StandardElement
            {
                Path = triggerCode.ToUpperInvariant(),
                Name = triggerData.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Description = triggerData.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                Standard = context.Config.StandardId,
                Version = context.Config.Version,
                DataType = "TriggerEvent",
                Usage = triggerData.TryGetProperty("usage", out var usage) ? usage.GetString() ?? "" : "",
                Examples = HL7JsonExtraction.ExtractExamples(triggerData)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading trigger event {TriggerCode}", triggerCode);
            return null;
        }
    }

    public async Task<StandardElement?> LoadMessageTypeAsync(
        HL7ResourceContext context,
        string messageType,
        CancellationToken cancellationToken)
    {
        var relative = $"trigger_events/{messageType.ToLowerInvariant()}.json";
        if (!_jsonLoader.Exists(context, relative))
            return null;

        try
        {
            var json = await _jsonLoader.LoadAsync(context, relative, cancellationToken);

            var triggerData = JsonSerializer.Deserialize<JsonElement>(json, HL7ReferenceJsonLoader.JsonOptions);

            return new StandardElement
            {
                Path = messageType.ToUpperInvariant(),
                Name = triggerData.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Description = triggerData.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                Standard = context.Config.StandardId,
                Version = context.Config.Version,
                DataType = "MessageType",
                Usage = triggerData.TryGetProperty("usage", out var usage) ? usage.GetString() ?? "" : "",
                Examples = HL7JsonExtraction.ExtractExamples(triggerData)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading message type {MessageType}", messageType);
            return null;
        }
    }

    public IEnumerable<string> EnumerateTriggerEventFiles(HL7ResourceContext context)
        => _jsonLoader.EnumerateFiles(context, "trigger_events");
}
