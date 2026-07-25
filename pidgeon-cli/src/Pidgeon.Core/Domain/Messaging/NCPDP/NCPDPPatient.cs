// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Messaging.NCPDP;

public record NCPDPPatient
{
    public required string LastName { get; init; }
    public required string FirstName { get; init; }
    public DateTime DateOfBirth { get; init; }
    public required string Gender { get; init; }
    public NCPDPAddress? Address { get; init; }
    public string? Phone { get; init; }
    public List<string>? Allergies { get; init; }
}
