// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// The semantic content of one segment occurrence, contributed by an <see cref="IValueContributor"/>.
/// It carries <em>values</em>, not a wire string: one or more <see cref="AtomicGroup"/>s of
/// (field-position to <see cref="FieldValue"/>). The serializer renders it against the version's
/// segment schema, so version-correct shape (CE/CWE width, withdrawn-field suppression, field
/// count) is decided by the serializer rather than baked into the contributor.
/// </summary>
public sealed record CoherentValueSet(IReadOnlyList<AtomicGroup> Groups)
{
    /// <summary>
    /// A value set whose populated fields are one coherent unit (the common case for the segment
    /// builders this replaces — a fully-populated instance anchored by a required set-id, so it is
    /// always emitted as a whole).
    /// </summary>
    public static CoherentValueSet SingleGroup(IReadOnlyDictionary<int, FieldValue> values)
        => new(new[] { new AtomicGroup(values) });
}
