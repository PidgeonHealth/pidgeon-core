// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Xml.Linq;
using Pidgeon.Core.Domain.Messaging.NCPDP;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP.Serialization;

/// <summary>
/// Serializes the NCPDP SCRIPT message <c>Body</c> for one or more SCRIPT releases. SCRIPT 2023
/// re-architected the body (NameComposite wrappers, a Product medication wrapper, a StatusType-based
/// RxFill) relative to 2017071, so each release family owns its own body serializer. The envelope —
/// the six Message version attributes, the Header, and the release-stamp dispatch — is shared by
/// <see cref="NCPDPXmlSerializer"/>, which selects the matching body serializer by version.
/// </summary>
public interface INcpdpBodySerializer
{
    /// <summary>
    /// Whether this serializer owns the given folder-style SCRIPT version (e.g. "2017071",
    /// "2023011", "2023071").
    /// </summary>
    bool CanSerialize(string version);

    /// <summary>
    /// Builds the <c>Body</c> element for the message's single transaction, using the SCRIPT
    /// namespace for every emitted element.
    /// </summary>
    XElement SerializeBody(NCPDPBody body, XNamespace ns);
}
