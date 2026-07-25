// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7;

/// <summary>
/// Maps an HL7 segment code to the on-disk file stem for its embedded JSON resource, accounting
/// for Windows-reserved device names. A file literally named <c>con.json</c> cannot exist on
/// Windows, so the HL7 Consent segment (CON) ships as <c>con_.json</c>. Any lookup that builds a
/// resource name from a segment code must apply the same trailing-<c>_</c> mapping or it silently
/// misses the file — CON is referenced by every v2.7/v2.8 MDM trigger event. The full reserved
/// set (CON, PRN, AUX, NUL, COM1-9, LPT1-9) is covered defensively so a future segment whose code
/// collides with a reserved name resolves the same way. Pure functions only.
/// </summary>
internal static class HL7ReservedResourceName
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    /// <summary>
    /// The lowercase file stem for a segment code's embedded JSON resource. Windows-reserved
    /// names get a trailing <c>_</c> (matching how they were stored at build time); every other
    /// code maps to its lowercase form unchanged.
    /// </summary>
    public static string ResourceStem(string segmentCode)
    {
        var lower = segmentCode.ToLowerInvariant();
        return Reserved.Contains(lower) ? lower + "_" : lower;
    }
}
