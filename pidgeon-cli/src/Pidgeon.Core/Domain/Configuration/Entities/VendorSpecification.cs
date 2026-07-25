// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Configuration.Entities;

/// <summary>
/// Complete vendor-specific healthcare interface specification.
/// Represents detailed field-level mappings, constraints, and business rules for a specific vendor.
/// Supports all healthcare standards (HL7, FHIR, NCPDP, DICOM) and interface types 
/// (EHR↔Pharmacy, EHR↔Labs, EHR↔Imaging, Device↔EHR, etc.).
/// Based on real-world vendor interface documentation like Epic, Cerner, AllScripts, etc.
/// </summary>
public record VendorSpecification
{
    /// <summary>
    /// Unique identifier for this vendor specification.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Specification metadata including interface type and directionality.
    /// </summary>
    public required SpecificationInfo Specification { get; init; }

    /// <summary>
    /// Message types that this vendor sends TO the receiving system.
    /// Examples: ADT, ORM, ORU (labs), SIU (scheduling), DFT (billing), SCRIPT (prescriptions)
    /// </summary>
    public Dictionary<string, MessageTypeSpec> MessagesToReceiver { get; init; } = new();

    /// <summary>
    /// Message types that this vendor expects FROM the receiving system.
    /// Examples: ORR, ACK, RDS, RDE (pharmacy), ORU (lab results), SIU (scheduling responses)
    /// </summary>
    public Dictionary<string, MessageTypeSpec> MessagesFromReceiver { get; init; } = new();

    /// <summary>
    /// Vendor-specific detection patterns for message identification.
    /// Used to identify if an incoming message matches this vendor's patterns.
    /// </summary>
    public VendorDetectionInfo DetectionInfo { get; init; } = new();

    /// <summary>
    /// Common deviations from HL7 standard that this vendor exhibits.
    /// </summary>
    public List<VendorDeviation> CommonDeviations { get; init; } = new();
}

/// <summary>
/// Basic specification information and metadata.
/// </summary>
public record SpecificationInfo
{
    /// <summary>
    /// Human-readable specification name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Vendor/organization name.
    /// </summary>
    public required string VendorName { get; init; }

    /// <summary>
    /// Healthcare standard and version (e.g., "HL7v2.3", "FHIR_R4", "NCPDP_SCRIPT_2017071").
    /// </summary>
    public required string Standard { get; init; }

    /// <summary>
    /// Interface type and directionality.
    /// Examples: "EHR_to_Pharmacy", "Lab_to_EHR", "EHR_to_EHR", "Device_to_EHR"
    /// </summary>
    public required string InterfaceType { get; init; }

    /// <summary>
    /// Whether this specification contains confidential/proprietary information.
    /// </summary>
    public bool Confidential { get; init; }

    /// <summary>
    /// Documentation date or version.
    /// </summary>
    public string? DocumentVersion { get; init; }

    /// <summary>
    /// Additional notes or description.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Specification for a specific message type (ADT, ORM, etc.).
/// </summary>
public record MessageTypeSpec
{
    /// <summary>
    /// Human-readable description of this message type.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Segment specifications for this message type.
    /// Key = segment name (MSH, PID, PV1, etc.)
    /// </summary>
    public Dictionary<string, SegmentSpec> Segments { get; init; } = new();

    /// <summary>
    /// Sample messages showing actual vendor format.
    /// </summary>
    public List<string> SampleMessages { get; init; } = new();

    /// <summary>
    /// Additional notes specific to this message type.
    /// </summary>
    public List<string> Notes { get; init; } = new();

    /// <summary>
    /// Whether this message type repeats or is single occurrence.
    /// </summary>
    public bool Repeating { get; init; }

    /// <summary>
    /// Whether this message type is required in the interface.
    /// </summary>
    public bool Required { get; init; } = true;
}

/// <summary>
/// Specification for a specific HL7 segment within a message.
/// </summary>
public record SegmentSpec
{
    /// <summary>
    /// Human-readable description of this segment.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Field specifications within this segment.
    /// Ordered by field position.
    /// </summary>
    public List<FieldSpec> Fields { get; init; } = new();

    /// <summary>
    /// Whether this segment can repeat within a message.
    /// </summary>
    public bool Repeating { get; init; }

    /// <summary>
    /// Whether this segment is required in the message.
    /// </summary>
    public bool Required { get; init; } = true;

    /// <summary>
    /// References to other segments this inherits from.
    /// Format: "MessageType.SegmentName" (e.g., "ORR.PID")
    /// </summary>
    public string? InheritsFrom { get; init; }
}

/// <summary>
/// Specification for a specific field within a segment.
/// </summary>
public record FieldSpec
{
    /// <summary>
    /// Field position within the segment (1-based).
    /// </summary>
    public required int Position { get; init; }

    /// <summary>
    /// Standard HL7 field name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Vendor-specific description or interpretation.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Field usage by this vendor.
    /// </summary>
    public required FieldUsage Usage { get; init; }

    /// <summary>
    /// Maximum field length constraint.
    /// </summary>
    public int? Length { get; init; }

    /// <summary>
    /// Component specifications for complex fields.
    /// Key = component number (1-based)
    /// </summary>
    public Dictionary<int, ComponentSpec> Components { get; init; } = new();

    /// <summary>
    /// Fixed value that this vendor always sends.
    /// </summary>
    public string? FixedValue { get; init; }

    /// <summary>
    /// Allowed values with their meanings.
    /// Key = value, Value = description
    /// </summary>
    public Dictionary<string, string> AllowedValues { get; init; } = new();

    /// <summary>
    /// Whether this field is sent by the vendor.
    /// Used for response message specifications.
    /// </summary>
    public bool? Sent { get; init; }
}

/// <summary>
/// Specification for a component within a complex field.
/// </summary>
public record ComponentSpec
{
    /// <summary>
    /// Component name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Vendor-specific description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Component usage by this vendor.
    /// </summary>
    public required FieldUsage Usage { get; init; }

    /// <summary>
    /// Maximum component length.
    /// </summary>
    public int? Length { get; init; }

    /// <summary>
    /// Fixed value for this component.
    /// </summary>
    public string? FixedValue { get; init; }

    /// <summary>
    /// Allowed values with descriptions.
    /// </summary>
    public Dictionary<string, string> AllowedValues { get; init; } = new();

    /// <summary>
    /// Whether this component is sent by the vendor.
    /// </summary>
    public bool? Sent { get; init; }
}

/// <summary>
/// How a vendor uses a specific field or component.
/// </summary>
public enum FieldUsage
{
    /// <summary>
    /// Field is required and must be present.
    /// </summary>
    Required,

    /// <summary>
    /// Field is optional and may be present.
    /// </summary>
    Optional,

    /// <summary>
    /// Field is ignored by the vendor.
    /// </summary>
    Ignored
}

/// <summary>
/// Information for detecting if a message matches this vendor's patterns.
/// </summary>
public record VendorDetectionInfo
{
    /// <summary>
    /// Application names commonly used by this vendor.
    /// Used in MSH.3 (Sending Application).
    /// </summary>
    public List<string> CommonApplicationNames { get; init; } = new();

    /// <summary>
    /// Facility patterns commonly used by this vendor.
    /// Used in MSH.4 (Sending Facility).
    /// </summary>
    public List<string> CommonFacilityPatterns { get; init; } = new();

    /// <summary>
    /// Message type patterns specific to this vendor.
    /// Includes trigger events and constraints.
    /// </summary>
    public List<string> MessageTypePatterns { get; init; } = new();

    /// <summary>
    /// Field patterns that uniquely identify this vendor.
    /// Format: "Segment.Field=Pattern" (e.g., "MSH.3=EPIC*")
    /// </summary>
    public List<string> UniqueFieldPatterns { get; init; } = new();

    /// <summary>
    /// Overall confidence level for vendor detection (0.0-1.0).
    /// </summary>
    public double BaseConfidence { get; init; } = 0.8;
}

/// <summary>
/// A deviation from standard HL7 that this vendor commonly exhibits.
/// </summary>
public record VendorDeviation
{
    /// <summary>
    /// Type of deviation.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Location where deviation occurs (e.g., "MSH.3", "PID.5.1").
    /// </summary>
    public required string Location { get; init; }

    /// <summary>
    /// Description of the deviation.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// How frequently this deviation occurs (0.0-1.0).
    /// </summary>
    public double Frequency { get; init; } = 1.0;

    /// <summary>
    /// Severity of the deviation.
    /// </summary>
    public string Severity { get; init; } = "Info";

    /// <summary>
    /// Examples of the deviation.
    /// </summary>
    public List<string> Examples { get; init; } = new();
}