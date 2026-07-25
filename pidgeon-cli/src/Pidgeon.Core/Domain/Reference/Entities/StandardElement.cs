// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Reference.Entities;

/// <summary>
/// Represents a single element in a healthcare standard (field, segment, resource, etc.).
/// Core domain model for the standards reference lookup system.
/// </summary>
public record StandardElement
{
    /// <summary>
    /// The full path to this element (e.g., "PID.3.5", "Patient.identifier", "NewRx.Patient").
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// Human-readable name of the element (e.g., "Patient Identifier Type Code").
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Detailed description of the element's purpose and usage.
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// Data type specification (e.g., "CX", "string", "CodeableConcept").
    /// </summary>
    public string DataType { get; init; } = "";

    /// <summary>
    /// Usage specification: Required (R), Optional (O), Conditional (C), Not used (X).
    /// </summary>
    public string Usage { get; init; } = "";

    /// <summary>
    /// Maximum length constraint for the field, if applicable.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Position number within the segment for HL7 fields (e.g., 8 for PID.8).
    /// </summary>
    public int? Position { get; init; }

    /// <summary>
    /// Repeatability indicator: "-" (non-repeating), "∞" (unlimited), or number (max repeats).
    /// </summary>
    public string? Repeatability { get; init; }

    /// <summary>
    /// Table reference for coded fields (e.g., "0001" for Administrative Sex).
    /// </summary>
    public string? TableReference { get; init; }

    /// <summary>
    /// Example values for this element to help developers understand usage.
    /// </summary>
    public List<string> Examples { get; init; } = [];

    /// <summary>
    /// Vendor-specific variations and implementation notes.
    /// </summary>
    public List<VendorNote> VendorVariations { get; init; } = [];

    /// <summary>
    /// The healthcare standard this element belongs to (e.g., "hl7v23", "fhir-r4", "ncpdp").
    /// </summary>
    public string Standard { get; init; } = "";

    /// <summary>
    /// Version of the standard (e.g., "2.3", "R4", "2017071").
    /// </summary>
    public string Version { get; init; } = "";

    /// <summary>
    /// Parent element path for hierarchical elements (e.g., "PID.3" for "PID.3.5").
    /// </summary>
    public string? ParentPath { get; init; }

    /// <summary>
    /// Child element paths if this is a composite element.
    /// </summary>
    public List<string> ChildPaths { get; init; } = [];

    /// <summary>
    /// Valid values/codes for this element, if it's a coded field.
    /// </summary>
    public List<ValidValue> ValidValues { get; init; } = [];

    /// <summary>
    /// Cross-references to equivalent fields in other standards.
    /// </summary>
    public List<CrossReference> CrossReferences { get; init; } = [];
}

/// <summary>
/// Represents a vendor-specific note or variation for a standard element.
/// </summary>
public record VendorNote
{
    /// <summary>
    /// Vendor name (e.g., "Epic", "Cerner", "AllScripts").
    /// </summary>
    public string Vendor { get; init; } = "";

    /// <summary>
    /// Note describing the vendor-specific behavior or requirement.
    /// </summary>
    public string Note { get; init; } = "";

    /// <summary>
    /// Vendor-specific examples for this element.
    /// </summary>
    public List<string> Examples { get; init; } = [];
}

/// <summary>
/// Represents a valid value or code for a coded element.
/// </summary>
public record ValidValue
{
    /// <summary>
    /// The code value (e.g., "MR", "SS", "DL").
    /// </summary>
    public string Code { get; init; } = "";

    /// <summary>
    /// Description of what this code means.
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// Whether this value is deprecated or still in use.
    /// </summary>
    public bool IsDeprecated { get; init; } = false;
}

/// <summary>
/// Represents a cross-reference to equivalent fields in other standards.
/// </summary>
public record CrossReference
{
    /// <summary>
    /// The target standard (e.g., "fhir-r4", "hl7v23").
    /// </summary>
    public string Standard { get; init; } = "";

    /// <summary>
    /// The equivalent path in the target standard.
    /// </summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// Mapping relationship: Exact, Approximate, Partial, None.
    /// </summary>
    public string MappingType { get; init; } = "";

    /// <summary>
    /// Notes about the mapping relationship or differences.
    /// </summary>
    public string Notes { get; init; } = "";
}