// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Generation;

/// <summary>
/// A single field value pinned for one generation request, supplied inline rather than
/// loaded from a named lock session. Used by callers (e.g. the Bridge GUI) that want to
/// constrain a value for the duration of a single request without persisting a session.
/// </summary>
public record AdhocLockedValue
{
    /// <summary>
    /// The semantic field path to pin (e.g. "patient.mrn", "provider.id").
    /// </summary>
    public required string FieldPath { get; init; }

    /// <summary>
    /// The value to pin for this field across the generated messages.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// Optional data type hint for the field, mirroring the persisted lock-value contract.
    /// </summary>
    public string? DataType { get; init; }
}
