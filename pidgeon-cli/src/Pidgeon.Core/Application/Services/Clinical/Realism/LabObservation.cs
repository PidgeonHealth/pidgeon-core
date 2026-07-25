// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Services.Clinical.Realism;

/// <summary>
/// A population-normal reference interval for one analyte — the value OBX-7 reports, and the
/// interval the abnormal flag is judged against. It is condition-INDEPENDENT (RI-1): the same
/// analyte reports the same reference interval no matter which condition ordered it. Distinct
/// from the condition-conditioned sampling distribution a value is drawn from.
/// </summary>
public readonly record struct ReferenceInterval(double Low, double High, string Units);

/// <summary>
/// The HL7 abnormal-flag values this engine derives. N (within interval), L (below), H (above).
/// Extends to LL/HH only when an interval carries critical bounds — not modelled here.
/// </summary>
public enum AbnormalFlag { N, L, H }

/// <summary>
/// A coherent lab observation produced by the shared realism model: the sampled numeric value,
/// its units, the population-normal reference interval, and the DERIVED abnormal flag. Stored
/// numeric (not as pre-formatted strings) so the tuple is internally checkable — a test can assert
/// <c>Flag == H ⇒ Value &gt; Interval.High</c> — and reusable by both the HL7 message path and the
/// Flock population path. Formatting (OBX-5 "F1", OBX-7 "Low-High") happens at the emit seam, not here.
/// </summary>
public sealed record LabObservation(
    string Loinc,
    string TestName,
    double Value,
    string Units,
    ReferenceInterval Interval,
    AbnormalFlag Flag);
