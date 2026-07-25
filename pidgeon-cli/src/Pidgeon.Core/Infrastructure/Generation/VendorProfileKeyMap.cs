// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation.Types;

namespace Pidgeon.Core.Generation;

/// <summary>
/// Maps the friendly <see cref="VendorProfile"/> enum to a curated interface-profile slash-key
/// (e.g. <c>Epic</c> → <c>epic/adt_outbound</c>) and parses a CLI <c>--vendor</c> string into the
/// enum. The enum and the interface-profile key stay deliberately separate (a mapping, not a merge):
/// merging them previously broke the friendly-name surfaces (GENERATE-DEPTH S2). Pure data — no
/// vendor-specific branching beyond the lookup tables, so new vendors are a table entry, not code.
/// </summary>
public static class VendorProfileKeyMap
{
    // Friendly / aliased --vendor input → enum. Aliases cover the common spellings an integration
    // engineer types (oracle = Cerner/Millennium, ecw = eClinicalWorks, athena = athenahealth).
    private static readonly IReadOnlyDictionary<string, VendorProfile> Aliases =
        new Dictionary<string, VendorProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["generic"] = VendorProfile.Generic,
            ["epic"] = VendorProfile.Epic,
            ["cerner"] = VendorProfile.Cerner,
            ["oracle"] = VendorProfile.Cerner,
            ["oraclehealth"] = VendorProfile.Cerner,
            ["millennium"] = VendorProfile.Cerner,
            ["allscripts"] = VendorProfile.AllScripts,
            ["veradigm"] = VendorProfile.AllScripts,
            ["athena"] = VendorProfile.Athenahealth,
            ["athenahealth"] = VendorProfile.Athenahealth,
            ["nextgen"] = VendorProfile.NextGen,
            ["ecw"] = VendorProfile.EClinicalWorks,
            ["eclinicalworks"] = VendorProfile.EClinicalWorks,
            ["meditech"] = VendorProfile.Meditech,
            ["greenway"] = VendorProfile.Greenway,
            ["drchrono"] = VendorProfile.DrChrono
        };

    // Enum → default interface-profile slash-key. Each vendor below ships an HL7-shaping profile YAML
    // (engine-vendor-dialect S2/S3). NextGen/Greenway/DrChrono are intentionally absent → they resolve
    // to generic, because public HL7 detail is insufficient to shape their output without fabricating
    // quirks (honest capability signaling). eClinicalWorks resolves to its general results profile (the
    // pharmacy-only profiles remain selectable by their own slash-keys).
    private static readonly IReadOnlyDictionary<VendorProfile, string> InterfaceKeys =
        new Dictionary<VendorProfile, string>
        {
            [VendorProfile.Epic] = "epic/adt_outbound",
            [VendorProfile.Cerner] = "cerner/adt_outbound",
            [VendorProfile.Meditech] = "meditech/adt_outbound",
            [VendorProfile.Athenahealth] = "athenahealth/results_outbound",
            [VendorProfile.AllScripts] = "allscripts/adt_outbound",
            [VendorProfile.EClinicalWorks] = "ecw/results_outbound"
        };

    /// <summary>
    /// Parses a CLI <c>--vendor</c> value into a <see cref="VendorProfile"/>. Returns null for a
    /// null/blank/unrecognized value so an unknown vendor degrades to standard output rather than erroring.
    /// </summary>
    public static VendorProfile? ParseVendor(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        return Aliases.TryGetValue(input.Trim(), out var profile) ? profile : null;
    }

    /// <summary>
    /// Resolves a vendor enum to its default interface-profile slash-key, or null when the vendor has
    /// no shipped HL7-shaping profile (Generic and the documented skip-list).
    /// </summary>
    public static string? ToInterfaceProfileKey(VendorProfile? vendor)
        => vendor is { } v && InterfaceKeys.TryGetValue(v, out var key) ? key : null;
}
