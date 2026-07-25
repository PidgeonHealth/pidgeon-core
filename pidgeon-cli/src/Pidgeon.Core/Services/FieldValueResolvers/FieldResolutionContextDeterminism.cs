// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Gives field-value resolvers access to the per-message deterministic random number
/// generator and wall-clock carried on the active <see cref="SegmentGenerationContext"/>.
/// Resolvers call <see cref="Rng"/> / <see cref="Clock"/> instead of holding their own
/// <c>new Random()</c> / reading <see cref="DateTime.Now"/>, so a seeded generation run
/// produces byte-identical output.
///
/// When a resolver is exercised outside the composer flow (no generation context wired),
/// these fall back to <see cref="Random.Shared"/> and the real clock, preserving the
/// previous non-deterministic behaviour.
/// </summary>
public static class FieldResolutionContextDeterminism
{
    /// <summary>The shared per-message RNG, or <see cref="Random.Shared"/> when unset.</summary>
    public static Random Rng(this FieldResolutionContext context)
        => context.GenerationContext?.Rng ?? Random.Shared;

    /// <summary>The shared per-message clock, or the real current time when unset.</summary>
    public static DateTime Clock(this FieldResolutionContext context)
        => context.GenerationContext?.Clock ?? DateTime.Now;

    /// <summary>
    /// The coordinate-addressed RNG for the field currently being resolved, a pure function of the
    /// (root seed, segment, set-id, field/component) path, so the value is invariant under resolver
    /// reordering, caching, and parallelism. Falls back to the per-message <see cref="Rng"/>
    /// (and thence <see cref="Random.Shared"/>) for contexts built outside the composer flow.
    /// </summary>
    public static Random FieldRng(this FieldResolutionContext context)
        => context.FieldKey?.AsRandom() ?? context.Rng();
}
