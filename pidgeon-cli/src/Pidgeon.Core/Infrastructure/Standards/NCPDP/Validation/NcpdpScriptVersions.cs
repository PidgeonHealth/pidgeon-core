// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP.Validation;

/// <summary>
/// The single canonical source of the NCPDP SCRIPT folder-key version strings the engine supports.
/// A version is "supported" precisely when its embedded XSD set loads, so the schema authority owns
/// this list: the conformance oracle (CanValidate), the capability version source, and the data
/// generator's version routing all derive from <see cref="Supported"/> rather than redeclaring the
/// triplet. Adding a release to <see cref="NcpdpScriptVersion"/> (with its embedded XSD bundle)
/// propagates to every consumer, so a new version can never be wired into one path and silently
/// dropped to 2017071 in another.
/// </summary>
internal static class NcpdpScriptVersions
{
    /// <summary>Folder-key string for an oracle version (matches the <c>ncpdp-{key}</c> bundles).</summary>
    public static string ToKey(NcpdpScriptVersion version) => version switch
    {
        NcpdpScriptVersion.V2017071 => "2017071",
        NcpdpScriptVersion.V2023011 => "2023011",
        NcpdpScriptVersion.V2023071 => "2023071",
        _ => "2017071",
    };

    /// <summary>
    /// Every supported SCRIPT folder-key, derived from <see cref="NcpdpScriptVersion"/> so the enum
    /// stays the single declaration point. Ordered by the enum's declaration order.
    /// </summary>
    public static readonly IReadOnlyList<string> Supported = BuildSupported();

    private static IReadOnlyList<string> BuildSupported()
    {
        var values = (NcpdpScriptVersion[])Enum.GetValues(typeof(NcpdpScriptVersion));
        var keys = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
            keys[i] = ToKey(values[i]);
        return Array.AsReadOnly(keys);
    }
}
