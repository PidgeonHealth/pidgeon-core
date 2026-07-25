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
/// Loads the cited lab-lab Spearman correlations (lab-correlations.yaml, served through the
/// registered <see cref="IDataResourceResolver"/> composition) for the B2 copula. The Spearman rho
/// is stored as-cited; the Spearman->Pearson conversion for the copula's latent normals happens at
/// assemble time in <see cref="LabValueEngine"/>, not here (the yaml documents this). Cached for
/// process lifetime. Graceful: an absent corpus yields an empty map, so the copula stays on the
/// uncorrelated prior rather than failing.
/// </summary>
public sealed partial class LabCorrelationProvider : ILabCorrelationProvider
{
    private readonly ILogger<LabCorrelationProvider> _logger;
    private readonly IDataResourceResolver _resourceResolver;
    private readonly string _resourcePath;
    private readonly Lazy<IReadOnlyDictionary<(string, string), double>> _byPair;

    public LabCorrelationProvider(
        ILogger<LabCorrelationProvider> logger,
        IDataResourceResolver resourceResolver,
        string resourcePath = "clinical/lab-correlations.yaml")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
        _resourcePath = resourcePath ?? throw new ArgumentNullException(nameof(resourcePath));
        _byPair = new Lazy<IReadOnlyDictionary<(string, string), double>>(Load);
    }

    public double? SpearmanRho(string loincA, string loincB)
    {
        if (string.IsNullOrWhiteSpace(loincA) || string.IsNullOrWhiteSpace(loincB))
            return null;
        var (a, b) = OrderPair(loincA.Trim(), loincB.Trim());
        return _byPair.Value.TryGetValue((a, b), out var rho) ? rho : null;
    }

    public IReadOnlyCollection<(string A, string B, double SpearmanRho)> Pairs()
        => _byPair.Value.Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList();

    /// <summary>
    /// The pair-orientation contract lives on <see cref="LabCorrelationOverride"/> (the
    /// community-admitted half of the pair) — both halves must key identically (B1).
    /// </summary>
    internal static (string, string) OrderPair(string a, string b)
        => LabCorrelationOverride.OrderPair(a, b);

    private IReadOnlyDictionary<(string, string), double> Load()
    {
        // One-time process-lifetime load inside the Lazy; the registered resolvers complete
        // synchronously, so blocking here cannot deadlock.
        var streamResult = _resourceResolver.OpenReadAsync(_resourcePath).AsTask().GetAwaiter().GetResult();
        if (streamResult.IsFailure)
        {
            _logger.LogWarning("lab-correlations.yaml unavailable: {Error}", streamResult.Error);
            return new Dictionary<(string, string), double>();
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
            var doc = deserializer.Deserialize<CorrelationDocument>(text);

            var map = new Dictionary<(string, string), double>();
            foreach (var row in doc?.Correlations ?? new List<CorrelationRow>())
            {
                var a = row.MemberA?.Loinc?.Trim();
                var b = row.MemberB?.Loinc?.Trim();
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                    continue;
                map[OrderPair(a, b)] = row.SpearmanRho;
            }

            _logger.LogDebug("Loaded {Count} cited lab-lab correlation pairs", map.Count);
            return map;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse lab-correlations.yaml");
            return new Dictionary<(string, string), double>();
        }
    }

    private sealed class CorrelationDocument
    {
        public List<CorrelationRow> Correlations { get; set; } = new();
    }

    private sealed class CorrelationRow
    {
        public Member? MemberA { get; set; }
        public Member? MemberB { get; set; }
        public double SpearmanRho { get; set; }
    }

    private sealed class Member
    {
        public string? Loinc { get; set; }
        public string? Analyte { get; set; }
    }
}
