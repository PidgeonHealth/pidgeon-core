// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Pidgeon.Core.Generation;

/// <summary>
/// SplitMix64 value stream — the owned PRNG behind <see cref="GenerationKey.Stream"/>.
/// SplitMix64 is used (rather than <see cref="System.Random"/>) because its algorithm is
/// fixed: <c>System.Random</c>'s sequence is not contracted to be stable across .NET major
/// versions, so committed byte-snapshots could silently drift on a runtime upgrade. This
/// cursor is mutable and minted fresh per <see cref="GenerationKey.Stream"/> call, so a
/// value-copy of the (immutable) key can never fork a shared sequence.
/// </summary>
internal sealed class SplitMix64Rng : IValueRng
{
    private ulong _state;

    public SplitMix64Rng(ulong seed) => _state = seed;

    /// <summary>Advances the stream and returns the next 64 raw bits.</summary>
    public ulong NextBits()
    {
        unchecked
        {
            ulong z = (_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    public double NextDouble()
    {
        // Top 53 bits → a double in [0, 1) with full mantissa precision.
        return (NextBits() >> 11) * (1.0 / 9007199254740992.0);
    }

    public double NextGaussian()
    {
        // Box-Muller: two uniforms → one standard normal. 1.0 - u keeps the log argument in (0,1]
        // (NextDouble can return 0 but never 1), so Math.Log never sees 0. A single normal per call
        // (the second Box-Muller output is discarded) keeps the draw count stable per coordinate.
        var u1 = 1.0 - NextDouble();
        var u2 = 1.0 - NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Upper bound must be positive.");
        unchecked
        {
            return (int)(NextBits() % (ulong)(uint)maxExclusive);
        }
    }

    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive < minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Upper bound must not be below the lower bound.");
        long range = (long)maxExclusive - minInclusive;
        if (range <= 0)
            return minInclusive;
        unchecked
        {
            return minInclusive + (int)(NextBits() % (ulong)range);
        }
    }

    public T Pick<T>(IReadOnlyList<T> items)
    {
        if (items is null)
            throw new ArgumentNullException(nameof(items));
        if (items.Count == 0)
            throw new ArgumentException("Cannot pick from an empty list.", nameof(items));
        return items[Next(items.Count)];
    }
}
