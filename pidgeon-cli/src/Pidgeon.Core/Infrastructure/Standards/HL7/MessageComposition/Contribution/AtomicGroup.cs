// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// A set of field positions an <see cref="IValueContributor"/> declares travel together: present
/// or absent as a unit. The serializer makes <em>one</em> present/absent decision per group,
/// never per field, so a coherent tuple (e.g. OBX value-type / value / units / result-status)
/// is never half-emitted. Keys are 1-based HL7 field positions within the segment; values are
/// the semantic <see cref="FieldValue"/> for each.
/// </summary>
public sealed record AtomicGroup(IReadOnlyDictionary<int, FieldValue> Values);
