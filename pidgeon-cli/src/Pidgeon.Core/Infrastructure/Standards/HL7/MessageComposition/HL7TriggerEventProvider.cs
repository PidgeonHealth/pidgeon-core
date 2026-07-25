// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Provides access to HL7 v2.3 trigger event definitions from embedded JSON resources.
/// Loads trigger event structures that define message segment composition.
/// </summary>
public partial class HL7TriggerEventProvider : IHL7TriggerEventProvider
{
    private readonly ILogger<HL7TriggerEventProvider> _logger;
    private readonly IDataResourceResolver _resourceResolver;
    private readonly string _dataPathPrefix;
    // ConcurrentDictionary so this provider is safe to register as a Singleton,
    // shared across all DI scopes and generation threads.
    private readonly ConcurrentDictionary<string, TriggerEvent> _cache = new();

    public HL7TriggerEventProvider(
        ILogger<HL7TriggerEventProvider> logger,
        IDataResourceResolver resourceResolver,
        string dataPathPrefix = "standards/hl7/v23")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
        _dataPathPrefix = dataPathPrefix ?? throw new ArgumentNullException(nameof(dataPathPrefix));
    }

    /// <summary>
    /// Gets a trigger event definition by code (e.g., "adt_a01", "orm_o01").
    /// Loads from the registered data-resource composition.
    /// </summary>
    public async Task<Result<TriggerEvent>> GetTriggerEventAsync(string triggerEventCode)
    {
        try
        {
            // Normalize the trigger event code
            var normalizedCode = triggerEventCode.ToLowerInvariant();

            // Check cache first
            if (_cache.TryGetValue(normalizedCode, out var cachedEvent))
            {
                return Result<TriggerEvent>.Success(cachedEvent);
            }

            // Build resource name for embedded JSON file
            var resourcePath = $"{_dataPathPrefix}/trigger_events/{normalizedCode}.json";

            _logger.LogDebug("Loading trigger event {TriggerEventCode} from resource {ResourcePath}",
                triggerEventCode, resourcePath);

            var streamResult = await _resourceResolver.OpenReadAsync(resourcePath);
            if (streamResult.IsFailure)
            {
                _logger.LogWarning("Trigger event resource unavailable: {ResourcePath}", resourcePath);
                return Result<TriggerEvent>.Failure(streamResult.Error);
            }

            using var stream = streamResult.Value;
            // Parse JSON to trigger event definition
            var json = await new StreamReader(stream).ReadToEndAsync();
            var jsonDoc = JsonDocument.Parse(json);
            var triggerEvent = ParseTriggerEventFromJson(jsonDoc.RootElement);

            // Cache for future requests
            _cache[normalizedCode] = triggerEvent;

            _logger.LogDebug("Successfully loaded trigger event {TriggerEventCode} with {SegmentCount} segments",
                triggerEventCode, triggerEvent.SegmentCount);

            return Result<TriggerEvent>.Success(triggerEvent);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Malformed trigger event resource {TriggerEventCode}", triggerEventCode);
            return Result<TriggerEvent>.Failure(Error.Create(
                "MALFORMED_DATA_RESOURCE", $"Trigger event resource is not valid JSON: {ex.Message}", triggerEventCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading trigger event {TriggerEventCode}", triggerEventCode);
            return Result<TriggerEvent>.Failure($"Failed to load trigger event: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all available trigger event codes from embedded resources.
    /// </summary>
    public Task<Result<IReadOnlyList<string>>> GetAvailableTriggerEventsAsync()
    {
        try
        {
            var prefix = $"{_dataPathPrefix}/trigger_events";
            var pathsResult = _resourceResolver.ListResourcePaths(prefix);
            if (pathsResult.IsFailure)
                return Task.FromResult(Result<IReadOnlyList<string>>.Failure(pathsResult.Error));

            var resourceNames = pathsResult.Value
                .Where(name => name.EndsWith(".json", StringComparison.Ordinal))
                .Select(path => Path.GetFileNameWithoutExtension(path)!)
                .OrderBy(code => code)
                .ToList();

            _logger.LogDebug("Found {Count} available trigger events", resourceNames.Count);

            return Task.FromResult(Result<IReadOnlyList<string>>.Success(resourceNames.AsReadOnly()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available trigger events");
            return Task.FromResult(Result<IReadOnlyList<string>>.Failure($"Failed to get trigger events: {ex.Message}"));
        }
    }

    /// <summary>
    /// Parses trigger event JSON structure into strongly-typed object.
    /// </summary>
    private TriggerEvent ParseTriggerEventFromJson(JsonElement root)
    {
        var code = root.GetProperty("code").GetString() ?? "";
        var name = root.GetProperty("name").GetString() ?? "";
        var version = root.GetProperty("version").GetString() ?? "";
        var chapter = root.GetProperty("chapter").GetString() ?? "";
        var description = root.GetProperty("description").GetString() ?? "";
        var segmentCount = root.GetProperty("segment_count").GetInt32();

        var segments = new List<TriggerEventSegment>();
        if (root.TryGetProperty("segments", out var segmentsArray))
        {
            foreach (var segmentElement in segmentsArray.EnumerateArray())
            {
                var segment = ParseTriggerEventSegmentFromJson(segmentElement);
                segments.Add(segment);
            }
        }

        var messageStructure = root.TryGetProperty("message_structure", out var structureProp)
            ? structureProp.GetString()
            : null;

        return new TriggerEvent(
            Code: code,
            Name: name,
            Version: version,
            Chapter: chapter,
            Description: description,
            Segments: segments.AsReadOnly(),
            SegmentCount: segmentCount,
            MessageStructure: string.IsNullOrWhiteSpace(messageStructure) ? null : messageStructure
        );
    }

    /// <summary>
    /// Parses trigger event segment JSON structure into strongly-typed object.
    /// </summary>
    private TriggerEventSegment ParseTriggerEventSegmentFromJson(JsonElement segmentElement)
    {
        var segmentCode = segmentElement.GetProperty("segment_code").GetString() ?? "";
        var segmentDesc = segmentElement.GetProperty("segment_desc").GetString() ?? "";
        var optionality = segmentElement.GetProperty("optionality").GetString() ?? "";
        var repeatability = segmentElement.GetProperty("repeatability").GetString() ?? "";
        var isGroup = segmentElement.GetProperty("is_group").GetBoolean();
        var level = segmentElement.GetProperty("level").GetInt32();
        var orderIndex = segmentElement.GetProperty("order_index").GetInt32();

        var groupPath = new List<string>();
        if (segmentElement.TryGetProperty("group_path", out var groupPathArray))
        {
            foreach (var pathElement in groupPathArray.EnumerateArray())
            {
                var pathSegment = pathElement.GetString();
                if (!string.IsNullOrEmpty(pathSegment))
                {
                    groupPath.Add(pathSegment);
                }
            }
        }

        // Optional statistical knobs: per-segment probabilities carried in the trigger-event JSON.
        // Absent in most trigger-event files; generation falls back to HL7SegmentStatistics.
        double? inclusionProbability = null;
        if (segmentElement.TryGetProperty("inclusion_probability", out var probabilityElement)
            && probabilityElement.ValueKind == JsonValueKind.Number)
        {
            inclusionProbability = probabilityElement.GetDouble();
        }

        int? repeatMin = null;
        int? repeatMax = null;
        if (segmentElement.TryGetProperty("repeat_range", out var repeatRangeElement)
            && repeatRangeElement.ValueKind == JsonValueKind.Array
            && repeatRangeElement.GetArrayLength() == 2)
        {
            repeatMin = repeatRangeElement[0].GetInt32();
            repeatMax = repeatRangeElement[1].GetInt32();
        }

        return new TriggerEventSegment(
            SegmentCode: segmentCode,
            SegmentDesc: segmentDesc,
            Optionality: optionality,
            Repeatability: repeatability,
            IsGroup: isGroup,
            GroupPath: groupPath.AsReadOnly(),
            Level: level,
            OrderIndex: orderIndex,
            InclusionProbability: inclusionProbability,
            RepeatMin: repeatMin,
            RepeatMax: repeatMax
        );
    }
}
