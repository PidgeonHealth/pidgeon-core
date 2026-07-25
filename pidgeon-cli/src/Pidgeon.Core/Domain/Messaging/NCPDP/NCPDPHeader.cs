// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Messaging.NCPDP;

public record NCPDPHeader
{
    public required NCPDPRoutingInfo To { get; init; }
    public required NCPDPRoutingInfo From { get; init; }
    public required string MessageID { get; init; }
    public DateTime SentTime { get; init; } = DateTime.UtcNow;
    public string? RelatesToMessageID { get; init; }
}

public record NCPDPRoutingInfo
{
    public required string Qualifier { get; init; }
    public required string Value { get; init; }
}
