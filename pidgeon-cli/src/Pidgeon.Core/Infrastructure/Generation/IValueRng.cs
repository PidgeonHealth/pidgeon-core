// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Pidgeon.Core.Generation;

/// <summary>
/// A deterministic value stream drawn from a single <see cref="GenerationKey"/> coordinate.
/// The operation surface is exactly what the generation engine draws — bounded ints, a unit
/// double, and list selection — so a coordinate's leaf produces the same values on every
/// machine and .NET version (the algorithm is owned, not <see cref="System.Random"/>'s).
/// </summary>
public interface IValueRng
{
    /// <summary>Uniform int in [0, <paramref name="maxExclusive"/>). Throws if the bound is not positive.</summary>
    int Next(int maxExclusive);

    /// <summary>Uniform int in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>); returns the bound when the range is empty (System.Random semantics).</summary>
    int Next(int minInclusive, int maxExclusive);

    /// <summary>Uniform double in [0.0, 1.0).</summary>
    double NextDouble();

    /// <summary>
    /// A standard normal draw (mean 0, variance 1), via Box-Muller over this stream's uniform
    /// doubles. Owned (not <see cref="System.Random"/>) so a coordinate's Gaussian leaf is
    /// byte-reproducible across machines and .NET versions — the entropy source the lab copula
    /// (ADR-0005 §7) needs for a shaped, correlated marginal.
    /// </summary>
    double NextGaussian();

    /// <summary>Selects one element uniformly. Throws if the list is empty.</summary>
    T Pick<T>(IReadOnlyList<T> items);
}
