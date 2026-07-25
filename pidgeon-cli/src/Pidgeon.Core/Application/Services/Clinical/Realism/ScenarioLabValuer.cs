// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Pidgeon.Core.Application.Interfaces.Clinical;
using Pidgeon.Core.Domain.Clinical;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.Clinical.Realism;

/// <summary>
/// One ordered analyte for a message: which LOINC, its display name, and the scenario's
/// condition-specific band (the abnormal expected range). Valuation turns the band into a directed
/// shift over the analyte's population-normal interval — the band is never reported as the reference
/// range (b40's fix).
/// </summary>
public readonly record struct LabOrder(string Loinc, string Name, ResultRange Band);

/// <summary>
/// Turns the message's selected lab orders into valued <see cref="LabTestResult"/>s via the shared
/// <see cref="LabValueEngine"/> (Realism Program S2). This is the message path's half of the §2
/// divergence retirement: valuation lives in a pure Core service taking a <see cref="GenerationKey"/>,
/// NOT in the coordinator's branchy per-path logic (ADR-0001 §7 landing seam).
///
/// Per analyte: OBX-7 reports the population-normal reference interval (RI-1); the value is drawn from
/// a condition-conditioned marginal whose directed shift is the delta between the scenario's abnormal
/// band and the normal interval (so "the hypothyroid patient's TSH is high" is a shift over 0.4-4.0,
/// never the abnormal band printed as the reference); the flag is derived by interval membership (RI-2).
/// An analyte with no resolvable interval keeps value+units but omits OBX-7/8 (honest whole-or-absent, RI-3).
/// </summary>
public class ScenarioLabValuer
{
    private readonly IReferenceIntervalProvider _intervalProvider;
    private readonly LabValueEngine _engine;

    public ScenarioLabValuer(IReferenceIntervalProvider intervalProvider, LabValueEngine engine)
    {
        _intervalProvider = intervalProvider ?? throw new ArgumentNullException(nameof(intervalProvider));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// Values every ordered analyte as one correlated, condition-conditioned vector keyed on
    /// <paramref name="labsKey"/> (RI-4: draw-order-, cache-, and parallelism-invariant).
    /// </summary>
    public List<LabTestResult> Value(IReadOnlyList<LabOrder> orders, GenerationKey labsKey)
    {
        var results = new List<LabTestResult>();
        if (orders is null || orders.Count == 0)
            return results;

        var descriptors = new List<LabSamplingDescriptor>();
        var uncovered = new List<LabOrder>();

        foreach (var order in orders)
        {
            var interval = _intervalProvider.Get(order.Loinc);
            var reportedUnit = order.Band.Units?.Trim() ?? string.Empty;
            // The interval and the reported unit must agree; otherwise the value would be judged on the
            // wrong scale, so fall back to value-only (the honesty rule the prior loader enforced).
            if (interval != null
                && (reportedUnit.Length == 0 || string.Equals(reportedUnit, interval.Unit, StringComparison.Ordinal)))
            {
                descriptors.Add(BuildDescriptor(interval, order.Band, order.Name));
            }
            else
            {
                uncovered.Add(order);
            }
        }

        if (descriptors.Count > 0)
        {
            foreach (var obs in _engine.Draw(labsKey, descriptors))
            {
                // The flag is re-judged at emission precision (RI-2 applied to the rendered numbers,
                // b83): OBX-5/OBX-7 go to the wire at F1, so the raw-double flag can contradict the
                // message as written when a value rounds onto a bound (raw 1.32 vs high 1.30 is H on
                // doubles; the wire reads "1.3 vs 0.6-1.3", Normal).
                results.Add(new LabTestResult
                {
                    LoincCode = obs.Loinc,
                    TestName = obs.TestName,
                    Value = obs.Value.ToString("F1", CultureInfo.InvariantCulture),
                    Units = obs.Units,
                    ReferenceRange = $"{obs.Interval.Low.ToString("F1", CultureInfo.InvariantCulture)}-{obs.Interval.High.ToString("F1", CultureInfo.InvariantCulture)}",
                    AbnormalFlag = AbnormalFlagRule.ToHl7(AbnormalFlagRule.EvaluateRendered(obs.Value, obs.Interval))
                });
            }
        }

        // Uncovered analytes: a value near the scenario band, no fabricated range/flag. Keyed off the
        // same labs coordinate per LOINC so the draw is still coordinate-addressed (RI-4).
        foreach (var order in uncovered)
        {
            var band = order.Band;
            var rng = labsKey.Derive(order.Loinc).Stream();
            var value = Math.Round(rng.NextDouble() * (band.MaxValue - band.MinValue) + band.MinValue, 1);
            results.Add(new LabTestResult
            {
                LoincCode = order.Loinc,
                TestName = order.Name,
                Value = value.ToString("F1", CultureInfo.InvariantCulture),
                Units = band.Units,
                ReferenceRange = string.Empty,
                AbnormalFlag = string.Empty
            });
        }

        return results;
    }

    // The sampling center is the scenario band's center — an abnormal-band scenario draws high/low
    // WITHOUT printing the band as the reference range (OBX-7 stays the population-normal interval, RI-1).
    // Sigma is a quarter of the band width (the dispersion the scenario expects).
    private static LabSamplingDescriptor BuildDescriptor(AnalyteReferenceInterval interval, ResultRange band, string displayName)
    {
        var bandCenter = (band.MinValue + band.MaxValue) / 2.0;
        var sigma = Math.Abs(band.MaxValue - band.MinValue) / 4.0;
        if (sigma <= 0)
            sigma = Math.Abs(interval.High - interval.Low) / 4.0;
        return LabSamplingDescriptor.Population(interval, bandCenter, sigma, displayName);
    }
}
