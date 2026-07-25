// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Domain.Reference.Entities;
using Pidgeon.Core.Infrastructure.Reference.HL7;

namespace Pidgeon.Core.Infrastructure.Reference;

/// <summary>
/// Lookup internals for <see cref="JsonHL7ReferencePlugin"/>: element-kind
/// routing, walk-all discovery, suggestion scoring, and vendor variation.
/// The public orchestration surface lives in the sibling partial.
/// </summary>
public partial class JsonHL7ReferencePlugin
{
    public async Task<Result<IReadOnlyList<string>>> GetSuggestionsAsync(string path, CancellationToken cancellationToken = default)
    {
        var context = EnsureInitialized();
        var suggestions = new List<string>();

        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return Result<IReadOnlyList<string>>.Success(suggestions);

            if (!path.Contains('.'))
            {
                var segment = path.ToUpperInvariant();
                var segmentFile = $"segments/{Pidgeon.Core.Infrastructure.Standards.HL7.HL7ReservedResourceName.ResourceStem(segment)}.json";

                if (_jsonLoader.Exists(context, segmentFile))
                {
                    var segmentElements = await _segmentLoader.LoadAllSegmentElementsAsync(context, segmentFile, cancellationToken);
                    suggestions.AddRange(segmentElements
                        .Where(e => e.Path.Count(c => c == '.') == 1)
                        .Select(e => e.Path)
                        .Take(5));
                }
            }
            else
            {
                var allElements = await LoadAllElementsAsync(context, cancellationToken);
                var similarPaths = allElements
                    .Where(e => LevenshteinDistance(e.Path, path.ToUpperInvariant()) <= 2)
                    .Select(e => e.Path)
                    .Take(5)
                    .ToList();
                suggestions.AddRange(similarPaths);
            }

            return Result<IReadOnlyList<string>>.Success(suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting suggestions for path {Path}", path);
            return Result<IReadOnlyList<string>>.Success(suggestions);
        }
    }

    public async Task<Result<StandardElement>> GetVendorVariationAsync(string path, string vendor, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var lookupResult = await LookupAsync(path, cancellationToken);
        if (lookupResult.IsFailure)
            return lookupResult;

        var element = lookupResult.Value;
        var vendorVariation = element.VendorVariations
            .FirstOrDefault(v => string.Equals(v.Vendor, vendor, StringComparison.OrdinalIgnoreCase));

        if (vendorVariation == null)
        {
            var baseElement = element with
            {
                VendorVariations = [new VendorNote
                {
                    Vendor = vendor,
                    Note = $"No {vendor}-specific variations documented for this field. Using standard {_config.StandardName} definition.",
                    Examples = []
                }]
            };
            return Result<StandardElement>.Success(baseElement);
        }

        return Result<StandardElement>.Success(element);
    }

    private async Task<StandardElement?> LoadElementAsync(HL7ResourceContext context, string path, CancellationToken cancellationToken)
    {
        var normalizedPath = path.ToUpperInvariant();
        var pathParts = normalizedPath.Split('.');

        if (pathParts.Length >= 2)
            return await _segmentLoader.LoadFieldAsync(context, normalizedPath, cancellationToken);

        if (pathParts.Length == 1)
        {
            var query = pathParts[0];

            // Only the 4-digit numeric-table form is unambiguous. Every other
            // pattern overlaps (NamedTablePattern matches segment names like PID
            // and trigger codes like A01), so ambiguous branches probe and fall
            // through on a miss instead of returning their null - a router that
            // dead-ends every bare segment/trigger name is not a lookup capability.
            if (query.Length == 4 && query.All(char.IsDigit))
                return await _tableLoader.LoadNumericTableAsync(context, query, cancellationToken);

            if (NamedTablePattern.IsMatch(query))
            {
                var namedTable = await _tableLoader.LoadNamedTableAsync(context, query, cancellationToken);
                if (namedTable != null) return namedTable;
            }

            if (MessageTypePattern.IsMatch(query))
            {
                var messageType = await _triggerEventLoader.LoadMessageTypeAsync(context, query, cancellationToken);
                if (messageType != null) return messageType;
            }

            if (Regex.IsMatch(query, @"^[A-Z]\d{2}$"))
            {
                var triggerEvent = await _triggerEventLoader.LoadTriggerEventAsync(context, query, cancellationToken);
                if (triggerEvent != null) return triggerEvent;
            }

            if (query.Length is >= 2 and <= 3 && query.All(char.IsLetter))
            {
                var segmentResult = await _segmentLoader.LoadSegmentAsync(context, query, cancellationToken);
                if (segmentResult != null) return segmentResult;

                var dataTypeResult = await _dataTypeLoader.LoadDataTypeAsync(context, query, cancellationToken);
                if (dataTypeResult != null) return dataTypeResult;
            }
        }

        return null;
    }

    private async Task<List<StandardElement>> LoadAllElementsAsync(HL7ResourceContext context, CancellationToken cancellationToken)
    {
        var allElements = new List<StandardElement>();

        try
        {
            foreach (var segmentFile in _jsonLoader.EnumerateFiles(context, "segments"))
            {
                var segmentElements = await _segmentLoader.LoadAllSegmentElementsAsync(context, segmentFile, cancellationToken);
                allElements.AddRange(segmentElements);
            }

            foreach (var tableFile in _jsonLoader.EnumerateFiles(context, "tables"))
            {
                var tableNumber = Path.GetFileNameWithoutExtension(tableFile);
                var tableElement = await _tableLoader.LoadNumericTableAsync(context, tableNumber, cancellationToken);
                if (tableElement != null) allElements.Add(tableElement);
            }

            foreach (var dataTypeFile in _jsonLoader.EnumerateFiles(context, "data_types"))
            {
                var dataTypeName = Path.GetFileNameWithoutExtension(dataTypeFile).ToUpperInvariant();
                var dataTypeElement = await _dataTypeLoader.LoadDataTypeAsync(context, dataTypeName, cancellationToken);
                if (dataTypeElement != null) allElements.Add(dataTypeElement);
            }

            foreach (var triggerFile in _jsonLoader.EnumerateFiles(context, "trigger_events"))
            {
                var triggerCode = Path.GetFileNameWithoutExtension(triggerFile).ToUpperInvariant();
                var triggerElement = await _triggerEventLoader.LoadTriggerEventAsync(context, triggerCode, cancellationToken);
                if (triggerElement != null) allElements.Add(triggerElement);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading all elements");
        }

        return allElements;
    }

    private bool IsKnownElement(string normalizedPath)
    {
        var context = EnsureInitialized();
        var lower = normalizedPath.ToLowerInvariant();

        return _jsonLoader.Exists(context, $"segments/{Pidgeon.Core.Infrastructure.Standards.HL7.HL7ReservedResourceName.ResourceStem(normalizedPath)}.json")
            || _jsonLoader.Exists(context, $"data_types/{lower}.json")
            || _jsonLoader.Exists(context, $"tables/{normalizedPath}.json")
            || _jsonLoader.Exists(context, $"trigger_events/{lower}.json");
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
        if (string.IsNullOrEmpty(s2)) return s1.Length;

        var distance = new int[s1.Length + 1, s2.Length + 1];
        for (int i = 0; i <= s1.Length; i++) distance[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++) distance[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[s1.Length, s2.Length];
    }
}
