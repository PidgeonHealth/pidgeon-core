// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Pidgeon.Core.Generation;

/// <summary>
/// A <see cref="System.Random"/> facade over a <see cref="SplitMix64Rng"/> coordinate, returned
/// by <see cref="GenerationKey.AsRandom"/>. It exists so a coordinate-derived stream can flow
/// into APIs still typed as <c>System.Random</c> (e.g. the demographics services) without
/// changing their signatures. Every public draw method used by those callers is overridden so
/// the owned SplitMix64 algorithm — not the base class's version-unstable implementation —
/// drives the values.
/// </summary>
internal sealed class DeterministicRandom : Random
{
    private readonly SplitMix64Rng _rng;

    public DeterministicRandom(ulong seed) => _rng = new SplitMix64Rng(seed);

    protected override double Sample() => _rng.NextDouble();

    public override int Next() => _rng.Next(int.MaxValue);

    public override int Next(int maxValue)
    {
        if (maxValue < 0)
            throw new ArgumentOutOfRangeException(nameof(maxValue), maxValue, "Upper bound must not be negative.");
        // System.Random.Next(0) returns 0 rather than throwing; preserve that for drop-in parity.
        return maxValue == 0 ? 0 : _rng.Next(maxValue);
    }

    public override int Next(int minValue, int maxValue) => _rng.Next(minValue, maxValue);

    public override double NextDouble() => _rng.NextDouble();

    public override void NextBytes(byte[] buffer)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));
        int i = 0;
        while (i < buffer.Length)
        {
            ulong bits = _rng.NextBits();
            for (int b = 0; b < 8 && i < buffer.Length; b++, i++)
            {
                buffer[i] = (byte)bits;
                bits >>= 8;
            }
        }
    }
}
