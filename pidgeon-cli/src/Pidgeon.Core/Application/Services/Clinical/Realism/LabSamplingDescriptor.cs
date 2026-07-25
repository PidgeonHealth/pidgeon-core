// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Interfaces.Clinical;

namespace Pidgeon.Core.Application.Services.Clinical.Realism;

/// <summary>
/// The condition-conditioned sampling descriptor for one analyte in a message/patient. It carries
/// the population-normal reference interval (OBX-7 fact, RI-1), plus the center and dispersion of the
/// condition-specific sampling distribution (RI-1: distinct from the interval).
///
/// The sampler draws value = F^-1(u; Center, Sigma, Interval.Shape). <see cref="Center"/> is where
/// this patient's value comes from — the population cohort mean plus any directed condition shift, so
/// a diabetic's glucose skews high; it is NOT the reference-interval midpoint (that would make the KS
/// gate score a shifted generation against a population reference and fail, RI-6). <see cref="Sigma"/>
/// is the dispersion. The shape (from <see cref="Interval"/>) chooses normal vs lognormal.
/// </summary>
public sealed record LabSamplingDescriptor(
    AnalyteReferenceInterval Interval,
    double Center,
    double Sigma,
    string? DisplayName = null,
    EmpiricalMarginal? Empirical = null)
{
    /// <summary>The OBX-3 display name to emit — the caller's ordered display when set, else the interval's analyte name.</summary>
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? Interval.Analyte : DisplayName!;

    /// <summary>
    /// A profiled descriptor (Tier-2): the marginal is a supplied empirical quantile ladder from a
    /// customer's real data, not a fitted normal/lognormal. When set, the sampler inverse-CDFs against
    /// the ladder, so the generated population marginal matches the customer's shape. The ladder is a
    /// monotone transform, so the copula's rank correlation is preserved exactly (RI-4).
    /// </summary>
    public static LabSamplingDescriptor Profiled(
        AnalyteReferenceInterval interval, EmpiricalMarginal empirical, string? displayName = null)
        => new(interval, empirical.Median, 0.0, displayName, empirical);

    /// <summary>
    /// A population-normal descriptor from an explicit population center + dispersion (e.g. the NHANES
    /// cohort mean/SD, the committed generation-parameter source). This is the KS-honest population draw:
    /// its center is the population mean, not the reference-interval midpoint.
    /// </summary>
    public static LabSamplingDescriptor Population(
        AnalyteReferenceInterval interval, double center, double sigma, string? displayName = null)
        => new(interval, center, sigma, displayName);

    /// <summary>
    /// A population-normal descriptor centered on the reference-interval midpoint, dispersion a quarter
    /// of the interval width. The fallback when no population moment (NHANES cohort) is available.
    /// </summary>
    public static LabSamplingDescriptor PopulationNormal(AnalyteReferenceInterval interval)
        => new(interval, (interval.Low + interval.High) / 2.0, (interval.High - interval.Low) / 4.0);

    /// <summary>
    /// A condition-conditioned descriptor: a population center moved by a directed shift, so the
    /// conditioned median moves in the clinically-correct direction (b56).
    /// </summary>
    public static LabSamplingDescriptor Conditioned(
        AnalyteReferenceInterval interval, double populationCenter, double shift, double sigma, string? displayName = null)
        => new(interval, populationCenter + shift, sigma, displayName);
}

/// <summary>
/// An empirical marginal distribution as a quantile ladder — ascending (probability, value) points from
/// a customer's real data (P5..P95, optionally extended to the observed min/max). The sampler's
/// inverse-CDF is a piecewise-linear interpolation of this ladder: monotone, so it preserves the
/// Gaussian copula's rank correlation exactly (the same property the lognormal marginal relies on),
/// and it needs no moments — robust quantiles alone (which survive outlier suppression) suffice.
///
/// This is the inverse of <c>RealismMetrics.EmpiricalCdf</c> (Flock): that walks the same ladder
/// value→probability to score a marginal, this walks it probability→value to draw one. They can't share
/// a helper (that lives in Flock; Core must not depend on Flock), so the piecewise-linear kernel is
/// intentionally duplicated — keep the two in sync if the interpolation ever changes.
/// </summary>
public sealed class EmpiricalMarginal
{
    private readonly double[] _probs;   // strictly ascending, in (0,1)
    private readonly double[] _values;  // non-decreasing

    /// <param name="probs">Ascending probabilities in (0,1); same length as <paramref name="values"/> (>= 2).</param>
    /// <param name="values">Non-decreasing quantile values. Validated here — this is a public Core entry point.</param>
    public EmpiricalMarginal(double[] probs, double[] values)
    {
        if (probs.Length < 2)
            throw new ArgumentException("A quantile ladder needs at least two points.", nameof(probs));
        if (probs.Length != values.Length)
            throw new ArgumentException("probs and values must have the same length.", nameof(values));
        for (int i = 1; i < probs.Length; i++)
        {
            if (probs[i] <= probs[i - 1])
                throw new ArgumentException("probs must be strictly ascending.", nameof(probs));
            if (values[i] < values[i - 1])
                throw new ArgumentException("values must be non-decreasing.", nameof(values));
        }

        _probs = probs;
        _values = values;
        Median = InverseCdf(0.5);
    }

    /// <summary>The 50th-percentile value (the ladder's center, for descriptor sanity).</summary>
    public double Median { get; }

    /// <summary>
    /// value = F^-1(u) by piecewise-linear interpolation of the quantile ladder. u outside the ladder's
    /// probability support clamps to the nearest ladder value (flat tails beyond the robust quantiles).
    /// </summary>
    public double InverseCdf(double u)
    {
        int n = _probs.Length;
        if (u <= _probs[0]) return _values[0];
        if (u >= _probs[n - 1]) return _values[n - 1];
        for (int i = 1; i < n; i++)
        {
            if (u <= _probs[i])
            {
                double span = _probs[i] - _probs[i - 1];
                double t = span > 0 ? (u - _probs[i - 1]) / span : 0.0;
                return _values[i - 1] + t * (_values[i] - _values[i - 1]);
            }
        }
        return _values[n - 1];
    }
}
