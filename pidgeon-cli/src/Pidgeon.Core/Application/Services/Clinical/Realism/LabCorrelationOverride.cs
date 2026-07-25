// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Pidgeon.Core.Application.Services.Clinical.Realism;

/// <summary>
/// A customer's observed LOINC-pair Spearman correlations, injected per-draw into the copula
/// (ADR-0010 Tier B, B1). Where a pair is covered, <see cref="LabValueEngine"/> uses the customer
/// rho instead of the cited-literature prior; uncovered pairs keep the prior. Immutable and passed
/// per call — the engine is a singleton and must never cache a customer's overlay.
///
/// Pairs are keyed by the SAME LOINC-ordinal orientation the yaml provider uses
/// (<see cref="LabCorrelationProvider.OrderPair"/>), so A~B and B~A are one pair here exactly as
/// they are there — a divergent normalization would silently miss lookups.
/// </summary>
public sealed class LabCorrelationOverride
{
    private readonly IReadOnlyDictionary<(string, string), double> _byPair;

    private LabCorrelationOverride(IReadOnlyDictionary<(string, string), double> byPair) => _byPair = byPair;

    /// <summary>
    /// The pair-orientation contract: pairs are symmetric, stored LOINC-ordinal-sorted. Lives on the
    /// override (community-admitted) so <see cref="LabCorrelationProvider"/> keys the curated data
    /// identically (B1) — two normalizations would silently miss each other's lookups.
    /// </summary>
    internal static (string, string) OrderPair(string a, string b)
        => string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);

    /// <summary>The number of usable pairs the overlay carries.</summary>
    public int Count => _byPair.Count;

    /// <summary>The customer rho for a pair (either orientation), or false when uncovered.</summary>
    public bool TryGetRho(string loincA, string loincB, out double rho)
    {
        rho = 0.0;
        if (string.IsNullOrWhiteSpace(loincA) || string.IsNullOrWhiteSpace(loincB)) return false;
        return _byPair.TryGetValue(OrderPair(loincA.Trim(), loincB.Trim()), out rho);
    }

    /// <summary>
    /// Builds an overlay from observed pairs, or null when nothing usable remains — the signal that
    /// keeps the copula on the prior. Unusable pairs are dropped, never repaired: NaN or |rho| &gt; 1
    /// (not a correlation), self-pairs (the diagonal is fixed at 1), blank LOINCs. Last-wins on a
    /// duplicate pair in either orientation.
    /// </summary>
    public static LabCorrelationOverride? FromPairs(
        IEnumerable<(string A, string B, double SpearmanRho)>? pairs)
    {
        if (pairs is null) return null;

        var map = new Dictionary<(string, string), double>();
        foreach (var (a, b, rho) in pairs)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) continue;
            var (ta, tb) = (a.Trim(), b.Trim());
            if (string.Equals(ta, tb, StringComparison.Ordinal)) continue;
            if (double.IsNaN(rho) || Math.Abs(rho) > 1.0) continue;
            map[OrderPair(ta, tb)] = rho;
        }

        return map.Count > 0 ? new LabCorrelationOverride(map) : null;
    }
}
