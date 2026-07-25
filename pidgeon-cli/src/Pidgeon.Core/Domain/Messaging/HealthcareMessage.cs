// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Messaging;

/// <summary>
/// Base class for all healthcare messages across standards.
/// Captures universal message concepts while remaining standard-agnostic.
/// </summary>
public abstract class HealthcareMessage
{
    /// <summary>
    /// Gets the unique message control identifier.
    /// This is equivalent to HL7 MSH.10, FHIR Bundle.identifier, NCPDP message ID.
    /// </summary>
    public string MessageControlId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the timestamp when the message was created.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the identifier of the system sending this message.
    /// This is equivalent to HL7 MSH.3, FHIR Bundle.entry.resource.identifier, NCPDP sender ID.
    /// </summary>
    public string SendingSystem { get; set; } = string.Empty;

    /// <summary>
    /// Gets the identifier of the system receiving this message.
    /// This is equivalent to HL7 MSH.5, FHIR Bundle target, NCPDP receiver ID.
    /// </summary>
    public string ReceivingSystem { get; set; } = string.Empty;

    /// <summary>
    /// Gets the healthcare standard this message conforms to.
    /// Values: "HL7v23", "HL7v25", "HL7v251", "FHIR", "NCPDP"
    /// </summary>
    public string Standard { get; set; } = string.Empty;

    /// <summary>
    /// Gets the specific version of the standard.
    /// Examples: "2.3", "2.5.1", "4.0.1", "2017071"
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets the processing instructions for this message.
    /// Values like "Production", "Training", "Debugging"
    /// </summary>
    public string ProcessingId { get; set; } = "Production";

    /// <summary>
    /// Gets additional metadata associated with this message.
    /// Used for extensibility without breaking base class contracts.
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Validates the basic structure and required fields of the healthcare message.
    /// Derived classes should override to add standard-specific validation.
    /// </summary>
    /// <returns>A result indicating whether the message is valid</returns>
    public virtual Result<HealthcareMessage> Validate()
    {
        if (string.IsNullOrWhiteSpace(MessageControlId))
            return Error.Validation("Message Control ID is required", nameof(MessageControlId));

        if (string.IsNullOrWhiteSpace(SendingSystem))
            return Error.Validation("Sending System is required", nameof(SendingSystem));

        if (string.IsNullOrWhiteSpace(ReceivingSystem))
            return Error.Validation("Receiving System is required", nameof(ReceivingSystem));

        if (string.IsNullOrWhiteSpace(Standard))
            return Error.Validation("Standard is required", nameof(Standard));

        if (string.IsNullOrWhiteSpace(Version))
            return Error.Validation("Version is required", nameof(Version));

        if (Timestamp == default)
            return Error.Validation("Timestamp is required", nameof(Timestamp));

        return Result<HealthcareMessage>.Success(this);
    }

    /// <summary>
    /// Gets a human-readable description of this message for logging and debugging.
    /// </summary>
    public virtual string GetDisplaySummary()
    {
        return $"{Standard} v{Version} message {MessageControlId} from {SendingSystem} to {ReceivingSystem} at {Timestamp:yyyy-MM-dd HH:mm:ss}";
    }
}