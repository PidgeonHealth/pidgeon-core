// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Semantic;

/// <summary>
/// Marketed oral-solid drug strengths (mg) per generic, derived from the FDA NDC directory.
/// The broad drug pool the HL7 RXE generator draws from so synthetic medication orders carry a
/// real marketed strength instead of a placeholder — the same source dose-ranges.yaml is
/// regenerated from, so the generator and the SEM-F01 dose-magnitude check cannot drift. Keyed by
/// exact lowercased generic name. The pool excludes the SEM-F04 marker drugs and SEM-F16
/// teratogens so an incidental out-of-scenario order never trips those presence-keyed checks.
/// </summary>
public interface IMedicationStrengthsLoader
{
    /// <summary>
    /// Every covered generic with its marketed strengths, in a stable sorted order. Empty when the
    /// dataset is unavailable — callers fall back rather than fail.
    /// </summary>
    IReadOnlyList<MedicationStrengthEntry> GetAll();

    /// <summary>The strengths entry for a generic name (matched lowercased), or null when not covered.</summary>
    MedicationStrengthEntry? Get(string drugName);
}

/// <summary>One generic's marketed strengths plus a representative give-code, form, and route for RXE emission.</summary>
public sealed record MedicationStrengthEntry(
    string Drug,
    IReadOnlyList<double> StrengthsMg,
    string Unit,
    string Form,
    string Route,
    string Ndc);
