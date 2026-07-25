// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.Common.HL7.Utilities;

/// <summary>
/// Message-intent classification shared by the generator's temporal coherence pass and the
/// validator's timeline check. Order messages (MSH-9.1 ∈ this set) carry requested or scheduled
/// specimen/result times that may legitimately post-date MSH-7 (a draw ordered now to be
/// collected later), so both the generator clamp and the validator's after-message check
/// exclude the requestable OBR times for these types. One owner so the two never desync.
/// Constants and a pure predicate, so a static class is appropriate.
/// </summary>
internal static class Hl7MessageIntent
{
    // MSH-9.1 message codes whose requestable times can legitimately be future-dated.
    private static readonly HashSet<string> OrderMessageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "RDE", "RDO", "ORM", "OMG", "OML", "OMP", "ORP", "RAS", "RGV", "RRE", "RRO",
    };

    /// <summary>
    /// True when the given MSH-9.1 message code (e.g. "RDE") is an order message whose
    /// requested/scheduled times may legitimately be in the future. Null/empty → false.
    /// </summary>
    public static bool IsOrderMessageType(string? messageCode) =>
        messageCode is not null && OrderMessageTypes.Contains(messageCode);
}
