// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;
using Pidgeon.Core.Services.FieldValueResolvers;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default <see cref="IHL7FieldComposer"/>: the field-value pipeline. Resolves one field at a
/// time: pins, optionality, composite expansion, and the primitive resolver chain. Each field
/// draws from its own coordinate off the segment key (<c>context.Key.Derive(field.Position)</c>),
/// so values are addressed by coordinate rather than RNG draw order.
/// </summary>
public class HL7FieldComposer : IHL7FieldComposer
{
    private readonly ILogger<HL7FieldComposer> _logger;
    private readonly IFieldValueResolverService _fieldValueResolverService;
    private readonly IEnumerable<ICompositeAwareResolver> _compositeAwareResolvers;
    private readonly IHL7FieldPinner _fieldPinner;
    private readonly IComponentImportanceClassifier _componentImportanceClassifier;
    private readonly CodedElementRenderer _codedElementRenderer;

    public HL7FieldComposer(
        ILogger<HL7FieldComposer> logger,
        IFieldValueResolverService fieldValueResolverService,
        IEnumerable<ICompositeAwareResolver> compositeAwareResolvers,
        IHL7FieldPinner fieldPinner,
        IComponentImportanceClassifier componentImportanceClassifier,
        CodedElementRenderer codedElementRenderer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fieldValueResolverService = fieldValueResolverService ?? throw new ArgumentNullException(nameof(fieldValueResolverService));
        _compositeAwareResolvers = compositeAwareResolvers ?? throw new ArgumentNullException(nameof(compositeAwareResolvers));
        _fieldPinner = fieldPinner ?? throw new ArgumentNullException(nameof(fieldPinner));
        _componentImportanceClassifier = componentImportanceClassifier ?? throw new ArgumentNullException(nameof(componentImportanceClassifier));
        _codedElementRenderer = codedElementRenderer ?? throw new ArgumentNullException(nameof(codedElementRenderer));
    }

    /// <summary>
    /// Generates field values using priority-based resolver chain.
    /// Handles composite data types (XAD, XPN, XTN, etc.) by expanding into components.
    /// Tries resolvers in order: Session context → HL7 semantics → Demographics → Random
    /// </summary>
    public async Task<string> ComposeFieldAsync(
        SegmentField field,
        SegmentGenerationContext context,
        GenerationOptions options)
    {
        // PINS workbench: pre-compute any pin overrides for (segment, field). Key 0
        // is a whole-field pin (PID.19), keys 1+ are per-component (PID.5.1).
        var fieldPins = _fieldPinner.GetPinsForField(context.SegmentCode, field.Position, options);

        // Whole-field pin short-circuits the resolver chain and the optional-drop.
        if (fieldPins.TryGetValue(0, out var wholeFieldPin))
        {
            return wholeFieldPin;
        }

        try
        {
            // Handle field optionality from schema
            // Cohort sequences populate every optional field so demographics stay
            // consistent across ADT → ORM → ORU messages sharing one Patient.
            // PINS: any component pin on this field also forces it present.
            // Withdrawn fields (optionality "W") were removed from the standard at this
            // version; the version-specific schema marks them. Emit nothing unless a pin
            // forces a value.
            if (field.Optionality == "W" && fieldPins.Count == 0)
            {
                return string.Empty;
            }

            if (field.Optionality == "O" && !options.IsCohortSequence && fieldPins.Count == 0 &&
                !IsClinicalContentPinnedField(context.SegmentCode, field.Position, options) &&
                context.Key.Derive(field.Position).Derive("present").AsRandom().NextDouble() > 0.7)
            {
                return string.Empty;
            }

            // Check if this field has a composite data type (XAD, XPN, XTN, CE, CWE, CX, etc.)
            var dataTypeResult = await context.DataTypeProvider!.GetDataTypeAsync(field.DataType);
            if (dataTypeResult.IsSuccess && dataTypeResult.Value.Components.Any())
            {
                // Check if any composite-aware resolver can handle this composite type
                // This ensures semantic coherence (e.g., CE components all refer to same code)
                var compositeResolvers = _compositeAwareResolvers
                    .Where(r => r.CanHandleComposite(field.DataType))
                    .OrderByDescending(r => r.Priority);

                foreach (var resolver in compositeResolvers)
                {
                    var compositeResolverContext = new FieldResolutionContext
                    {
                        SegmentCode = context.SegmentCode,
                        FieldPosition = field.Position,
                        Field = field,
                        GenerationContext = context,
                        Options = options,
                        FieldKey = context.Key.Derive(field.Position)
                    };

                    var compositeResult = await resolver.ResolveCompositeAsync(field, dataTypeResult.Value, compositeResolverContext);
                    if (compositeResult != null)
                    {
                        // Resolver handled it — render through the shared coded-element renderer so
                        // composite width (CE↔CWE) is decided in one place for both the field pipeline
                        // and the value-set serializer.
                        var rendered = _codedElementRenderer.RenderComponents(
                            compositeResult, dataTypeResult.Value.Components.Count);

                        // Per-field per-composite hot path: hottest log in the composer.
                        if (_logger.IsEnabled(LogLevel.Debug))
                            _logger.LogDebug("Composite {DataType} resolved by {Resolver} for field {Field}",
                                field.DataType, resolver.GetType().Name, field.Name);

                        return _fieldPinner.ApplyComponentPins(ApplyFieldLengthBudget(field, rendered), fieldPins);
                    }
                }

                // No composite-aware resolver handled it - fall back to component-by-component generation
                var componentValues = new List<string>();

                int componentIndex = 0;
                foreach (var component in dataTypeResult.Value.Components)
                {
                    var componentValue = await GenerateComponentValueAsync(component, field, context, options, componentIndex);
                    componentValues.Add(componentValue);
                    componentIndex++;
                }

                return _fieldPinner.ApplyComponentPins(
                    ApplyFieldLengthBudget(field, string.Join("^", componentValues)), fieldPins);
            }

            // Simple/primitive field - use resolver chain directly
            var resolverContext = new FieldResolutionContext
            {
                SegmentCode = context.SegmentCode,
                FieldPosition = field.Position,
                Field = field,
                GenerationContext = context,
                Options = options,
                FieldKey = context.Key.Derive(field.Position)
            };

            // Use resolver chain to determine field value
            var resolvedValue = await _fieldValueResolverService.ResolveFieldValueAsync(resolverContext);

            return _fieldPinner.ApplyComponentPins(ApplyFieldLengthBudget(field, resolvedValue), fieldPins);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error generating field {FieldName}", field.Name);
            return string.Empty;
        }
    }

    // Administrative Sex (PID-8) and Date/Time of Birth (PID-7) are the demographic anchors of a
    // real feed — populated on nearly every message. The generic 30% optional-field drop above
    // blanked each at a rate a clinical reviewer flags (b67 pinned the sex; a blank PID-7 blocks
    // age-dependent clinical review outright), so the clinical-content profile pins both present.
    // Value coherence is untouched: both still resolve from the Patient entity
    // (PatientDemographicResolver), so the pinned fields carry the patient's sex and birth date
    // rather than a blank.
    private static bool IsClinicalContentPinnedField(string segmentCode, int fieldPosition, GenerationOptions options)
        => options.RequireClinicalContent && segmentCode == "PID" && fieldPosition is 7 or 8;

    /// <summary>
    /// Resolves one component of a composite field. Optional components are kept or dropped by their
    /// importance tier (critical 95% / important 50% / optional 20%), then a pseudo-field carrying the
    /// component's type and table binding is resolved through the shared resolver chain.
    /// </summary>
    private async Task<string> GenerateComponentValueAsync(
        DataTypeComponent component,
        SegmentField parentField,
        SegmentGenerationContext context,
        GenerationOptions options,
        int componentIndex)
    {
        // Coordinate for this component: (segment-instance key) → field position → component index.
        // Presence and value draw from disjoint children so the keep/drop decision can't shift the value.
        var componentKey = context.Key.Derive(parentField.Position).Derive(componentIndex);

        // Handle optional components with 3-tier semantic probability
        if (component.Optionality == "O")
        {
            var importance = _componentImportanceClassifier.GetComponentImportance(component, parentField);
            var presenceRng = componentKey.Derive("present").AsRandom();

            var shouldSkip = importance switch
            {
                ComponentImportance.Critical => presenceRng.NextDouble() > 0.95,  // 95% populated
                ComponentImportance.Important => presenceRng.NextDouble() > 0.50, // 50% populated
                ComponentImportance.Optional => presenceRng.NextDouble() > 0.20,  // 20% populated
                _ => presenceRng.NextDouble() > 0.20
            };

            if (shouldSkip)
                return string.Empty;
        }

        // Create a pseudo-field from component for resolver chain
        var componentField = new SegmentField(
            Position: parentField.Position,  // Use parent field position for resolver context
            Name: component.Name,
            DataType: component.DataType,
            Optionality: component.Optionality,
            Repeatability: "-",
            Length: 0,
            TableId: component.TableId,
            Description: null);

        var resolverContext = new FieldResolutionContext
        {
            SegmentCode = context.SegmentCode,
            FieldPosition = parentField.Position,
            Field = componentField,
            GenerationContext = context,
            Options = options,
            FieldKey = componentKey,
            ParentFieldDataType = parentField.DataType
        };

        // Resolve through the chain so the table / demographic / identifier / contact resolvers
        // apply to the component just as they do to a whole field.
        return await _fieldValueResolverService.ResolveFieldValueAsync(resolverContext);
    }

    // Leaf datatypes whose value must NOT be blunt-truncated to fit a length budget: truncating a
    // coded value (ID/IS) can turn a real table code into a non-member (strict HL7-TABLE-001), and
    // truncating a date/time can produce a malformed timestamp (strict HL7-TS-001). These carry the
    // advisory HL7-LEN-001 warning intact rather than risk a strict error the generator must never
    // emit; free-text (ST/TX/FT) and numeric (NM/SI) leaves clamp safely.
    private static readonly HashSet<string> LengthUnsafeToTruncate = new(StringComparer.OrdinalIgnoreCase)
    {
        "ID", "IS", "DT", "TM", "TS", "DTM",
    };

    /// <summary>
    /// Holds a composed field value to the max length its derived spec publishes
    /// (<see cref="SegmentField.Length"/>) — the SAME length column the validator grades against
    /// (HL7-LEN-001), so the generator emits what the validator will accept instead of an
    /// over-length value a receiving system would truncate. Length 0 means the oracle publishes no
    /// bound, so nothing is done. The value is brought under budget honestly, never by mangling:
    /// (1) trailing empty components/subcomponents/repetitions are trimmed (canonical HL7);
    /// (2) a still-over composite drops whole trailing components until it fits — its structure
    /// stays valid, no component is cut mid-value; (3) a still-over leaf clamps only when clamping
    /// cannot invalidate it (see <see cref="LengthUnsafeToTruncate"/>).
    /// </summary>
    internal static string ApplyFieldLengthBudget(SegmentField field, string value)
    {
        if (field.Length <= 0 || string.IsNullOrEmpty(value) || value.Length <= field.Length)
        {
            return value;
        }

        var trimmed = value.TrimEnd('^', '&', '~');
        if (trimmed.Length <= field.Length)
        {
            return trimmed;
        }

        // Composite still over budget: drop whole optional trailing components until it fits.
        // Repetitions (~) are left alone — dropping across a repeat boundary is not structure-safe.
        if (trimmed.Contains('^') && !trimmed.Contains('~'))
        {
            var components = trimmed.Split('^');
            for (var count = components.Length - 1; count >= 1; count--)
            {
                var candidate = string.Join('^', components.Take(count)).TrimEnd('^', '&');
                if (candidate.Length <= field.Length)
                {
                    return candidate;
                }
            }

            // One component remains and is itself over budget — fall through to leaf handling on it.
            trimmed = components[0].TrimEnd('&');
            if (trimmed.Length <= field.Length)
            {
                return trimmed;
            }
        }

        if (trimmed.Contains('^'))
        {
            // A repeating/composite value we couldn't shorten further: keep it whole and let the
            // advisory length warning stand rather than cut across a component or repeat boundary.
            return trimmed;
        }

        if (LengthUnsafeToTruncate.Contains(field.DataType))
        {
            // A coded (ID/IS) or temporal (DT/TM/TS/DTM) leaf that still overflows can't be
            // truncated without risking a strict error (a non-member table code / malformed
            // timestamp). When the field is not required the honest emission is nothing — a
            // deprecated or mis-length slot like DG1-2 ("I10" in a length-2 field) holds no
            // conformant value; a required field keeps its value and carries the advisory warning.
            return field.Optionality == "R" ? trimmed : string.Empty;
        }

        var clamped = trimmed[..field.Length];
        if (string.Equals(field.DataType, "NM", StringComparison.OrdinalIgnoreCase))
        {
            // A numeric clamp must not end on a decimal point or sign (an invalid NM).
            clamped = clamped.TrimEnd('.', '+', '-');
        }

        return clamped;
    }
}
