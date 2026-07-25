// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using Pidgeon.Core.Application.Interfaces.Clinical;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.Clinical.Realism;

/// <summary>
/// The shared, coordinate-addressed lab valuation model consumed by BOTH the HL7 message path and
/// the Flock population path (Realism Program §2 divergence retired). Given the ordered analyte set
/// for a message/patient, a per-analyte <see cref="LabSamplingDescriptor"/>, and a single parent
/// <see cref="GenerationKey"/>, it produces the correlated, condition-conditioned lab vector as a
/// PURE FUNCTION of the coordinate (RI-4): draw-order-, cache-, and parallelism-invariant.
///
/// Design (ADR-0005 §7 + Realism Program §4/§5):
///   1. Sort analytes by LOINC into a canonical order — independent of composition control flow.
///   2. Per-analyte standard normal, addressed by LOINC: z_a = labsKey.Derive(loinc).Stream().NextGaussian().
///      A value's entropy is a pure function of (seed, message, "labs", loinc), not of draw order.
///   3. Correlate via a shared deterministic linear map: x = L*z, L = Cholesky(Sigma). Sigma is the
///      latent Pearson matrix (Spearman -> Pearson converted per-pair); correlation comes from the
///      shared parent (the z's) and the shared covariance (L), never from a sequence.
///   4. Marginal transform: u = Phi(x); value = F^-1(u; center + shift, sigma, shape). Inverse-CDF
///      against a bounded/fitted marginal removes the clamp that distorted the tail (b55 root cause).
///   5. Flag = AbnormalFlagRule(value, interval) (RI-2).
/// </summary>
public class LabValueEngine
{
    private readonly ILabCorrelationProvider _correlations;

    public LabValueEngine(ILabCorrelationProvider correlations)
    {
        _correlations = correlations ?? throw new ArgumentNullException(nameof(correlations));
    }

    /// <summary>
    /// Values the analytes in <paramref name="descriptors"/> as one correlated vector, keyed on
    /// <paramref name="labsKey"/> (the shared parent — derive it once as
    /// <c>context.MessageKey.Derive("labs")</c> / <c>patientKey.Derive("labs")</c>). The result order
    /// matches the input descriptor order; internally the draw is canonically LOINC-sorted so the
    /// output is independent of the order the caller supplied.
    ///
    /// <paramref name="customerCorrelations"/> (ADR-0010 Tier B, B1): a covered pair's Spearman rho
    /// replaces the cited prior in the latent matrix; uncovered pairs keep the prior. Null — the
    /// HL7 message path and every unprofiled run — leaves the draw byte-identical: the z vector is
    /// coordinate-addressed and untouched, so only the Cholesky map can change, and it only changes
    /// when an overlay actually covers a pair of the drawn set.
    /// </summary>
    public IReadOnlyList<LabObservation> Draw(
        GenerationKey labsKey,
        IReadOnlyList<LabSamplingDescriptor> descriptors,
        LabCorrelationOverride? customerCorrelations = null)
    {
        if (descriptors is null) throw new ArgumentNullException(nameof(descriptors));
        if (descriptors.Count == 0) return Array.Empty<LabObservation>();

        // Canonical LOINC order — removes any dependence on the order the caller requested analytes.
        var canonical = descriptors
            .OrderBy(d => d.Interval.Loinc, StringComparer.Ordinal)
            .ToList();
        int k = canonical.Count;

        // Per-analyte independent standard normal, coordinate-addressed by LOINC (jump-anywhere).
        var z = new double[k];
        for (int i = 0; i < k; i++)
            z[i] = labsKey.Derive(canonical[i].Interval.Loinc).Stream().NextGaussian();

        // Correlated standard normals x = L*z (L lower-triangular from Cholesky(Sigma)).
        var l = CholeskyLower(BuildLatentCorrelation(canonical, customerCorrelations));
        var x = new double[k];
        for (int i = 0; i < k; i++)
        {
            double sum = 0.0;
            for (int j = 0; j <= i; j++)
                sum += l[i][j] * z[j];
            x[i] = sum;
        }

        // Marginal transform per analyte, then derive the flag.
        var byLoinc = new Dictionary<string, LabObservation>(StringComparer.Ordinal);
        for (int i = 0; i < k; i++)
        {
            var d = canonical[i];
            var u = NormalDistribution.Phi(x[i]);
            var value = InverseMarginal(u, d);
            var interval = new ReferenceInterval(d.Interval.Low, d.Interval.High, d.Interval.Unit);
            var flag = AbnormalFlagRule.Evaluate(value, interval);
            byLoinc[d.Interval.Loinc] = new LabObservation(
                d.Interval.Loinc, d.Name, value, d.Interval.Unit, interval, flag);
        }

        // Return in the caller's input order.
        return descriptors.Select(d => byLoinc[d.Interval.Loinc]).ToList();
    }

    // Inverse marginal CDF: value = F^-1(u). Normal: center + sigma*Phi^-1(u). Lognormal (right-skew,
    // positive support): method-of-moments so the generated MEAN and variance match (center, sigma) —
    // exp(mu + s*Phi^-1(u)) with s the log-scale sigma and mu = ln(center) - s^2/2. No clamp — the
    // marginal respects its own support, so the tail and rank order are undistorted.
    private static double InverseMarginal(double u, LabSamplingDescriptor d)
    {
        // Tier-2 profiled marginal: inverse-CDF against the customer's empirical quantile ladder.
        // Monotone, so the correlated uniform u keeps its rank — the copula's Spearman rho is preserved.
        if (d.Empirical is { } empirical)
            return empirical.InverseCdf(u);

        var zq = NormalDistribution.PhiInv(u);
        if (d.Interval.Shape == MarginalShape.Lognormal && d.Center > 0.0)
        {
            // Method-of-moments lognormal for a target arithmetic mean = center, SD = sigma:
            //   s^2 = ln(1 + (sigma/center)^2),  mu = ln(center) - s^2/2.
            // The -s^2/2 term is essential: without it median == center, so the generated MEAN
            // overshoots the target by exp(s^2/2) — a bias that grows with variance and pushed the
            // high-dispersion (older) cohorts further from the microdata KS reference.
            var cv = d.Sigma / d.Center;
            var s2 = Math.Log(1.0 + cv * cv);
            var s = Math.Sqrt(s2);
            return Math.Exp(Math.Log(d.Center) - 0.5 * s2 + s * zq);
        }
        return d.Center + d.Sigma * zq;
    }

    // The k x k latent Pearson correlation matrix, in canonical order. Off-diagonals come from the
    // Spearman rho converted to the copula's latent Pearson parameter — customer-first per pair when
    // an overlay covers it (B1), else the cited prior; missing pairs -> 0.
    //
    // PD guard (B1, distinct from the CholeskyLower pivot-floor REPAIR below): the repair exists for
    // the curated literature pairs, which are at worst mildly non-PD. A customer overlay comes from
    // arbitrary SQL aggregates and can be jointly inconsistent — silently floor-repairing that would
    // emit a joint nobody parameterized. So when an overlay contributed at least one cell, the
    // composite is probed with a strict no-floor Cholesky; if it is not positive-definite the WHOLE
    // overlay is rejected and the matrix rebuilds prior-only (graceful: no throw, the draw equals the
    // unprofiled draw). The probe shares the repair's epsilon so the two can never disagree about the
    // same matrix.
    private double[][] BuildLatentCorrelation(
        IReadOnlyList<LabSamplingDescriptor> canonical, LabCorrelationOverride? customer)
    {
        var sigma = AssembleLatent(canonical, customer, out var overlayApplied);
        if (overlayApplied && !IsPositiveDefinite(sigma))
            sigma = AssembleLatent(canonical, null, out _);
        return sigma;
    }

    private double[][] AssembleLatent(
        IReadOnlyList<LabSamplingDescriptor> canonical, LabCorrelationOverride? customer, out bool overlayApplied)
    {
        overlayApplied = false;
        int k = canonical.Count;
        var sigma = new double[k][];
        for (int i = 0; i < k; i++)
        {
            sigma[i] = new double[k];
            sigma[i][i] = 1.0;
        }

        for (int i = 0; i < k; i++)
        {
            for (int j = i + 1; j < k; j++)
            {
                double? rhoS;
                if (customer != null && customer.TryGetRho(
                        canonical[i].Interval.Loinc, canonical[j].Interval.Loinc, out var customerRho))
                {
                    rhoS = customerRho;
                    overlayApplied = true;
                }
                else
                {
                    rhoS = _correlations.SpearmanRho(canonical[i].Interval.Loinc, canonical[j].Interval.Loinc);
                }
                if (rhoS is null) continue;
                var rhoP = SpearmanToPearson(rhoS.Value);
                sigma[i][j] = rhoP;
                sigma[j][i] = rhoP;
            }
        }
        return sigma;
    }

    // Strict positive-definite probe: the shared Cholesky walk in fail-on-pivot mode. One
    // implementation with the repair path means the probe and the repair CANNOT disagree about the
    // same matrix — the boundary invariant is structural, not a matched pair of constants.
    private static bool IsPositiveDefinite(double[][] a)
        => TryCholeskyLower(a, failOnNonPositivePivot: true, out _);

    // Spearman rank rho -> Pearson correlation of the latent normals for a Gaussian copula. Exact:
    // rho_P = 2 * sin(pi * rho_S / 6). Applied at consume time (per lab-correlations.yaml). Because
    // the inverse-CDF marginal is strictly monotone, Spearman rank correlation is invariant under it,
    // so the generated Spearman rho equals the target rho_S (the S1 gate measures what was parameterized).
    // Do NOT "simplify" to rho_P = rho_S — that biases the joint (§5.4).
    private static double SpearmanToPearson(double spearman)
        => 2.0 * Math.Sin(Math.PI * spearman / 6.0);

    // Cholesky lower-triangular factor, with a deterministic positive-definite repair: pairwise-assembled
    // literature correlations need not be jointly PD, so a non-positive pivot is floored to a small
    // epsilon (eigenvalue-clip-in-spirit) rather than throwing. For the k<=~10 analyte sets here the
    // cost is negligible.
    private static double[][] CholeskyLower(double[][] a)
    {
        TryCholeskyLower(a, failOnNonPositivePivot: false, out var l);
        return l;
    }

    // The one Cholesky walk. Repair mode (failOnNonPositivePivot=false) floors a non-positive pivot
    // to eps and always completes; probe mode returns false at the first non-positive pivot (the
    // customer-overlay PD guard). The single eps is the shared boundary for both.
    private static bool TryCholeskyLower(double[][] a, bool failOnNonPositivePivot, out double[][] l)
    {
        int n = a.Length;
        l = new double[n][];
        for (int i = 0; i < n; i++) l[i] = new double[n];

        const double eps = 1e-9;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j <= i; j++)
            {
                double sum = a[i][j];
                for (int m = 0; m < j; m++)
                    sum -= l[i][m] * l[j][m];

                if (i == j)
                {
                    if (sum <= eps && failOnNonPositivePivot) return false;
                    l[i][j] = Math.Sqrt(sum > eps ? sum : eps);   // PD repair: floor the pivot.
                }
                else
                {
                    l[i][j] = sum / l[j][j];
                }
            }
        }
        return true;
    }
}
