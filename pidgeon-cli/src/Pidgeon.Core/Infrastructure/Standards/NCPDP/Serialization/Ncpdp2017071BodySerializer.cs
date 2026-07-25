// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Xml.Linq;
using Pidgeon.Core.Domain.Messaging.NCPDP;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP.Serialization;

/// <summary>
/// Body serializer for SCRIPT 2017071. Emits the 2017071 body shapes: a flat <c>Name</c>, a
/// <c>Gender</c> text element, a <c>DrugCoded/ProductCode</c> medication code, a direct
/// <c>Substitutions</c> code, and an RxFill <c>FillStatus</c>. This is the body logic the unified
/// serializer carried before the per-release split; it remains independently XSD-validated against
/// the 2017071 oracle.
/// </summary>
public sealed class Ncpdp2017071BodySerializer : INcpdpBodySerializer
{
    public bool CanSerialize(string version) => version == "2017071";

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
        // NewRx sequence (required children only): Patient, Pharmacy?, Prescriber,
        // MedicationPrescribed.
        var el = b.El("NewRx", SerializePatient(b, newRx.Patient, addressMandatory: true));

        if (newRx.Pharmacy != null)
            el.Add(SerializePharmacy(b, newRx.Pharmacy, addressMandatory: false));

        el.Add(
            SerializePrescriber(b, newRx.Prescriber, addressMandatory: true),
            SerializeMedication(b, "MedicationPrescribed", newRx.MedicationPrescribed));

        return el;
    }

    private static XElement SerializeRxFill(NcpdpXmlBuilders b, NCPDPRxFill rxFill)
    {
        // RxFill sequence (required children only): FillStatus, Patient, Pharmacy, Prescriber,
        // MedicationDispensed?.
        return b.El("RxFill",
            b.El("FillStatus", b.El("Dispensed")),
            SerializePatient(b, rxFill.Patient, addressMandatory: false),
            SerializePharmacy(b, rxFill.Pharmacy, addressMandatory: true),
            SerializePrescriber(b, rxFill.Prescriber, addressMandatory: false),
            SerializeMedication(b, "MedicationDispensed", rxFill.MedicationDispensed));
    }

    private static XElement SerializeCancelRx(NcpdpXmlBuilders b, NCPDPCancelRx cancelRx)
    {
        // CancelRx sequence (required children only): Patient, Prescriber, MedicationPrescribed.
        return b.El("CancelRx",
            SerializePatient(b, cancelRx.Patient, addressMandatory: false),
            SerializePrescriber(b, cancelRx.Prescriber, addressMandatory: false),
            SerializeMedication(b, "MedicationPrescribed", cancelRx.MedicationPrescribed));
    }

    private static XElement SerializePatient(NcpdpXmlBuilders b, NCPDPPatient patient, bool addressMandatory)
    {
        // Patient is a HumanPatient/NonHumanPatient choice; sequence: Identification?, Name,
        // Gender, DateOfBirth, Address, CommunicationNumbers?.
        var human = b.El("HumanPatient",
            b.Name(patient.LastName, patient.FirstName),
            b.El("Gender", patient.Gender),
            b.Date("DateOfBirth", patient.DateOfBirth));

        if (patient.Address != null || addressMandatory)
            human.Add(b.Address(patient.Address, addressMandatory));

        var comms = b.CommunicationNumbers(patient.Phone);
        if (comms != null)
            human.Add(comms);

        return b.El("Patient", human);
    }

    private static XElement SerializePrescriber(NcpdpXmlBuilders b, NCPDPPrescriber prescriber, bool addressMandatory)
    {
        // Prescriber is a NonVeterinarian/Veterinarian choice; sequence: Identification, Specialty?,
        // Name, Address (mandatory on NewRx, optional elsewhere), CommunicationNumbers.
        var nonVet = b.El("NonVeterinarian", SerializePrescriberId(b, prescriber));

        if (prescriber.Specialty != null)
            nonVet.Add(b.El("Specialty", prescriber.Specialty));

        nonVet.Add(b.Name(prescriber.LastName, prescriber.FirstName));

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
        // Pharmacy sequence: Identification (NCPDPID + NPI?), Specialty?, Pharmacist?, BusinessName,
        // Address, CommunicationNumbers.
        var identification = b.El("Identification", b.El("NCPDPID", pharmacy.NCPDPID));
        if (pharmacy.NPI != null)
            identification.Add(b.El("NPI", pharmacy.NPI));

        var el = b.El("Pharmacy", identification);

        // NameType requires both LastName and FirstName; the Pharmacist element itself is optional.
        // Emit it only when both name parts are present rather than duplicating the last name into
        // the first-name slot (which would assert a wrong given name).
        if (pharmacy.PharmacistLastName != null && pharmacy.PharmacistFirstName != null)
            el.Add(b.El("Pharmacist",
                b.Name(pharmacy.PharmacistLastName, pharmacy.PharmacistFirstName)));

        el.Add(b.El("BusinessName", pharmacy.StoreName));

        if (pharmacy.Address != null || addressMandatory)
            el.Add(b.Address(pharmacy.Address, addressMandatory));

        el.Add(b.CommunicationNumbers(pharmacy.Phone) ?? b.DefaultCommunicationNumbers());

        return el;
    }

    private static XElement SerializeMedication(NcpdpXmlBuilders b, string elementName, NCPDPMedication medication)
    {
        // Prescribed/Dispensed medication sequence (required children): DrugDescription, DrugCoded?,
        // Quantity, DaysSupply?, WrittenDate, LastFillDate?, Substitutions, NumberOfRefills, ..., Sig.
        var el = b.El(elementName, b.El("DrugDescription", medication.DrugDescription));

        // The NDC carries on DrugCoded/ProductCode (Qualifier "ND" = National Drug Code), the
        // schema-valid home — not a bare <NDC> element, which the 2017071 schema has no place for.
        if (medication.NDCCode != null)
            el.Add(b.El("DrugCoded",
                b.El("ProductCode",
                    b.El("Code", medication.NDCCode),
                    b.El("Qualifier", "ND"))));

        el.Add(b.El("Quantity", b.Quantity(medication.Quantity, medication.QuantityUnitCode)));

        if (medication.DaysSupply > 0)
            el.Add(b.El("DaysSupply", medication.DaysSupply));

        el.Add(b.Date("WrittenDate", medication.WrittenDate));

        // RxFill dispensing carries the actual fill date; SCRIPT's home for it on the dispensed
        // medication is LastFillDate (the most recent dispensing event for the prescription).
        if (medication.LastFillDate is DateTime lastFill)
            el.Add(b.Date("LastFillDate", lastFill));

        el.Add(
            b.El("Substitutions", medication.Substitutions),
            b.El("NumberOfRefills", medication.Refills),
            b.El("Sig", b.El("SigText", medication.SigText)));

        return el;
    }
}
