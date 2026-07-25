// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Universal factory interface for generating any HL7 v2.3 standards-compliant message.
/// Uses data-driven approach to support all trigger events defined by the installed data composition.
/// </summary>
public interface IHL7MessageFactory
{
    /// <summary>
    /// Universal message generation method - takes any HL7 message type and generates it
    /// using the corresponding trigger event JSON definition from the installed data composition.
    /// </summary>
    /// <param name="messageType">Any HL7 message type (e.g., "ADT^A01", "ORM^O01", "ORU^R01")</param>
    /// <param name="patient">Patient data (required for all messages)</param>
    /// <param name="encounter">Encounter data (optional, for visit-related messages)</param>
    /// <param name="prescription">Prescription data (optional, for pharmacy messages)</param>
    /// <param name="observation">Observation data (optional, for lab messages)</param>
    /// <param name="order">Order data (optional, for order messages)</param>
    /// <param name="options">Generation options</param>
    /// <returns>Standards-compliant HL7 message generated from trigger event definition</returns>
    Task<Result<string>> GenerateMessageAsync(
        string messageType,
        Patient patient,
        Encounter? encounter = null,
        Prescription? prescription = null,
        ObservationResult? observation = null,
        Order? order = null,
        GenerationOptions? options = null);
}


/// <summary>
/// Represents a clinical order for order management messages.
/// </summary>
public record Order
{
    public required string Id { get; init; }
    public required string OrderType { get; init; } // LAB, RAD, PHARM, etc.
    public required string Description { get; init; }
    public required string OrderCode { get; init; }
    public string? Priority { get; init; } = "R"; // R=Routine, S=Stat, A=ASAP
    public string? Status { get; init; } = "NW"; // NW=New, IP=In Progress, CM=Complete
    public DateTime? OrderDateTime { get; init; }
    public Provider? OrderingProvider { get; init; }
}