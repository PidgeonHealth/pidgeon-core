// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.VendorIntelligence;

/// <summary>
/// A curated vendor interface profile parsed from vendor documentation.
/// Represents the complete specification of how a specific vendor system
/// sends and receives HL7 messages on a particular interface.
/// </summary>
public record VendorInterfaceProfile
{
    /// <summary>
    /// Profile metadata (vendor name, standard, version, interface name, direction).
    /// </summary>
    public required ProfileMetadata Profile { get; init; }

    /// <summary>
    /// Segment-level specifications including field requirements.
    /// Key = segment ID (e.g., "MSH", "PID", "RXE").
    /// </summary>
    public Dictionary<string, VendorSegmentSpec> Segments { get; init; } = new();

    /// <summary>
    /// Expected segment ordering for messages on this interface.
    /// </summary>
    public List<string> SegmentOrder { get; init; } = new();

    /// <summary>
    /// Vendor-specific quirks and deviations from the HL7 standard.
    /// </summary>
    public List<VendorQuirk> Quirks { get; init; } = new();

    /// <summary>
    /// Sample messages demonstrating the vendor's format.
    /// </summary>
    public List<SampleMessage> SampleMessages { get; init; } = new();

    /// <summary>
    /// Unique key for this profile: "{vendor}/{interface_name}".
    /// </summary>
    public string Key => $"{Profile.Vendor.ToLowerInvariant()}/{Profile.InterfaceName.ToLowerInvariant().Replace(" ", "_")}";
}

/// <summary>
/// Metadata about the vendor interface profile.
/// </summary>
public record ProfileMetadata
{
    public required string Vendor { get; init; }
    public string? VendorVersion { get; init; }
    public required string Standard { get; init; }
    public required string Version { get; init; }
    public required string InterfaceName { get; init; }
    public required string Direction { get; init; }
    public List<string> MessageTypes { get; init; } = new();
}

/// <summary>
/// Specification for a segment within a vendor interface profile.
/// </summary>
public record VendorSegmentSpec
{
    public bool Required { get; init; }
    public bool Repeating { get; init; }
    public Dictionary<string, VendorFieldSpec> Fields { get; init; } = new();
}

/// <summary>
/// Field-level specification within a vendor interface profile.
/// </summary>
public record VendorFieldSpec
{
    public required string Name { get; init; }
    public bool Required { get; init; }
    public string? ExpectedValue { get; init; }
    public string? Format { get; init; }
    public int? MaxLength { get; init; }
    public string? CodingSystem { get; init; }
    public Dictionary<string, VendorSubfieldSpec>? Subfields { get; init; }
    public Dictionary<string, string>? AllowedValues { get; init; }

    /// <summary>
    /// Optional declared value-provenance for this field: a closed-vocabulary token
    /// (e.g. <c>patient.id</c>, <c>patient.phone</c>, <c>now</c>) stating that the field's value comes
    /// from the message's clinical context rather than a fixed literal. A declarative profile property,
    /// the same kind as <see cref="ExpectedValue"/> and <see cref="Format"/> — it says *what* the value
    /// is, not *how* any one consumer populates it. The Z-segment composer reads it to keep a vendor
    /// Z-segment coherent with the patient; precedence is <see cref="ExpectedValue"/> (a fixed literal)
    /// over this over <see cref="Format"/>.
    /// </summary>
    public string? Source { get; init; }
}

/// <summary>
/// Subfield (component) specification within a vendor field spec.
/// </summary>
public record VendorSubfieldSpec
{
    public required string Name { get; init; }
    public bool Required { get; init; }
    public string? ExpectedValue { get; init; }
    public string? Format { get; init; }
    public int? MaxLength { get; init; }
}

/// <summary>
/// A vendor-specific quirk or deviation from the HL7 standard.
/// </summary>
public record VendorQuirk
{
    public required string Description { get; init; }
    public required string Field { get; init; }
    public required string Rule { get; init; }
}

/// <summary>
/// A sample message demonstrating vendor format.
/// </summary>
public record SampleMessage
{
    public required string Name { get; init; }
    public required string Trigger { get; init; }
    public required string Message { get; init; }
}
