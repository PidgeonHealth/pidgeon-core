// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Clinical;

/// <summary>
/// The cited lab-lab correlation seed for the B2 Gaussian copula (clinical/lab-correlations.yaml).
/// Stores the SPEARMAN rank correlation each pair reports (what the literature and the S1 gate
/// measure); the consumer converts to the latent Pearson parameter at assemble time.
/// </summary>
public interface ILabCorrelationProvider
{
    /// <summary>
    /// The cited Spearman rank correlation for the (a, b) analyte pair (order-independent), or null
    /// when no pair is cited — an honest "we have no rho", treated as independent (rho = 0) by the copula.
    /// </summary>
    double? SpearmanRho(string loincA, string loincB);

    /// <summary>Every cited pair (for the RI-8 count guard).</summary>
    IReadOnlyCollection<(string A, string B, double SpearmanRho)> Pairs();
}
