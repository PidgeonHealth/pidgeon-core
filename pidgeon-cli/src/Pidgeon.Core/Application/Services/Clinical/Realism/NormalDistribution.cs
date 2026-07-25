// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Pidgeon.Core.Application.Services.Clinical.Realism;

/// <summary>
/// Owned standard-normal numerics: the CDF (Phi) and inverse CDF (PhiInv). Owned rather than a
/// library call so the copula's u = Phi(x) transform and the marginal inverse-CDF are byte-stable
/// across machines and .NET versions (the same reason ADR-0005 owns the PRNG). Pure functions.
/// </summary>
public static class NormalDistribution
{
    /// <summary>
    /// Standard-normal CDF via the Abramowitz &amp; Stegun 7.1.26 erf approximation
    /// (|abs error| &lt; 1.5e-7). Phi(x) = 0.5 * (1 + erf(x / sqrt(2))).
    /// </summary>
    public static double Phi(double x)
    {
        // erf via A&amp;S 7.1.26.
        var sign = x < 0 ? -1.0 : 1.0;
        var z = Math.Abs(x) / Math.Sqrt(2.0);
        var t = 1.0 / (1.0 + 0.3275911 * z);
        var y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592)
                    * t * Math.Exp(-z * z);
        return 0.5 * (1.0 + sign * y);
    }

    /// <summary>
    /// Standard-normal inverse CDF (quantile) via Acklam's rational approximation
    /// (relative error &lt; 1.15e-9 across the full open interval). Clamps the open endpoints so a
    /// u of exactly 0 or 1 (which the copula never produces, but a defensive caller might) does not
    /// return +/-infinity.
    /// </summary>
    public static double PhiInv(double p)
    {
        // Acklam coefficients.
        const double a1 = -3.969683028665376e+01, a2 = 2.209460984245205e+02, a3 = -2.759285104469687e+02,
                     a4 = 1.383577518672690e+02, a5 = -3.066479806614716e+01, a6 = 2.506628277459239e+00;
        const double b1 = -5.447609879822406e+01, b2 = 1.615858368580409e+02, b3 = -1.556989798598866e+02,
                     b4 = 6.680131188771972e+01, b5 = -1.328068155288572e+01;
        const double c1 = -7.784894002430293e-03, c2 = -3.223964580411365e-01, c3 = -2.400758277161838e+00,
                     c4 = -2.549732539343734e+00, c5 = 4.374664141464968e+00, c6 = 2.938163982698783e+00;
        const double d1 = 7.784695709041462e-03, d2 = 3.224671290700398e-01, d3 = 2.445134137142996e+00,
                     d4 = 3.754408661907416e+00;
        const double pLow = 0.02425, pHigh = 1.0 - pLow;

        // Never return an infinite quantile for a boundary u.
        if (p <= 0.0) p = 1e-12;
        if (p >= 1.0) p = 1.0 - 1e-12;

        double q, r;
        if (p < pLow)
        {
            q = Math.Sqrt(-2.0 * Math.Log(p));
            return (((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) /
                   ((((d1 * q + d2) * q + d3) * q + d4) * q + 1.0);
        }
        if (p <= pHigh)
        {
            q = p - 0.5;
            r = q * q;
            return (((((a1 * r + a2) * r + a3) * r + a4) * r + a5) * r + a6) * q /
                   (((((b1 * r + b2) * r + b3) * r + b4) * r + b5) * r + 1.0);
        }
        q = Math.Sqrt(-2.0 * Math.Log(1.0 - p));
        return -(((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) /
                ((((d1 * q + d2) * q + d3) * q + d4) * q + 1.0);
    }
}
