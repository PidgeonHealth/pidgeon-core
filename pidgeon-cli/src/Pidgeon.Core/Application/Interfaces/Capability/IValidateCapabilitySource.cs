// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Pidgeon.Core.Domain.Capability;

namespace Pidgeon.Core.Application.Interfaces.Capability;

/// <summary>
/// Supplies the per-artifact VALIDATE-capability rows for one standard we check but do not
/// generate (today, C-CDA). This is the validate axis's analogue of the generation plugin —
/// it owns its own standard literal and the artifact identities, so the standard-agnostic
/// <see cref="ICapabilitySignalingService"/> stays free of any standard string (red line #5).
///
/// Unlike <see cref="ICapabilityEvidenceSource"/>, evaluation is synchronous: a validate
/// capability is derivable from which validators are currently available (schema oracle,
/// conformance oracle) plus the registry of recognized artifact types — no generate→validate
/// round-trip is needed. The level is still derived from LIVE availability, never a hand table,
/// so it can only understate, never overstate, what we can verify.
/// </summary>
public interface IValidateCapabilitySource
{
    /// <summary>Canonical standard id for these rows (e.g. "ccda").</summary>
    string StandardName { get; }

    /// <summary>
    /// One <see cref="ValidateCapabilityCell"/> per recognized artifact type, with the level
    /// derived from the validators that are actually available right now.
    /// </summary>
    IReadOnlyList<ValidateCapabilityCell> Describe();
}
