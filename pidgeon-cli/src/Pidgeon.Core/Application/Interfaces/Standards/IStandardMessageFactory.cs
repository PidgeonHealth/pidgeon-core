// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core;
using Pidgeon.Core.Application.DTOs;

namespace Pidgeon.Core.Application.Interfaces.Standards;

/// <summary>
/// Factory interface for creating standard-specific healthcare messages.
/// Provides consistent API across HL7, FHIR, NCPDP, and future standards.
/// </summary>
public interface IStandardMessageFactory
{
    /// <summary>
    /// Creates a patient admission message (HL7 ADT^A01, FHIR Encounter, etc.).
    /// </summary>
    /// <param name="patient">The patient being admitted</param>
    /// <param name="options">Optional message configuration</param>
    /// <returns>A result containing the admission message or error</returns>
    Result<IStandardMessage> CreatePatientAdmission(PatientDto patient, MessageOptions? options = null);

    /// <summary>
    /// Creates a patient discharge message (HL7 ADT^A03, FHIR Encounter end, etc.).
    /// </summary>
    /// <param name="patient">The patient being discharged</param>
    /// <param name="encounter">The encounter being closed</param>
    /// <param name="options">Optional message configuration</param>
    /// <returns>A result containing the discharge message or error</returns>
    Result<IStandardMessage> CreatePatientDischarge(PatientDto patient, EncounterDto encounter, MessageOptions? options = null);

    /// <summary>
    /// Creates a prescription message (HL7 RDE^O11, FHIR MedicationRequest, NCPDP NewRx).
    /// </summary>
    /// <param name="prescription">The prescription to transmit</param>
    /// <param name="options">Optional message configuration</param>
    /// <returns>A result containing the prescription message or error</returns>
    Result<IStandardMessage> CreatePrescription(PrescriptionDto prescription, MessageOptions? options = null);

    /// <summary>
    /// Creates a lab order message (HL7 ORM^O01, FHIR ServiceRequest).
    /// </summary>
    /// <param name="order">The lab order to transmit</param>
    /// <param name="options">Optional message configuration</param>
    /// <returns>A result containing the lab order message or error</returns>
    Result<IStandardMessage> CreateLabOrder(object order, MessageOptions? options = null);

    /// <summary>
    /// Creates a lab result message (HL7 ORU^R01, FHIR DiagnosticReport).
    /// </summary>
    /// <param name="result">The lab result to transmit</param>
    /// <param name="options">Optional message configuration</param>
    /// <returns>A result containing the lab result message or error</returns>
    Result<IStandardMessage> CreateLabResult(object result, MessageOptions? options = null);

    /// <summary>
    /// Checks if this factory supports creating the specified message type.
    /// </summary>
    /// <param name="messageType">The message type to check (e.g., "ADT^A01", "MedicationRequest")</param>
    /// <returns>True if the message type is supported</returns>
    bool SupportsMessageType(string messageType);

    /// <summary>
    /// Creates a custom message for standard-specific scenarios.
    /// </summary>
    /// <param name="messageType">The specific message type to create</param>
    /// <param name="data">The data object containing message content</param>
    /// <param name="options">Optional message configuration</param>
    /// <returns>A result containing the custom message or error</returns>
    Result<IStandardMessage> CreateCustomMessage(string messageType, object data, MessageOptions? options = null);
}

/// <summary>
/// Configuration options for message creation.
/// Can be extended by standard-specific implementations.
/// </summary>
public class MessageOptions
{
    /// <summary>
    /// Override for message control identifier. If null, factory generates one.
    /// </summary>
    public string? MessageControlId { get; set; }

    /// <summary>
    /// Override for message timestamp. If null, factory uses current time.
    /// </summary>
    public DateTime? Timestamp { get; set; }

    /// <summary>
    /// Sending application identifier.
    /// </summary>
    public string? SendingApplication { get; set; }

    /// <summary>
    /// Receiving application identifier.
    /// </summary>
    public string? ReceivingApplication { get; set; }

    /// <summary>
    /// Sending facility identifier.
    /// </summary>
    public string? SendingFacility { get; set; }

    /// <summary>
    /// Receiving facility identifier.
    /// </summary>
    public string? ReceivingFacility { get; set; }
}