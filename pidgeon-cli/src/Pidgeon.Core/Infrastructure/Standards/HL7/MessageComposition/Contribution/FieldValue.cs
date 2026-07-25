// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// One field's semantic value, contributed by an <see cref="IValueContributor"/>. A contributor
/// decides <em>content</em> and emits it as semantics, never as a pre-joined wire string. The
/// serializer renders it to wire shape using the version's data-type definition, so a coded
/// element becomes CE (3 components) at v2.3 through v2.5.1 and CWE at v2.6+ without the
/// contributor knowing the version.
/// </summary>
public abstract record FieldValue
{
    private FieldValue() { }

    /// <summary>A primitive scalar (NM, ST, ID, SI, TS/DTM, …). The serializer emits it verbatim.</summary>
    public sealed record Primitive(string Value) : FieldValue;

    /// <summary>
    /// A coded element (LOINC, ICD-10, NDC, …). Identifier/Text/CodingSystem map to components
    /// 1/2/3 of whatever coded data type the version's schema declares for the field (CE, CWE, CNE,
    /// CF, IS); the optional alternate triplet maps to 4/5/6. The serializer owns the CE↔CWE shape.
    /// </summary>
    public sealed record Coded(
        string Identifier,
        string? Text = null,
        string? CodingSystem = null,
        string? AlternateIdentifier = null,
        string? AlternateText = null,
        string? AlternateCodingSystem = null) : FieldValue;

    /// <summary>An intentionally empty field (a hole the contributor declares explicitly).</summary>
    public static readonly FieldValue Empty = new Primitive(string.Empty);
}
