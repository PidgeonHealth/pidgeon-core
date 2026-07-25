// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical.Entities;

namespace Pidgeon.Core.Domain.Clinical;

/// <summary>
/// The population prevalence of administrative sex used when a patient's sex is a free demographic
/// draw (no scenario has pinned it). Weights are relative and normalized at sample time, so a
/// caller can pass whole percentages, fractions, or raw counts.
///
/// This exists because sampling PID-8 uniformly from HL7 table 0001 is wrong: the table carries
/// A/F/M/N/O/U (four to six codes by version), so a uniform draw makes roughly a third to a half of
/// unconstrained patients Unknown/Other — a distribution a clinical reviewer reads as unrealistic.
/// A real feed is Female/Male-dominant with Unknown rare. <see cref="Realistic"/> is that default;
/// callers who want a different mix (an all-female cohort, a higher unknown rate for a stress corpus)
/// override it.
/// </summary>
public sealed record SexPrevalence
{
    /// <summary>Relative weight of <see cref="Gender.Female"/>.</summary>
    public double Female { get; init; }

    /// <summary>Relative weight of <see cref="Gender.Male"/>.</summary>
    public double Male { get; init; }

    /// <summary>Relative weight of <see cref="Gender.Unknown"/> — kept small in the realistic default.</summary>
    public double Unknown { get; init; }

    /// <summary>
    /// The default free-draw distribution: near-even Female/Male with Unknown at ~1%. Chosen to keep
    /// generated corpora Female/Male-dominant while still exercising the rare-unknown code so a
    /// downstream consumer that must handle PID-8 = U still sees it.
    /// </summary>
    public static SexPrevalence Realistic { get; } = new()
    {
        Female = 0.495,
        Male = 0.495,
        Unknown = 0.01
    };

    /// <summary>The sum of the weights; the normalization denominator for a draw.</summary>
    public double Total => Female + Male + Unknown;
}
