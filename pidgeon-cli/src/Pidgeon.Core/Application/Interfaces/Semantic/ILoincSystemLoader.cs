// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Semantic;

/// <summary>
/// Resolves a LOINC code to the LOINC SYSTEM-axis short name(s) the analyte is
/// reported in (the free-tier loinc_common.json corpus already in-tree). Pairs with
/// <see cref="ISpecimenLoincLoader"/> to power SEM-F08 (specimen ↔ test mismatch): the
/// analyte's own system (e.g. "Ser/Plas" for a serum chemistry) is compared against the
/// system the order's specimen code resolves to, so a serum-only analyte reported on a
/// urine specimen is detectable. A LOINC System axis value can name more than one
/// acceptable specimen ("Ser/Plas"), so the loader returns the split token set.
/// </summary>
public interface ILoincSystemLoader
{
    /// <summary>
    /// The LOINC SYSTEM-axis tokens for a LOINC code (e.g. "Ser/Plas" → {"Ser","Plas"}),
    /// or an empty set when the code is unknown or carries no system. Tokens are the
    /// loinc_common System value split on '/', so any one is an acceptable specimen system.
    /// </summary>
    IReadOnlyCollection<string> GetSystemTokens(string loincCode);

    /// <summary>Total count of LOINC codes carrying a system axis (0 if the dataset is unavailable).</summary>
    Result<int> GetLoadedCodeCount();
}
