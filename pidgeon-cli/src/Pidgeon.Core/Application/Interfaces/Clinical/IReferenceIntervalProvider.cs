// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Services.Clinical.Realism;

namespace Pidgeon.Core.Application.Interfaces.Clinical;

/// <summary>
/// One analyte's population-normal reference interval plus its intrinsic marginal shape.
/// Condition-independent (RI-1): the reference interval OBX-7 reports is the same regardless of
/// which condition ordered the analyte. <see cref="Shape"/> is the analyte's intrinsic
/// distributional shape the condition-conditioned sampler draws from (right-skewed analytes are
/// lognormal); the condition-directed shift and dispersion live in the condition->analyte data,
/// not here.
/// </summary>
public sealed record AnalyteReferenceInterval(
    string Loinc,
    string Analyte,
    string Unit,
    double Low,
    double High,
    MarginalShape Shape);

/// <summary>The intrinsic marginal shape of an analyte's sampling distribution.</summary>
public enum MarginalShape { Normal, Lognormal }

/// <summary>
/// The single source of truth for population-normal reference intervals (Realism Program §2.1),
/// keyed by LOINC and consumed by BOTH the HL7 message path and the Flock population path. Reads
/// the cited clinical/reference-intervals.yaml. A distinct artifact from the SEM-F03
/// impossible-value bounds (<c>IReferenceRangeLoader</c>): this is the normal interval, not the
/// survivability envelope.
/// </summary>
public interface IReferenceIntervalProvider
{
    /// <summary>The reference interval for a LOINC code, or null when the analyte is not covered.</summary>
    AnalyteReferenceInterval? Get(string loincCode);

    /// <summary>Every covered LOINC code (for the RI-8 count guard and coverage tests).</summary>
    IReadOnlyCollection<string> CoveredLoincCodes();
}
