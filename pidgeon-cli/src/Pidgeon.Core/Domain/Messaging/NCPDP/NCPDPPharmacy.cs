// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Messaging.NCPDP;

public record NCPDPPharmacy
{
    public required string NCPDPID { get; init; }

    /// <summary>
    /// National Provider Identifier for the pharmacy. Emitted in the Identification/NPI slot;
    /// distinct from the NCPDP provider ID, which carries its own NCPDPID element.
    /// </summary>
    public string? NPI { get; init; }

    public required string StoreName { get; init; }
    public NCPDPAddress? Address { get; init; }
    public string? Phone { get; init; }
    public string? PharmacistLastName { get; init; }
    public string? PharmacistFirstName { get; init; }
}
