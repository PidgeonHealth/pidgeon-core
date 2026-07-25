// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Composes a single field's wire value. This is the field-level value pipeline: whole-field
/// and per-component pins, optionality drop (including withdrawn "W" suppression and
/// cohort-sequence force-population), composite-data-type expansion via the composite-aware
/// resolvers (with the component-importance tiers for stray optional components), and the
/// primitive resolver chain. It owns value selection only; the message composer keeps
/// structural concerns (segment assembly, MSH layout, repetition, group inclusion).
/// </summary>
public interface IHL7FieldComposer
{
    /// <summary>
    /// Produces the wire string for one field. Derives the field's entropy coordinate from
    /// <c>context.Key</c> so a seeded generation is reproducible, applies pin overrides from
    /// <see cref="GenerationOptions"/>, and resolves values through the version-aware providers
    /// carried on <paramref name="context"/>. Returns <see cref="string.Empty"/> when the field is
    /// dropped (optional/withdrawn and unpinned).
    /// </summary>
    Task<string> ComposeFieldAsync(
        SegmentField field,
        SegmentGenerationContext context,
        GenerationOptions options);
}
