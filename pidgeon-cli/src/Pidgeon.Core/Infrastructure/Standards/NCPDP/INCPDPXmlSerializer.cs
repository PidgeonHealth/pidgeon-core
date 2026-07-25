// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Messaging.NCPDP;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP;

/// <summary>
/// Serializes NCPDP SCRIPT 2017071 domain messages to XML wire format.
/// </summary>
public interface INCPDPXmlSerializer
{
    /// <summary>
    /// Serializes an NCPDP message to XML string conforming to SCRIPT 2017071.
    /// </summary>
    /// <param name="message">The NCPDP message to serialize</param>
    /// <returns>UTF-8 encoded XML string</returns>
    string Serialize(NCPDPMessage message);
}
