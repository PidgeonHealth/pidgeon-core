// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

/// <summary>
/// Default <see cref="ICoherentValueSetSerializer"/>. Walks the version's segment schema and renders
/// each contributed value to its version-correct wire shape — coded elements via
/// <see cref="CodedElementRenderer"/> against the field's data type (CE 3-component triplet at
/// v2.3–2.5.1, the wider CWE at v2.6+), withdrawn fields ("W") suppressed, every other schema field
/// empty. Mirrors the field pipeline's full-schema emission, so a contributed segment carries the
/// same field count and component width as one rendered field-by-field.
/// </summary>
public class CoherentValueSetSerializer : ICoherentValueSetSerializer
{
    private readonly CodedElementRenderer _codedElementRenderer;
    private readonly IHL7FieldPinner _fieldPinner;

    public CoherentValueSetSerializer(
        CodedElementRenderer codedElementRenderer,
        IHL7FieldPinner fieldPinner)
    {
        _codedElementRenderer = codedElementRenderer ?? throw new ArgumentNullException(nameof(codedElementRenderer));
        _fieldPinner = fieldPinner ?? throw new ArgumentNullException(nameof(fieldPinner));
    }

    public async Task<string> SerializeAsync(
        CoherentValueSet valueSet,
        SegmentSchema schema,
        SegmentGenerationContext context,
        GenerationOptions options)
    {
        var fieldByPosition = schema.Fields.ToDictionary(f => f.Position);

        // Collapse the present groups into one position → value map. One present/absent decision per
        // group (never per field) keeps a coherent tuple whole-or-absent. The decision draws from the
        // group's own coordinate off the segment key, so it is a pure function of (seed,
        // segment, set-id, group) and stays byte-stable regardless of draw order.
        var values = new Dictionary<int, FieldValue>();
        foreach (var group in valueSet.Groups)
        {
            if (!IsGroupPresent(group, fieldByPosition, schema.Code, context, options))
                continue;
            foreach (var (position, value) in group.Values)
                values[position] = value;
        }

        var maxPosition = schema.Fields.Count == 0 ? 0 : schema.Fields.Max(f => f.Position);
        var parts = new List<string>(maxPosition + 1) { schema.Code };

        for (var position = 1; position <= maxPosition; position++)
        {
            if (!fieldByPosition.TryGetValue(position, out var field))
            {
                parts.Add(string.Empty);
                continue;
            }

            // Withdrawn at this version (e.g. DG1-2 / DG1-4 at v2.6+): emit nothing, regardless of
            // what the contributor supplied. The coding system now travels in the CWE component.
            if (field.Optionality == "W")
            {
                parts.Add(string.Empty);
                continue;
            }

            parts.Add(values.TryGetValue(position, out var fieldValue)
                ? await RenderAsync(fieldValue, field, context)
                : string.Empty);
        }

        return string.Join("|", parts);
    }

    private bool IsGroupPresent(
        AtomicGroup group,
        IReadOnlyDictionary<int, SegmentField> fieldByPosition,
        string segmentCode,
        SegmentGenerationContext context,
        GenerationOptions options)
    {
        foreach (var position in group.Values.Keys)
        {
            if (fieldByPosition.TryGetValue(position, out var field) && field.Optionality == "R")
                return true;
            if (_fieldPinner.GetPinsForField(segmentCode, position, options).Count > 0)
                return true;
        }

        // Cohort sequences populate optional content so demographics/clinical detail stay consistent
        // across the ADT → ORM → ORU messages sharing one patient.
        if (options.IsCohortSequence)
            return true;

        // A purely-optional group: one inclusion draw for the whole tuple, matching the field
        // pipeline's per-field optional threshold (keep when <= 0.7). Keyed by the group's lowest
        // field position, a stable per-group anchor: every contributor today emits a single group
        // (SingleGroup), and any multi-group contributor must give its groups distinct minima so
        // they decide independently.
        return context.Key.Derive("optional-group").Derive(group.Values.Keys.Min()).AsRandom().NextDouble() <= 0.7;
    }

    private async Task<string> RenderAsync(FieldValue value, SegmentField field, SegmentGenerationContext context)
    {
        // A contributed value is held to the field's derived-spec max length the same way the
        // field pipeline holds a composed one — the serializer is the choke point every contributor
        // passes through, so a legacy slot the contributor overfills (DG1-2 "I10" in a length-2
        // field, a diagnosis description longer than DG1-4's 40) is length-corrected here rather
        // than leaking an HL7-LEN-001 warning the validator would then raise against the generator.
        switch (value)
        {
            case FieldValue.Primitive primitive:
                return HL7FieldComposer.ApplyFieldLengthBudget(field, primitive.Value);

            case FieldValue.Coded coded:
                if (context.DataTypeProvider is not null)
                {
                    var dataTypeResult = await context.DataTypeProvider.GetDataTypeAsync(field.DataType);
                    if (dataTypeResult.IsSuccess && dataTypeResult.Value.Components.Any())
                        return HL7FieldComposer.ApplyFieldLengthBudget(
                            field, _codedElementRenderer.Render(coded, dataTypeResult.Value));
                }

                // No version data type available (non-composer callers): render the supplied
                // components with no schema padding so the value is never lost.
                return HL7FieldComposer.ApplyFieldLengthBudget(field, RenderCodedWithoutSchema(coded));

            default:
                return string.Empty;
        }
    }

    private string RenderCodedWithoutSchema(FieldValue.Coded coded)
    {
        var components = new Dictionary<int, string> { [1] = coded.Identifier };
        if (coded.Text is not null) components[2] = coded.Text;
        if (coded.CodingSystem is not null) components[3] = coded.CodingSystem;
        if (coded.AlternateIdentifier is not null) components[4] = coded.AlternateIdentifier;
        if (coded.AlternateText is not null) components[5] = coded.AlternateText;
        if (coded.AlternateCodingSystem is not null) components[6] = coded.AlternateCodingSystem;
        return _codedElementRenderer.RenderComponents(components, components.Count);
    }
}
