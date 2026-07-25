// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Messaging.NCPDP;

public record NCPDPPrescriber
{
    public required string LastName { get; init; }
    public required string FirstName { get; init; }
    public required string NPI { get; init; }
    public string? DEA { get; init; }
    public NCPDPAddress? Address { get; init; }
    public string? Phone { get; init; }
    public string? Specialty { get; init; }
    public string? ClinicName { get; init; }
}
