// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Capability;

/// <summary>
/// The honest capability tier we can claim for a single (standard, type, version) cell.
///
/// Distinct from <see cref="Conformance.ConformanceLevel"/>, which is the verdict for one
/// concrete message instance. A <see cref="CapabilityLevel"/> is a claim about the engine's
/// proven ability for a whole cell, and it is derived from a live generate→validate→oracle
/// sweep — never asserted by hand. The values are ordered ascending so callers can compare
/// strength (e.g. "this cell must report at least <see cref="SpecValidated"/>").
///
/// The lethal product failure mode is over-claiming: authoritative-looking output that is
/// subtly non-conformant with no signal it is unverified. Every promotion here is gated on
/// real evidence so the signal can only understate, never overstate, reality.
/// </summary>
public enum CapabilityLevel
{
    /// <summary>
    /// A plugin lists the type but it does not actually generate for this cell
    /// (e.g. a message type not yet registered at a given HL7 version).
    /// </summary>
    Unsupported = 0,

    /// <summary>
    /// Output generates, but our own validation finds error-severity issues, or no validator
    /// is wired for the standard. "Generated, validation in progress."
    /// </summary>
    Generated = 1,

    /// <summary>
    /// Output generates AND passes our own compatibility validation with zero error-severity
    /// issues — the engine validates its own output cleanly, but no independent oracle attests it.
    /// </summary>
    SpecValidated = 2,

    /// <summary>
    /// Output generates AND passes a registered independent oracle (a validator we do not
    /// control, e.g. the NCPDP SCRIPT XSD). The strongest, most defensible claim.
    /// </summary>
    IndependentlyValidated = 3
}
