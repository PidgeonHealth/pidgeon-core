// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Lightweight StructureDefinition model parsed from FHIR JSON.
/// Contains only the fields needed for validation: snapshot elements with
/// path, min, max, type, binding, fixed/pattern values.
/// </summary>
public class FHIRStructureDefinition
{
    public string Url { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? BaseDefinition { get; set; }

    /// <summary>
    /// The StructureDefinition's business <c>version</c> (e.g. <c>3.1.1</c>).
    /// Two IG releases publish the same canonical URL at different versions
    /// (US Core 3.1.1 beside 6.1), so the loader indexes version-pinned
    /// canonicals (<c>url|version</c>) beside the bare URL, and the IG version
    /// stamp reads this to report which release actually backed a validation.
    /// Null when the source JSON carries no version.
    /// </summary>
    public string? Version { get; set; }
    public IReadOnlyList<FHIRElementDefinition> Elements { get; set; } = Array.Empty<FHIRElementDefinition>();

    /// <summary>
    /// Where this profile was loaded from. Distinguishes a hand-trimmed
    /// embedded subset stub (shipped for offline canonical-URL resolution)
    /// from a full profile delivered by an installed IG package or a
    /// user-supplied file. A conformance run reads this to flag when it
    /// validated against a stub rather than the real IG, so an archival
    /// scorecard never presents subset-stub validation as full-IG
    /// conformance (FABLE_CONFORM_AUDIT C13). Defaults to
    /// <see cref="StructureDefinitionOrigin.Package"/> so a hand-built
    /// profile is never mistaken for a stub; the loader sets the true
    /// origin at every load site.
    /// </summary>
    public StructureDefinitionOrigin Origin { get; set; } = StructureDefinitionOrigin.Package;
}

/// <summary>
/// Provenance of a loaded <see cref="FHIRStructureDefinition"/>: an embedded
/// subset stub, a profile from an installed IG package, or a user-supplied file.
/// </summary>
public enum StructureDefinitionOrigin
{
    /// <summary>A hand-trimmed subset stub compiled into the binary for
    /// offline canonical-URL resolution — not the full published IG profile.</summary>
    Embedded,

    /// <summary>A profile parsed from an installed IG data package directory.</summary>
    Package,

    /// <summary>A profile parsed from a user-supplied file (e.g. <c>--profile ./x.json</c>).</summary>
    File,
}

/// <summary>
/// A single element constraint from a StructureDefinition snapshot.
/// </summary>
public class FHIRElementDefinition
{
    /// <summary>
    /// The snapshot element's <c>id</c>. Unlike <see cref="Path"/> it carries slice
    /// context: <c>Claim.item.extension:nursingHome.url</c> constrains only the
    /// instances that slice claims, while its path is the plain
    /// <c>Claim.item.extension.url</c>. The main element walk skips any definition
    /// whose id names a slice; <see cref="SliceScopeValidator"/> is the one place
    /// those definitions execute.
    /// </summary>
    public string? Id { get; set; }

    public string Path { get; set; } = string.Empty;
    public int Min { get; set; }
    public string Max { get; set; } = "*";
    public IReadOnlyList<FHIRElementType> Types { get; set; } = Array.Empty<FHIRElementType>();
    public FHIRElementBinding? Binding { get; set; }
    public JsonElement? FixedValue { get; set; }
    public JsonElement? PatternValue { get; set; }
    public FHIRSlicingDefinition? Slicing { get; set; }
    public string? SliceName { get; set; }
    public bool IsSliceEntry => Slicing != null;

    /// <summary>
    /// FHIR <c>mustSupport</c> flag. When a profile marks an element
    /// must-support, a conformant system SHOULD be capable of populating
    /// the element when source data is available — distinct from
    /// <see cref="Min"/>, which says the element MUST be populated. The
    /// validator treats a missing must-support element as a
    /// <see cref="FHIRDiagnosticSeverity.Warning"/>, not an error, so
    /// operators can distinguish structural conformance (no red diagnostics)
    /// from IG compliance (no yellow diagnostics either).
    /// </summary>
    public bool MustSupport { get; set; }

    /// <summary>FHIRPath invariants declared on this element (see <see cref="FHIRConstraint"/>).</summary>
    public IReadOnlyList<FHIRConstraint> Constraints { get; set; } = Array.Empty<FHIRConstraint>();
}

/// <summary>
/// A type constraint on an element (e.g., HumanName, Reference, code).
/// </summary>
public class FHIRElementType
{
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Profiles the type must conform to. For an element typed as <c>Extension</c>
    /// this carries the extension's canonical URL (e.g., <c>us-core-race</c>),
    /// which is how extension slices are identified on the wire. For other
    /// complex types it typically restricts the element to a specific profile.
    /// Separate from <see cref="TargetProfile"/>, which applies to Reference
    /// targets only.
    /// </summary>
    public IReadOnlyList<string> Profile { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> TargetProfile { get; set; } = Array.Empty<string>();
}

/// <summary>
/// A terminology binding on an element.
/// </summary>
public class FHIRElementBinding
{
    public string Strength { get; set; } = string.Empty;
    public string? ValueSet { get; set; }
}

/// <summary>
/// Slicing definition for an element that can be sliced.
/// </summary>
public class FHIRSlicingDefinition
{
    public IReadOnlyList<FHIRSlicingDiscriminator> Discriminator { get; set; } = Array.Empty<FHIRSlicingDiscriminator>();
    public string Rules { get; set; } = "open";
}

/// <summary>
/// A discriminator that identifies which slice an element belongs to.
/// </summary>
public class FHIRSlicingDiscriminator
{
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Lightweight CodeSystem model: canonical URL plus the flattened concept code
/// list. Enumerable content only — a CodeSystem published without concepts
/// (content: not-present) parses to an empty list, which consumers treat as
/// non-enumerable rather than empty-and-decided.
/// </summary>
public class FHIRCodeSystem
{
    public string Url { get; set; } = string.Empty;
    public IReadOnlyList<string> Codes { get; set; } = Array.Empty<string>();
}
