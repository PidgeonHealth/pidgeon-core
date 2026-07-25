// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Clinical;
using Pidgeon.Core.Application.Interfaces.Data;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pidgeon.Core.Application.Services.Clinical.Realism;

/// <summary>
/// Loads the cited population-normal reference intervals (reference-intervals.yaml, served through
/// the registered <see cref="IDataResourceResolver"/> composition) keyed by LOINC. The single
/// OBX-7 / marginal-shape source both value paths read (RI-1). Cached for process lifetime,
/// mirroring <see cref="ReferenceRangeLoader"/>. Graceful: an absent corpus yields an empty map,
/// so uncovered analytes take the honest whole-or-absent path rather than failing.
/// </summary>
public sealed partial class ReferenceIntervalProvider : IReferenceIntervalProvider
{
    private readonly ILogger<ReferenceIntervalProvider> _logger;
    private readonly IDataResourceResolver _resourceResolver;
    private readonly string _resourcePath;
    private readonly Lazy<IReadOnlyDictionary<string, AnalyteReferenceInterval>> _byLoinc;

    public ReferenceIntervalProvider(
        ILogger<ReferenceIntervalProvider> logger,
        IDataResourceResolver resourceResolver,
        string resourcePath = "clinical/reference-intervals.yaml")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
        _resourcePath = resourcePath ?? throw new ArgumentNullException(nameof(resourcePath));
        _byLoinc = new Lazy<IReadOnlyDictionary<string, AnalyteReferenceInterval>>(Load);
    }

    public AnalyteReferenceInterval? Get(string loincCode)
    {
        if (string.IsNullOrWhiteSpace(loincCode))
            return null;
        return _byLoinc.Value.TryGetValue(loincCode.Trim(), out var interval) ? interval : null;
    }

    public IReadOnlyCollection<string> CoveredLoincCodes() => _byLoinc.Value.Keys.ToList();

    private IReadOnlyDictionary<string, AnalyteReferenceInterval> Load()
    {
        // One-time process-lifetime load inside the Lazy; the registered resolvers complete
        // synchronously, so blocking here cannot deadlock.
        var streamResult = _resourceResolver.OpenReadAsync(_resourcePath).AsTask().GetAwaiter().GetResult();
        if (streamResult.IsFailure)
        {
            _logger.LogWarning("reference-intervals.yaml unavailable: {Error}", streamResult.Error);
            return new Dictionary<string, AnalyteReferenceInterval>();
        }

        try
        {
            string text;
            using (var stream = streamResult.Value)
            using (var reader = new StreamReader(stream))
            {
                text = reader.ReadToEnd();
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var doc = deserializer.Deserialize<IntervalDocument>(text);

            var map = new Dictionary<string, AnalyteReferenceInterval>(StringComparer.Ordinal);
            foreach (var row in doc?.Intervals ?? new List<IntervalRow>())
            {
                var loinc = row.Loinc?.Trim();
                var unit = row.Unit?.Trim();
                if (string.IsNullOrEmpty(loinc) || string.IsNullOrEmpty(unit))
                    continue;

                // A usable interval must be ordered and non-degenerate.
                if (!(row.Low < row.High))
                    continue;

                var shape = string.Equals(row.Shape?.Trim(), "lognormal", StringComparison.OrdinalIgnoreCase)
                    ? MarginalShape.Lognormal
                    : MarginalShape.Normal;

                map[loinc] = new AnalyteReferenceInterval(
                    loinc, row.Analyte?.Trim() ?? loinc, unit, row.Low, row.High, shape);
            }

            _logger.LogDebug("Loaded population-normal reference intervals for {Count} LOINC codes", map.Count);
            return map;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse reference-intervals.yaml");
            return new Dictionary<string, AnalyteReferenceInterval>();
        }
    }

    private sealed class IntervalDocument
    {
        public List<IntervalRow> Intervals { get; set; } = new();
    }

    private sealed class IntervalRow
    {
        public string? Loinc { get; set; }
        public string? Analyte { get; set; }
        public string? Unit { get; set; }
        public double Low { get; set; }
        public double High { get; set; }
        public string? Shape { get; set; }
    }
}
