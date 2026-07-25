// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// The single CE/CWE/coded-element renderer: the serializer owns shape. Maps component-positioned
/// values onto the version's data-type component list and joins with '^'. Both the field-value
/// pipeline (<see cref="HL7FieldComposer"/>) and the value-set serializer route composite
/// rendering through here, so there is exactly one place that decides CE (3 components) vs CWE
/// (more) shape. Version-correctness falls out of the component list the data-type provider
/// returns for the requested version.
/// </summary>
public class CodedElementRenderer
{
    /// <summary>
    /// Joins a component-position → value map into a '^'-delimited composite string. Renders at least
    /// <paramref name="schemaComponentCount"/> components (the version's data-type width), extending to
    /// the highest supplied position when a resolver carries more than the schema declares. Gaps are
    /// empty. The field pipeline and the value-set serializer share this join, so CE/CWE width is
    /// decided in one place.
    /// </summary>
    public string RenderComponents(IReadOnlyDictionary<int, string> components, int schemaComponentCount)
    {
        var maxComponentPosition = components.Keys.Any()
            ? Math.Max(schemaComponentCount, components.Keys.Max())
            : schemaComponentCount;

        var resolved = new List<string>(maxComponentPosition);
        for (int i = 1; i <= maxComponentPosition; i++)
            resolved.Add(components.TryGetValue(i, out var value) ? value : string.Empty);

        return string.Join("^", resolved);
    }

    /// <summary>
    /// Renders a semantic <see cref="FieldValue.Coded"/> against <paramref name="dataType"/>: the
    /// identifier/text/coding-system triplet maps to components 1/2/3 and the alternate triplet to
    /// 4/5/6, then the width of the version's data type governs the emitted component count.
    /// </summary>
    public string Render(FieldValue.Coded coded, DataType dataType)
    {
        var components = new Dictionary<int, string> { [1] = coded.Identifier };
        if (coded.Text is not null) components[2] = coded.Text;
        if (coded.CodingSystem is not null) components[3] = coded.CodingSystem;
        if (coded.AlternateIdentifier is not null) components[4] = coded.AlternateIdentifier;
        if (coded.AlternateText is not null) components[5] = coded.AlternateText;
        if (coded.AlternateCodingSystem is not null) components[6] = coded.AlternateCodingSystem;

        return RenderComponents(components, dataType.Components.Count);
    }
}
