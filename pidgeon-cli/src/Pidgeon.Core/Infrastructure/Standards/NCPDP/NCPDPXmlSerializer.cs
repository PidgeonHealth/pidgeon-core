// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Xml.Linq;
using Pidgeon.Core.Domain.Messaging.NCPDP;
using Pidgeon.Core.Infrastructure.Standards.NCPDP.Serialization;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP;

/// <summary>
/// Serializes NCPDP SCRIPT (2017071 / 2023011 / 2023071) domain messages to XML wire format
/// conforming to the authoritative SCRIPT XSD set (transport.xsd and its includes). This class owns
/// the shared envelope — the six required <c>Message</c> version attributes, the release-stamp
/// dispatch keyed by the message's folder-style Version, and the <c>Header</c> with its required
/// <c>SenderSoftware</c> element. The transaction <c>Body</c>, whose shape differs between 2017071 and
/// 2023, is delegated to the matching <see cref="INcpdpBodySerializer"/>; an unrecognized version
/// falls back to the 2017071 body serializer (mirroring the release-stamp fallback).
/// </summary>
public class NCPDPXmlSerializer : INCPDPXmlSerializer
{
    private static readonly XNamespace Ns = "http://www.ncpdp.org/schema/SCRIPT";

    // SCRIPT release stamps the six MessageType version attributes carry, keyed by the domain
    // message's folder-style Version. Each value is the exact `version=` attribute on that release's
    // transport.xsd (2017071→20170715, 2023011→20230115, 2023071→20230713 — July is the 13th, not
    // the 15th). An unrecognized domain Version falls back to 2017071 rather than emitting a stamp
    // the embedded oracle can't resolve.
    private const string DefaultReleaseVersion = "20170715";

    private static readonly IReadOnlyDictionary<string, string> ReleaseStamps =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["2017071"] = "20170715",
            ["2023011"] = "20230115",
            ["2023071"] = "20230713",
        };

    private const string SenderSoftwareDeveloper = "Pidgeon";
    private const string SenderSoftwareProduct = "Post";
    private const string SenderSoftwareVersion = "1.0";

    private readonly IReadOnlyList<INcpdpBodySerializer> _bodySerializers;
    private readonly INcpdpBodySerializer _fallbackBodySerializer;

    /// <summary>
    /// DI constructor. The registered <see cref="INcpdpBodySerializer"/> set selects the body shape
    /// by SCRIPT version; the 2017071 serializer is the fallback for an unrecognized version.
    /// </summary>
    public NCPDPXmlSerializer(IEnumerable<INcpdpBodySerializer> bodySerializers)
    {
        _bodySerializers = bodySerializers.ToList();
        _fallbackBodySerializer =
            _bodySerializers.FirstOrDefault(s => s.CanSerialize("2017071"))
            ?? new Ncpdp2017071BodySerializer();
    }

    /// <summary>
    /// Parameterless constructor wiring the built-in body serializers (2017071 + 2023), for callers
    /// that construct the serializer directly rather than through DI.
    /// </summary>
    public NCPDPXmlSerializer()
        : this(new INcpdpBodySerializer[]
        {
            new Ncpdp2017071BodySerializer(),
            new Ncpdp2023BodySerializer(),
        })
    {
    }

    /// <inheritdoc />
    public string Serialize(NCPDPMessage message)
    {
        var declaration = new XDeclaration("1.0", "UTF-8", null);

        // The domain Version (folder-style, e.g. "2023011") drives the on-wire release stamp; an
        // unrecognized value falls back to the 2017071 release so the document still resolves against
        // an embedded oracle.
        var release = ReleaseStamps.TryGetValue(message.Version, out var stamp)
            ? stamp
            : DefaultReleaseVersion;

        var bodySerializer =
            _bodySerializers.FirstOrDefault(s => s.CanSerialize(message.Version))
            ?? _fallbackBodySerializer;

        var root = El("Message",
            new XAttribute("DatatypesVersion", release),
            new XAttribute("TransportVersion", release),
            new XAttribute("TransactionDomain", "SCRIPT"),
            new XAttribute("TransactionVersion", release),
            new XAttribute("StructuresVersion", release),
            new XAttribute("ECLVersion", release),
            SerializeHeader(message.Header),
            bodySerializer.SerializeBody(message.Body, Ns));

        return declaration.ToString() + Environment.NewLine + root.ToString(SaveOptions.None);
    }

    private XElement El(string name, params object?[] content) => new(Ns + name, content);

    private XElement SerializeHeader(NCPDPHeader header)
    {
        // HeaderType sequence: To, From, MessageID, RelatesToMessageID?, SentTime, Security?,
        // SenderSoftware (required), ...
        var headerEl = El("Header",
            El("To", new XAttribute("Qualifier", header.To.Qualifier), header.To.Value),
            El("From", new XAttribute("Qualifier", header.From.Qualifier), header.From.Value),
            El("MessageID", header.MessageID));

        if (header.RelatesToMessageID != null)
            headerEl.Add(El("RelatesToMessageID", header.RelatesToMessageID));

        headerEl.Add(
            El("SentTime", header.SentTime.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            El("SenderSoftware",
                El("SenderSoftwareDeveloper", SenderSoftwareDeveloper),
                El("SenderSoftwareProduct", SenderSoftwareProduct),
                El("SenderSoftwareVersionRelease", SenderSoftwareVersion)));

        return headerEl;
    }
}
