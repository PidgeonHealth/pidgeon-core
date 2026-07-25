// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Xml.Linq;
using Pidgeon.Core.Domain.Messaging.NCPDP;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP.Serialization;

/// <summary>
/// SCRIPT XML primitives shared by every release's body serializer: element construction in the
/// SCRIPT namespace plus the building blocks whose shape is identical across 2017071 and 2023
/// (NameType, MandatoryAddressType / AddressType, DateType, CommunicationNumbersType, and the
/// QuantityType value/qualifier/unit triple). Release-specific wrappers (the 2023 Names / GenderAndSex
/// / Product / StatusType composites) live in the release's own serializer, not here.
/// </summary>
internal sealed class NcpdpXmlBuilders
{
    private readonly XNamespace _ns;

    public NcpdpXmlBuilders(XNamespace ns) => _ns = ns;

    /// <summary>Constructs an element in the SCRIPT namespace.</summary>
    public XElement El(string name, params object?[] content) => new(_ns + name, content);

    /// <summary>
    /// NameType: LastName then FirstName, both required (an1..35). The shared leaf both the flat
    /// 2017071 Name and the 2023 Names/NameComposite wrap.
    /// </summary>
    public XElement Name(string lastName, string firstName)
        => El("Name", El("LastName", lastName), El("FirstName", firstName));

    /// <summary>DateType: a single Date child in ISO yyyy-MM-dd form (the SimpleDateType branch).</summary>
    public XElement Date(string elementName, DateTime date)
        => El(elementName, El("Date", date.ToString("yyyy-MM-dd")));

    /// <summary>
    /// CommunicationNumbersType: PrimaryTelephone is required, so when the source carries no usable
    /// phone the caller decides whether to omit the whole element (optional slots) or fall back to
    /// <see cref="DefaultCommunicationNumbers"/> (mandatory slots). Returns null when no digits.
    /// </summary>
    public XElement? CommunicationNumbers(string? phone)
    {
        var digits = NumericPhone(phone);
        return digits == null
            ? null
            : El("CommunicationNumbers", El("PrimaryTelephone", El("Number", digits)));
    }

    /// <summary>A schema-valid CommunicationNumbers for slots the schema requires but the source lacks.</summary>
    public XElement DefaultCommunicationNumbers()
        => El("CommunicationNumbers", El("PrimaryTelephone", El("Number", "0000000000")));

    /// <summary>
    /// QuantityType body: Value, CodeListQualifier (38 = NCPDP prescription quantity code list), and
    /// QuantityUnitOfMeasure (NCICode with a Code child). The unit is schema-required; when the source
    /// did not carry it the element is omitted rather than asserting a wrong unit — honestly
    /// "missing data" instead of fabricated.
    /// </summary>
    public IEnumerable<XElement> Quantity(int value, string? unitCode)
    {
        yield return El("Value", value);
        yield return El("CodeListQualifier", "38");
        if (unitCode != null)
            yield return El("QuantityUnitOfMeasure", El("Code", unitCode));
    }

    /// <summary>
    /// AddressType / MandatoryAddressType: AddressLine1, City, StateProvince, PostalCode, CountryCode
    /// in schema order. Mandatory slots fill schema-valid placeholders for absent parts; optional
    /// slots emit only the parts that are present.
    /// </summary>
    public XElement Address(NCPDPAddress? address, bool mandatory)
    {
        var el = El("Address");
        var line1 = address?.AddressLine1;
        var city = address?.City;
        var state = address?.State;
        var zip = address?.ZipCode;

        if (mandatory)
        {
            el.Add(
                El("AddressLine1", line1 ?? "Unknown"),
                El("City", city ?? "Unknown"),
                El("StateProvince", state ?? "XX"),
                El("PostalCode", zip ?? "00000"),
                El("CountryCode", "US"));
            return el;
        }

        if (line1 != null) el.Add(El("AddressLine1", line1));
        if (city != null) el.Add(El("City", city));
        if (state != null) el.Add(El("StateProvince", state));
        if (zip != null) el.Add(El("PostalCode", zip));

        return el;
    }

    // PhoneType.Number is numeric (n1..10): strip formatting and clamp to the schema's max length.
    private static string? NumericPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var sb = new StringBuilder();
        foreach (var c in phone)
            if (char.IsDigit(c))
                sb.Append(c);

        if (sb.Length == 0)
            return null;

        return sb.Length > 10 ? sb.ToString(0, 10) : sb.ToString();
    }
}
