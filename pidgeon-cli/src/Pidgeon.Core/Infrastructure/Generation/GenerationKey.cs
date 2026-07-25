// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Pidgeon.Core.Generation;

/// <summary>
/// A coordinate-addressed entropy key. Every generated value is a pure function of
/// a root seed and a stable domain path, for example
/// <c>Root(seed).Derive("patient").Derive(3).Derive("name")</c> — so output depends on the
/// coordinate, never on RNG draw order or count. That makes generation invariant under caching,
/// resolver reordering, and parallelism (the fragility the prior positional scheme had).
///
/// The struct is immutable and carries only the derived 64-bit state; the mutable value cursor
/// lives in the stream object minted by <see cref="Stream"/> / <see cref="AsRandom"/>, so copying
/// a key never forks a shared sequence. Coherence between related values is expressed by deriving
/// them from a shared parent key, not by relying on a shared draw order.
/// </summary>
public readonly struct GenerationKey : IEquatable<GenerationKey>
{
    // Domain separators keep Derive(string) and Derive(int) in disjoint sub-streams, so an integer
    // index can never alias a string label (e.g. Derive("7") and Derive(7) stay independent).
    private const ulong DomainSeed = 0;
    private const ulong DomainLabel = 1;
    private const ulong DomainIndex = 2;

    // Odd 64-bit multipliers (golden-ratio + a second irrational constant) spread the input and
    // domain across the state before the finalizer, generalizing Pidgeon.Bench's DeterministicSeed.
    private const ulong InputMult = 0x9E3779B97F4A7C15UL;
    private const ulong DomainMult = 0xD1B54A32D192ED03UL;

    private readonly ulong _state;

    private GenerationKey(ulong state) => _state = state;

    /// <summary>The root key for a run. All coordinates descend from this via <see cref="Derive(string)"/> / <see cref="Derive(int)"/>.</summary>
    public static GenerationKey Root(long seed) => new(Mix(Finalize((ulong)seed), 0UL, DomainSeed));

    /// <summary>Derives the child coordinate named <paramref name="label"/> (e.g. a segment or field path component).</summary>
    public GenerationKey Derive(string label)
    {
        if (label is null)
            throw new ArgumentNullException(nameof(label));
        return new GenerationKey(Mix(_state, Fnv1a64(label), DomainLabel));
    }

    /// <summary>Derives the child coordinate at <paramref name="index"/> (e.g. the i-th entity in a cohort or the i-th repetition).</summary>
    public GenerationKey Derive(int index) => new(Mix(_state, unchecked((uint)index), DomainIndex));

    /// <summary>Mints a fresh value stream for this coordinate. Two streams from the same key produce the same sequence.</summary>
    public IValueRng Stream() => new SplitMix64Rng(_state);

    /// <summary>
    /// Mints a <see cref="System.Random"/>-typed stream for this coordinate, for APIs still typed
    /// as <c>System.Random</c>. Backed by the same owned SplitMix64 algorithm as <see cref="Stream"/>.
    /// </summary>
    public Random AsRandom() => new DeterministicRandom(_state);

    // Folds (input, domain) into the parent state, then runs the SplitMix64 finalizer so each
    // coordinate's state is well distributed and reproducible across machines and .NET versions.
    private static ulong Mix(ulong state, ulong input, ulong domain)
    {
        unchecked
        {
            return Finalize(state + InputMult * (input + 1UL) + DomainMult * (domain + 1UL));
        }
    }

    private static ulong Finalize(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    // FNV-1a (64-bit). A fixed hash — not string.GetHashCode(), whose per-process randomization
    // would break byte-reproducibility. Labels are ASCII, so the char-wise fold is stable.
    private static ulong Fnv1a64(string s)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (var c in s)
                hash = (hash ^ c) * 1099511628211UL;
            return hash;
        }
    }

    public bool Equals(GenerationKey other) => _state == other._state;

    public override bool Equals(object? obj) => obj is GenerationKey other && Equals(other);

    public override int GetHashCode() => _state.GetHashCode();
}
