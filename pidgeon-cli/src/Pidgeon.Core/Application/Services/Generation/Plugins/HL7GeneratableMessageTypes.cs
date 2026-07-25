// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Services.Generation.Plugins;

/// <summary>
/// The set of HL7 message types the generation plugin can produce. Add an entry
/// here when registering a new type; the dispatch switch in
/// <see cref="HL7MessageGenerationPlugin"/> needs a matching base-type arm.
/// </summary>
internal static class HL7GeneratableMessageTypes
{
    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        // === ADT - Admit/Discharge/Transfer (Patient Movement) ===
        "ADT^A01", // Patient Admission - Notifies all systems when a patient is admitted to the facility
        "ADT^A02", // Patient Transfer - Notifies systems when patient moves between units/rooms
        "ADT^A03", // Patient Discharge - Notifies all systems when patient is discharged from facility
        "ADT^A04", // Patient Registration - Registers outpatient for services without admission
        "ADT^A05", // Pre-Admission - Pre-registers patient for future admission
        "ADT^A08", // Patient Information Update - Updates patient demographics or visit information
        "ADT^A11", // Cancel Admit - Cancels a previous admission notification
        "ADT^A12", // Cancel Transfer - Cancels a previous transfer notification
        "ADT^A13", // Cancel Discharge - Cancels a previous discharge notification
        "ADT",     // Generic ADT Message - Admit/Discharge/Transfer message (specify trigger event)

        // === ORM - Order Management (Clinical Orders) ===
        "ORM^O01", // General Order - Places new orders for lab tests, medications, procedures
        "ORM^O02", // Order Response - Confirms receipt and processing of orders
        "ORM^O03", // Diet Order - Specific order for dietary/nutrition requirements
        "ORM",     // Generic Order Message - Order management message (specify trigger event)

        // === ORU - Observation Results (Test Results/Reports) ===
        "ORU^R01", // Unsolicited Observation - Lab results, vital signs, diagnostic reports
        "ORU^R02", // Query Response - Results sent in response to a query
        "ORU^R03", // Display Response - Results formatted for display purposes
        "ORU^R04", // Response to Query/Display - Combined query response and display data
        "ORU",     // Generic Result Message - Observation result message (specify trigger event)

        // === OML - Laboratory Order (Order Entry, v2.4+) ===
        "OML^O21", // Laboratory Order - Places laboratory test orders (ORC/OBR/SPM)
        "OML",     // Generic Laboratory Order - Laboratory order message (specify trigger event)

        // === RDE - Pharmacy Orders (Medication Management) ===
        "RDE^O11", // Pharmacy/Treatment Encoded Order - Detailed pharmacy order with dosing instructions
        "RDE^O25", // Refill Authorization Request - Request for medication refill authorization
        "RDE",     // Generic Pharmacy Order - Pharmacy order message (specify trigger event)

        // === RGV - Pharmacy Give (Medication Administration) ===
        "RGV^O15", // Pharmacy/Treatment Give - Records medication administration to patient
        "RGV",     // Generic Pharmacy Give - Medication administration message

        // === RAS - Pharmacy Administration (Medication Status) ===
        "RAS^O17", // Pharmacy/Treatment Administration - Pharmacy administration and status updates
        "RAS",     // Generic Administration Message - Pharmacy administration status

        // === SIU - Scheduling (Appointment Management) ===
        "SIU^S12", // New Appointment Booking - Creates new patient appointment
        "SIU^S13", // Appointment Rescheduling - Modifies existing appointment time/date
        "SIU^S14", // Appointment Modification - Updates appointment details
        "SIU^S15", // Appointment Cancellation - Cancels existing patient appointment
        "SIU^S17", // Appointment Deletion - Removes appointment from system
        "SIU",     // Generic Scheduling Message - Scheduling information message

        // === VXU - Immunization (Vaccination Records) ===
        "VXU^V04", // Unsolicited Vaccination Record Update - Reports vaccination administration

        // === MDM - Medical Document Management (Clinical Documentation) ===
        "MDM^T02", // Original Document Notification - New clinical document created
        "MDM^T04", // Document Edit Notification - Existing document has been modified
        "MDM^T06", // Document Addendum Notification - Addendum added to existing document
        "MDM^T08", // Document Edit Completion - Document editing process completed
        "MDM^T10", // Document Replacement - Document replaced with new version
        "MDM",     // Generic Document Message - Medical document management message

        // === QBP/RSP - Query/Response (Data Requests) ===
        "QBP^Q11", // Query by Parameter - Request for specific patient/clinical data
        "QBP^Q21", // Find Candidates Query - Search for patients matching criteria
        "QBP^Q22", // Find Personnel Query - Search for staff/provider information
        "RSP^K11", // Segment Pattern Response - Response to query with requested data
        "RSP^K21", // Find Candidates Response - Patient search results
        "RSP^K22", // Find Personnel Response - Staff search results

        // === ACK - Acknowledgment (System Communication) ===
        "ACK",     // General Acknowledgment - Confirms receipt and processing status of messages

        // === BAR - Add/Change Billing (Financial) ===
        "BAR^P01", // Add Patient Accounts - Creates new billing account for patient
        "BAR^P02", // Purge Patient Accounts - Removes patient billing account
        "BAR^P05", // Update Account - Modifies existing patient account information
        "BAR",     // Generic Billing Message - Billing account management message

        // === DFT - Detailed Financial Transaction (Financial) ===
        "DFT^P03", // Post Detail Financial Transaction - Posts charges/payments to a patient account
        "DFT"      // Generic Financial Transaction - Detailed financial transaction message
    };
}
