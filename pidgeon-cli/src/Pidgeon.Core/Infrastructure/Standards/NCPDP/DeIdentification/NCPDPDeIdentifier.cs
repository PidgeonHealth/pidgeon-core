// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using System.Xml.Linq;
using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP.DeIdentification;

/// <summary>
/// NCPDP SCRIPT 2017071 de-identification implementation.
/// Walks XML documents and replaces PHI in patient, prescriber, and pharmacy elements
/// using shared DeIdentificationContext for cross-format consistency.
/// </summary>
public class NCPDPDeIdentifier
{
    private static readonly XNamespace NcpdpNs = "http://www.ncpdp.org/schema/SCRIPT";
    private readonly NCPDPSafeHarborFieldMapper _fieldMapper;

    public NCPDPDeIdentifier()
    {
        _fieldMapper = new NCPDPSafeHarborFieldMapper();
    }

    /// <summary>
    /// De-identifies an NCPDP SCRIPT XML message.
    /// </summary>
    public Result<string> DeIdentifyMessage(string ncpdpXml, DeIdentificationContext context)
    {
        if (string.IsNullOrWhiteSpace(ncpdpXml))
            return Result<string>.Failure("NCPDP XML content cannot be null or empty");

        try
        {
            var doc = XDocument.Parse(ncpdpXml);
            var root = doc.Root;
            if (root == null)
                return Result<string>.Failure("NCPDP XML has no root element");

            // Determine namespace: use document namespace or try without
            var ns = root.Name.Namespace;

            // Process all transaction types in Body
            var body = FindElement(root, "Body", ns);
            if (body != null)
            {
                foreach (var transaction in body.Elements())
                {
                    DeIdentifyTransaction(transaction, ns, context);
                }
            }

            // De-identify header identifiers
            var header = FindElement(root, "Header", ns);
            if (header != null)
            {
                DeIdentifyHeader(header, ns, context);
            }

            return Result<string>.Success(doc.Declaration != null
                ? $"{doc.Declaration}\n{doc.Root}"
                : doc.ToString());
        }
        catch (System.Xml.XmlException ex)
        {
            return Result<string>.Failure($"Invalid NCPDP XML: {ex.Message}");
        }
    }

    private void DeIdentifyTransaction(XElement transaction, XNamespace ns, DeIdentificationContext context)
    {
        // De-identify Patient element
        var patient = FindElement(transaction, "Patient", ns);
        if (patient != null)
        {
            DeIdentifyPatient(patient, ns, context);
        }

        // De-identify Prescriber element
        var prescriber = FindElement(transaction, "Prescriber", ns);
        if (prescriber != null)
        {
            DeIdentifyPrescriber(prescriber, ns, context);
        }

        // De-identify Pharmacy element
        var pharmacy = FindElement(transaction, "Pharmacy", ns);
        if (pharmacy != null)
        {
            DeIdentifyPharmacy(pharmacy, ns, context);
        }

        // Medication data is NOT de-identified — it's clinical, not PHI
    }

    private void DeIdentifyPatient(XElement patient, XNamespace ns, DeIdentificationContext context)
    {
        // Name
        var name = FindElement(patient, "Name", ns);
        if (name != null)
        {
            var lastName = FindElement(name, "LastName", ns);
            var firstName = FindElement(name, "FirstName", ns);

            var originalLast = lastName?.Value ?? "";
            var originalFirst = firstName?.Value ?? "";
            var originalFull = $"{originalLast}^{originalFirst}".Trim('^');

            if (!string.IsNullOrEmpty(originalFull))
            {
                var syntheticName = context.GetOrCreateSyntheticId(originalFull, IdentifierType.PatientName);
                var (family, given) = SplitSyntheticName(syntheticName);

                if (lastName != null) lastName.Value = family;
                if (firstName != null) firstName.Value = given;
            }
        }

        // Date of birth
        var dob = FindElement(patient, "DateOfBirth", ns);
        if (dob != null && !string.IsNullOrEmpty(dob.Value))
        {
            ShiftDateElement(dob, context);
        }

        // Phone
        var phone = FindElement(patient, "Phone", ns);
        if (phone != null && !string.IsNullOrEmpty(phone.Value))
        {
            phone.Value = context.GetOrCreateSyntheticId(phone.Value, IdentifierType.PhoneNumber);
        }

        // Address
        var address = FindElement(patient, "Address", ns);
        if (address != null)
        {
            DeIdentifyAddress(address, ns, context);
        }

        // Identification (SSN, Cardholder ID)
        var identification = FindElement(patient, "Identification", ns);
        if (identification != null)
        {
            var ssn = FindElement(identification, "SocialSecurity", ns);
            if (ssn != null && !string.IsNullOrEmpty(ssn.Value))
            {
                ssn.Value = context.GetOrCreateSyntheticId(ssn.Value, IdentifierType.SocialSecurityNumber);
            }

            var cardholderId = FindElement(identification, "CardholderID", ns);
            if (cardholderId != null && !string.IsNullOrEmpty(cardholderId.Value))
            {
                cardholderId.Value = context.GetOrCreateSyntheticId(cardholderId.Value, IdentifierType.InsuranceId);
            }
        }
    }

    private void DeIdentifyPrescriber(XElement prescriber, XNamespace ns, DeIdentificationContext context)
    {
        // Name
        var name = FindElement(prescriber, "Name", ns);
        if (name != null)
        {
            var lastName = FindElement(name, "LastName", ns);
            var firstName = FindElement(name, "FirstName", ns);

            var originalLast = lastName?.Value ?? "";
            var originalFirst = firstName?.Value ?? "";
            var originalFull = $"{originalLast}^{originalFirst}".Trim('^');

            if (!string.IsNullOrEmpty(originalFull))
            {
                var syntheticName = context.GetOrCreateSyntheticId(originalFull, IdentifierType.ProviderName);
                var (family, given) = SplitSyntheticName(syntheticName);

                if (lastName != null) lastName.Value = family;
                if (firstName != null) firstName.Value = given;
            }
        }

        // NPI — deterministic hash preserving 10-digit format
        var npi = FindElement(prescriber, "NPI", ns);
        if (npi != null && !string.IsNullOrEmpty(npi.Value))
        {
            npi.Value = context.GetOrCreateSyntheticId(npi.Value, IdentifierType.Other);
        }

        // DEA — deterministic hash preserving format (2 letters + 7 digits)
        var dea = FindElement(prescriber, "DEA", ns);
        if (dea != null && !string.IsNullOrEmpty(dea.Value))
        {
            dea.Value = context.GetOrCreateSyntheticId(dea.Value, IdentifierType.LicenseNumber);
        }

        // Phone
        var phone = FindElement(prescriber, "Phone", ns);
        if (phone != null && !string.IsNullOrEmpty(phone.Value))
        {
            phone.Value = context.GetOrCreateSyntheticId(phone.Value, IdentifierType.PhoneNumber);
        }

        // Address
        var address = FindElement(prescriber, "Address", ns);
        if (address != null)
        {
            DeIdentifyAddress(address, ns, context);
        }
    }

    private void DeIdentifyPharmacy(XElement pharmacy, XNamespace ns, DeIdentificationContext context)
    {
        // NCPDP ID
        var ncpdpId = FindElement(pharmacy, "NCPDPID", ns);
        if (ncpdpId != null && !string.IsNullOrEmpty(ncpdpId.Value))
        {
            ncpdpId.Value = context.GetOrCreateSyntheticId(ncpdpId.Value, IdentifierType.Other);
        }

        // Phone
        var phone = FindElement(pharmacy, "Phone", ns);
        if (phone != null && !string.IsNullOrEmpty(phone.Value))
        {
            phone.Value = context.GetOrCreateSyntheticId(phone.Value, IdentifierType.PhoneNumber);
        }

        // Address
        var address = FindElement(pharmacy, "Address", ns);
        if (address != null)
        {
            DeIdentifyAddress(address, ns, context);
        }
    }

    private static void DeIdentifyAddress(XElement address, XNamespace ns, DeIdentificationContext context)
    {
        var street = FindElement(address, "Street", ns);
        if (street != null && !string.IsNullOrEmpty(street.Value))
        {
            street.Value = context.GetOrCreateSyntheticId(street.Value, IdentifierType.Address);
        }

        var city = FindElement(address, "City", ns);
        if (city != null && !string.IsNullOrEmpty(city.Value))
        {
            city.Value = "ANYTOWN";
        }

        var postalCode = FindElement(address, "PostalCode", ns);
        if (postalCode != null && !string.IsNullOrEmpty(postalCode.Value) && postalCode.Value.Length >= 3)
        {
            postalCode.Value = DeIdentificationContext.RedactZip(postalCode.Value);
        }

        // Preserve State for geographic analysis
    }

    private void DeIdentifyHeader(XElement header, XNamespace ns, DeIdentificationContext context)
    {
        // MessageID — deterministic hash
        var messageId = FindElement(header, "MessageID", ns);
        if (messageId != null && !string.IsNullOrEmpty(messageId.Value))
        {
            messageId.Value = context.GetOrCreateSyntheticId(messageId.Value, IdentifierType.Other);
        }
    }

    private static void ShiftDateElement(XElement element, DeIdentificationContext context)
    {
        var dateStr = element.Value;

        // Try ISO format (YYYY-MM-DD)
        if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            var shifted = context.ShiftDate(date, preserveTime: false);
            element.Value = shifted.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            context.RecordDateShift(dateStr, element.Value);
            return;
        }

        // Try ISO format with time (YYYY-MM-DDTHH:MM:SSZ)
        if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dateTime))
        {
            var shifted = context.ShiftDate(dateTime);
            element.Value = shifted.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            context.RecordDateShift(dateStr, element.Value);
        }
    }

    /// <summary>
    /// Finds a child element by local name, trying both namespaced and non-namespaced variants.
    /// </summary>
    private static XElement? FindElement(XElement parent, string localName, XNamespace ns)
    {
        // Try with namespace first
        var element = parent.Element(ns + localName);
        if (element != null) return element;

        // Fall back to no namespace
        return parent.Element(localName);
    }

    private static (string family, string given) SplitSyntheticName(string syntheticName)
    {
        var parts = syntheticName.Split('^', 2);
        return parts.Length >= 2 ? (parts[0], parts[1]) : (parts[0], "UNKNOWN");
    }
}
