// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Generation.Types;

/// <summary>
/// Vendor profiles for EHR-specific formatting patterns and data structures.
/// Basic patterns available in free tier, specific templates in subscription tiers.
/// </summary>
public enum VendorProfile
{
    /// <summary>
    /// Generic healthcare patterns suitable for most systems.
    /// Standard HL7 formatting without vendor-specific customizations.
    /// </summary>
    Generic,

    /// <summary>
    /// Epic EHR patterns (Professional+ tiers with vendor templates).
    /// Epic-specific MRN formats, timestamp patterns, custom segments.
    /// </summary>
    Epic,

    /// <summary>
    /// Cerner EHR patterns (Professional+ tiers with vendor templates).
    /// Cerner-specific identifier formats, field ordering, custom fields.
    /// </summary>
    Cerner,

    /// <summary>
    /// AllScripts EHR patterns (Professional+ tiers with vendor templates).
    /// AllScripts-specific formatting deviations and field variations.
    /// </summary>
    AllScripts,

    /// <summary>
    /// athenahealth EHR patterns (Professional+ tiers with vendor templates).
    /// Athena-specific workflow patterns and data formatting.
    /// </summary>
    Athenahealth,

    /// <summary>
    /// NextGen EHR patterns (Professional+ tiers with vendor templates).
    /// NextGen-specific message structures and field formats.
    /// </summary>
    NextGen,

    /// <summary>
    /// eClinicalWorks patterns (Professional+ tiers with vendor templates).
    /// eCW-specific formatting and workflow patterns.
    /// </summary>
    EClinicalWorks,

    /// <summary>
    /// MEDITECH patterns (Professional+ tiers with vendor templates).
    /// MEDITECH-specific legacy formatting and field structures.
    /// </summary>
    Meditech,

    /// <summary>
    /// Greenway patterns (Professional+ tiers with vendor templates).
    /// Greenway Prime Suite specific formatting patterns.
    /// </summary>
    Greenway,

    /// <summary>
    /// DrChrono patterns (Professional+ tiers with vendor templates).
    /// Cloud-based EHR specific patterns and API formatting.
    /// </summary>
    DrChrono
}

/// <summary>
/// Extensions for working with vendor profiles.
/// </summary>
public static class VendorProfileExtensions
{
    /// <summary>
    /// Gets the display name for a vendor profile.
    /// </summary>
    public static string GetDisplayName(this VendorProfile profile) => profile switch
    {
        VendorProfile.Generic => "Generic Healthcare",
        VendorProfile.Epic => "Epic Systems",
        VendorProfile.Cerner => "Cerner (Oracle Health)",
        VendorProfile.AllScripts => "AllScripts",
        VendorProfile.Athenahealth => "athenahealth",
        VendorProfile.NextGen => "NextGen Healthcare",
        VendorProfile.EClinicalWorks => "eClinicalWorks",
        VendorProfile.Meditech => "MEDITECH",
        VendorProfile.Greenway => "Greenway Health",
        VendorProfile.DrChrono => "DrChrono",
        _ => profile.ToString()
    };

    /// <summary>
    /// Determines if the vendor profile requires subscription tier access.
    /// Currently disabled for beta launch - all vendor profiles are available.
    /// </summary>
    public static bool RequiresSubscription(this VendorProfile profile) => profile switch
    {
        _ => false // All vendor profiles available during beta launch
    };

    /// <summary>
    /// Gets the market share category for prioritization.
    /// </summary>
    public static string GetMarketCategory(this VendorProfile profile) => profile switch
    {
        VendorProfile.Epic or VendorProfile.Cerner => "Tier 1 - Major EHR",
        VendorProfile.AllScripts or VendorProfile.Athenahealth or VendorProfile.NextGen => "Tier 2 - Significant Market Share",
        _ => "Tier 3 - Specialized/Regional"
    };
}