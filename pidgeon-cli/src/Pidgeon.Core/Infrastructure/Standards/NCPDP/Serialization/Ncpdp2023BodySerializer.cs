// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Xml.Linq;
using Pidgeon.Core.Domain.Messaging.NCPDP;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP.Serialization;

/// <summary>
/// Body serializer for SCRIPT 2023011 and 2023071. The two releases share one required-path body
/// shape — their published differences are all optional or maxOccurs=0 elements this serializer does
/// not emit — so one implementation owns both versions.
///
/// SCRIPT 2023 re-architected the body relative to 2017071:
/// <list type="bullet">
///   <item>the flat <c>Name</c> became a <c>Names</c> NameComposite wrapping <c>Name</c>;</item>
///   <item>the <c>Gender</c> text element became a <c>GenderAndSex</c> composite with an
///   <c>AdministrativeGender</c> child;</item>
///   <item>the medication code moved into a <c>Product</c> wrapper whose <c>DrugCoded</c> carries a
///   real <c>NDC</c> element, the quantity unit of measure became schema-required, and
///   <c>Substitutions</c> became a SubstitutionType composite;</item>
///   <item>RxFill's <c>FillStatus</c> became a required <c>StatusType</c> choice (e.g. Dispensed);</item>
///   <item>the prescriber/pharmacy identification elements gained explicit per-id-type tags.</item>
/// </list>
/// Only structural reshaping of values the generator already produces happens here — no clinical
/// values are fabricated. When a schema-required element has no source value, the absence is
/// surfaced (the document fails its oracle) rather than papered over with an invented value.
/// </summary>
public sealed class Ncpdp2023BodySerializer : INcpdpBodySerializer
{
    public bool CanSerialize(string version) => version is "2023011" or "2023071";

    public XElement SerializeBody(NCPDPBody body, XNamespace ns)
    {
        var b = new NcpdpXmlBuilders(ns);
        var bodyEl = b.El("Body");

        if (body.NewRx != null)
            bodyEl.Add(SerializeNewRx(b, body.NewRx));
        else if (body.RxFill != null)
            bodyEl.Add(SerializeRxFill(b, body.RxFill));
        else if (body.CancelRx != null)
            bodyEl.Add(SerializeCancelRx(b, body.CancelRx));

        return bodyEl;
    }

    private static XElement SerializeNewRx(NcpdpXmlBuilders b, NCPDPNewRx newRx)
    {
        // NewRx required children: Patient (PatientMandatoryAddressType), Pharmacy?, Prescriber
        // (MandatoryPrescriberChoice), MedicationPrescribed (NewRxPrescribedMedication).
        var el = b.El("NewRx", SerializePatient(b, newRx.Patient, addressMandatory: true));

        if (newRx.Pharmacy != null)
            el.Add(SerializePharmacy(b, newRx.Pharmacy, addressMandatory: false));

        el.Add(
            SerializePrescriber(b, newRx.Prescriber, addressMandatory: true, identificationMandatory: true),
            SerializeMedication(b, "MedicationPrescribed", newRx.MedicationPrescribed));

        return el;
    }

    private static XElement SerializeRxFill(NcpdpXmlBuilders b, NCPDPRxFill rxFill)
    {
        // RxFill required children: StatusType (replaces FillStatus), Patient, Pharmacy
        // (MandatoryAddressPharmacy), Prescriber (PrescriberGeneralChoice; Identification optional).
        // MedicationDispensed is XSD-optional, but the SCRIPT business rule (script.xsd, RxFill
        // StatusType documentation) requires it when StatusType is Dispensed/PartiallyDispensed —
        // and the generator always emits Dispensed with a fully populated dispensed medication. So it
        // is emitted, carrying the dispensed quantity, the NDC, and the fill date the domain supplies.
        var el = b.El("RxFill",
            SerializeStatusType(b, rxFill.FillStatus),
            SerializePatient(b, rxFill.Patient, addressMandatory: false),
            SerializePharmacy(b, rxFill.Pharmacy, addressMandatory: true),
            SerializePrescriber(b, rxFill.Prescriber, addressMandatory: false, identificationMandatory: false));

        // RxFillDispensedMedication shares the 2023 Medication required-path shape (DrugDescription,
        // Product, Quantity, Substitutions, NumberOfRefills, Sig). Emit it after Prescriber, the
        // schema-ordered position for MedicationDispensed.
        if (rxFill.MedicationDispensed != null)
            el.Add(SerializeMedication(b, "MedicationDispensed", rxFill.MedicationDispensed));

        return el;
    }

    private static XElement SerializeCancelRx(NcpdpXmlBuilders b, NCPDPCancelRx cancelRx)
    {
        // CancelRx required children: Patient (PatientType), Prescriber (PrescriberChoice),
        // MedicationPrescribed (PrescribedMedicationForCancelRx — same required shape as NewRx).
        return b.El("CancelRx",
            SerializePatient(b, cancelRx.Patient, addressMandatory: false),
            SerializePrescriber(b, cancelRx.Prescriber, addressMandatory: false, identificationMandatory: true),
            SerializeMedication(b, "MedicationPrescribed", cancelRx.MedicationPrescribed));
    }

    private static XElement SerializePatient(NcpdpXmlBuilders b, NCPDPPatient patient, bool addressMandatory)
    {
        // HumanPatient sequence: Identification?, Names (NameComposite), GenderAndSex, DateOfBirth,
        // Address (mandatory on NewRx, optional elsewhere), CommunicationNumbers?.
        var human = b.El("HumanPatient",
            Names(b, patient.LastName, patient.FirstName),
            GenderAndSex(b, patient.Gender),
            b.Date("DateOfBirth", patient.DateOfBirth));

        if (patient.Address != null || addressMandatory)
            human.Add(b.Address(patient.Address, addressMandatory));

        var comms = b.CommunicationNumbers(patient.Phone);
        if (comms != null)
            human.Add(comms);

        return b.El("Patient", human);
    }

    private static XElement SerializePrescriber(
        NcpdpXmlBuilders b, NCPDPPrescriber prescriber, bool addressMandatory, bool identificationMandatory)
    {
        // NonVeterinarian sequence: Identification(?), Specialty?, PracticeLocation?, Names, Address?,
        // PrescriberAgent?, CommunicationNumbers. Identification is required on NewRx/CancelRx and
        // optional on RxFill (PrescriberGeneral).
        var nonVet = b.El("NonVeterinarian");

        if (identificationMandatory || prescriber.NPI != null)
            nonVet.Add(SerializePrescriberId(b, prescriber));

        // SCRIPT 2023 narrowed Specialty to an1..10 — a Health Care Provider Taxonomy CODE, not a
        // free-text specialty name. The domain carries the descriptive name ("Family Medicine"), which
        // neither fits the length nor is a taxonomy code, so the optional element is omitted rather
        // than emitting a truncated/invalid value that would assert a wrong code.
        nonVet.Add(Names(b, prescriber.LastName, prescriber.FirstName));

        if (prescriber.Address != null || addressMandatory)
            nonVet.Add(b.Address(prescriber.Address, addressMandatory));

        nonVet.Add(b.CommunicationNumbers(prescriber.Phone) ?? b.DefaultCommunicationNumbers());

        return b.El("Prescriber", nonVet);
    }

    private static XElement SerializePrescriberId(NcpdpXmlBuilders b, NCPDPPrescriber prescriber)
    {
        // NonVeterinarianID sequence (subset): ..., DEANumber?, ..., NPI (required, ordered last).
        var id = b.El("Identification");

        if (prescriber.DEA != null)
            id.Add(b.El("DEANumber", prescriber.DEA));

        id.Add(b.El("NPI", prescriber.NPI));
        return id;
    }

    private static XElement SerializePharmacy(NcpdpXmlBuilders b, NCPDPPharmacy pharmacy, bool addressMandatory)
    {
        // Pharmacy sequence: Identification (PharmacyID: NCPDPID + NPI), Specialty?, Pharmacist?,
        // BusinessName, Address, CommunicationNumbers. PharmacyID requires NPI as well as NCPDPID.
        var identification = b.El("Identification", b.El("NCPDPID", pharmacy.NCPDPID));
        if (pharmacy.NPI != null)
            identification.Add(b.El("NPI", pharmacy.NPI));

        var el = b.El("Pharmacy", identification);

        // Pharmacist is optional; emit it only when both name parts are present, with the 2023
        // Names composite. Emitting it with a missing given name would assert a wrong name.
        if (pharmacy.PharmacistLastName != null && pharmacy.PharmacistFirstName != null)
            el.Add(b.El("Pharmacist",
                Names(b, pharmacy.PharmacistLastName, pharmacy.PharmacistFirstName)));

        el.Add(b.El("BusinessName", pharmacy.StoreName));

        if (pharmacy.Address != null || addressMandatory)
            el.Add(b.Address(pharmacy.Address, addressMandatory));

        el.Add(b.CommunicationNumbers(pharmacy.Phone) ?? b.DefaultCommunicationNumbers());

        return el;
    }

    private static XElement SerializeMedication(NcpdpXmlBuilders b, string elementName, NCPDPMedication medication)
    {
        // 2023 medication sequence (required children): DrugDescription, Product (ProductMandatory),
        // Quantity, DaysSupply?, WrittenDate, LastFillDate?, Substitutions (SubstitutionType),
        // NumberOfRefills, ..., Sig.
        var el = b.El(elementName, b.El("DrugDescription", medication.DrugDescription));

        // Product/DrugCoded/NDC: ProductMandatory is a required choice with no empty branch, so a
        // null NDC has no schema-valid emission. The generator carries a real NDC; when it is
        // genuinely absent the Product element is omitted, leaving the document to fail its oracle —
        // an honest "missing required data" rather than a fabricated NDC.
        if (medication.NDCCode != null)
            el.Add(b.El("Product",
                b.El("DrugCoded",
                    b.El("NDC", medication.NDCCode))));

        el.Add(b.El("Quantity", b.Quantity(medication.Quantity, medication.QuantityUnitCode)));

        if (medication.DaysSupply > 0)
            el.Add(b.El("DaysSupply", medication.DaysSupply));

        el.Add(b.Date("WrittenDate", medication.WrittenDate));

        if (medication.LastFillDate is DateTime lastFill)
            el.Add(b.Date("LastFillDate", lastFill));

        // SubstitutionType wraps the substitution code in its own Substitutions child (2017071 carried
        // the code directly on the Substitutions element).
        el.Add(
            b.El("Substitutions", b.El("Substitutions", medication.Substitutions)),
            b.El("NumberOfRefills", medication.Refills),
            b.El("Sig", b.El("SigText", medication.SigText)));

        return el;
    }

    // GenderAndSexType: AdministrativeGender (required) carries the gender code; the rest of the
    // composite (SexAssignedAtBirth, ReproductivePotential) is optional and omitted.
    private static XElement GenderAndSex(NcpdpXmlBuilders b, string gender)
        => b.El("GenderAndSex", b.El("AdministrativeGender", gender));

    // NameComposite: a required Name (NameType) plus optional variants the generator does not carry.
    private static XElement Names(NcpdpXmlBuilders b, string lastName, string firstName)
        => b.El("Names", b.Name(lastName, firstName));

    // StatusType is a required choice. Map the domain FillStatus string to its choice child; the
    // child's inner content (ReferenceNumber, Note, ReasonCode) is optional, so an empty element is
    // schema-valid. An unrecognized status falls back to Dispensed, the generated default.
    private static XElement SerializeStatusType(NcpdpXmlBuilders b, string fillStatus)
    {
        var choice = fillStatus switch
        {
            "PartiallyDispensed" => "PartiallyDispensed",
            "NotDispensed" => "NotDispensed",
            "Transferred" => "Transferred",
            _ => "Dispensed"
        };

        return b.El("StatusType", b.El(choice));
    }
}
