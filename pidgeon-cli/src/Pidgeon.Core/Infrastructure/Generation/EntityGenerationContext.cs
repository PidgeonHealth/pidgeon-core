// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;

namespace Pidgeon.Core.Generation;

/// <summary>
/// The entropy + clock carrier threaded through the per-entity generators. Every drawn value
/// is a pure function of a stable coordinate
/// (<see cref="Key"/>) rather than of draw order. A composing generator derives a child
/// coordinate for each sub-entity via <see cref="Derive"/> (e.g. an encounter derives
/// <c>"patient"</c> and <c>"provider"</c>), which keeps each sub-entity on its own stream and
/// makes nested generation invariant under reordering, caching, and parallelism.
///
/// <para>
/// <see cref="Clock"/> is the seeded wall-clock anchor; generators read it for entity dates
/// instead of <see cref="System.DateTime.Now"/> / <c>Today</c>, so a seeded run is byte-stable.
/// <see cref="Options"/> carries the non-entropy generation configuration (locked values, etc.).
/// The struct is immutable; <see cref="Derive"/> returns a copy with only the key advanced.
/// Mirrors the placement of <see cref="SegmentGenerationContext"/> (the HL7 composer's carrier)
/// so both generation streams keep their entropy carrier in the Infrastructure generation layer.
/// </para>
/// </summary>
public readonly struct EntityGenerationContext
{
    public EntityGenerationContext(GenerationKey key, DateTime clock, GenerationOptions options)
    {
        Key = key;
        Clock = clock;
        Options = options;
    }

    /// <summary>The coordinate this generator draws from. <see cref="GenerationKey.Stream"/> mints its value RNG.</summary>
    public GenerationKey Key { get; }

    /// <summary>The seeded wall-clock anchor for entity dates (fixed under a seed, real time otherwise).</summary>
    public DateTime Clock { get; }

    /// <summary>The non-entropy generation configuration (locked-value sessions, scenario id, etc.).</summary>
    public GenerationOptions Options { get; }

    /// <summary>Derives the child context named <paramref name="label"/> — same clock and options, the key advanced to that sub-coordinate.</summary>
    public EntityGenerationContext Derive(string label) =>
        new(Key.Derive(label), Clock, Options);
}
