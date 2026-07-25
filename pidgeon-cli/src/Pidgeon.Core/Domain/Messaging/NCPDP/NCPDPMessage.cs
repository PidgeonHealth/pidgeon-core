// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Messaging.NCPDP;

/// <summary>
/// Root NCPDP SCRIPT 2017071 message containing header and body.
/// </summary>
public record NCPDPMessage
{
    public required NCPDPHeader Header { get; init; }
    public required NCPDPBody Body { get; init; }
    public string Version { get; init; } = "2017071";
}

/// <summary>
/// NCPDP message body containing the transaction-specific content.
/// Exactly one of NewRx, RxFill, or CancelRx should be set.
/// </summary>
public record NCPDPBody
{
    public NCPDPNewRx? NewRx { get; init; }
    public NCPDPRxFill? RxFill { get; init; }
    public NCPDPCancelRx? CancelRx { get; init; }
}
