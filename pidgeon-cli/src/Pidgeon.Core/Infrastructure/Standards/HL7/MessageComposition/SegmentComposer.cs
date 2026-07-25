// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Common;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.Contribution;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default <see cref="ISegmentComposer"/>: the per-segment composition. Resolves one segment
/// occurrence to its wire string by preferring a value contributor (OBX/RXE/DG1, content as
/// semantics, rendered version-correct by the serializer), then the MSH header composer, then
/// the schema-driven field pipeline; falling back to a minimal segment when neither a
/// contributor nor a schema is available.
/// </summary>
public class SegmentComposer : ISegmentComposer
{
    private readonly ILogger<SegmentComposer> _logger;
    private readonly IHL7FieldPinner _fieldPinner;
    private readonly IMshSegmentComposer _mshSegmentComposer;
    private readonly IHL7FieldComposer _fieldComposer;
    private readonly ValueContributorRegistry? _valueContributorRegistry;
    private readonly ICoherentValueSetSerializer? _valueSetSerializer;

    public SegmentComposer(
        ILogger<SegmentComposer> logger,
        IHL7FieldPinner fieldPinner,
        IMshSegmentComposer mshSegmentComposer,
        IHL7FieldComposer fieldComposer,
        ValueContributorRegistry? valueContributorRegistry = null,
        ICoherentValueSetSerializer? valueSetSerializer = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fieldPinner = fieldPinner ?? throw new ArgumentNullException(nameof(fieldPinner));
        _mshSegmentComposer = mshSegmentComposer ?? throw new ArgumentNullException(nameof(mshSegmentComposer));
        _fieldComposer = fieldComposer ?? throw new ArgumentNullException(nameof(fieldComposer));
        _valueContributorRegistry = valueContributorRegistry;
        _valueSetSerializer = valueSetSerializer;
    }

    /// <summary>
    /// Composes one segment using a value contributor when registered, the MSH composer for MSH,
    /// otherwise the schema-driven field pipeline; with a minimal-segment fallback when no schema is
    /// available. Pin overlays are applied to contributor output as a post-pass.
    /// </summary>
    public async Task<Result<string>> ComposeSegmentAsync(
        TriggerEventSegment segmentDef,
        SegmentGenerationContext context,
        GenerationOptions options,
        int setId = 1)
    {
        try
        {
            // Prefer a value contributor (owns content) + serializer (owns shape) when registered.
            // The contributor returns semantics; the serializer renders them version-correct
            // against the segment schema (CE/CWE width, withdrawn-field suppression, field count).
            // Pins are applied as a post-pass.
            var contributor = _valueContributorRegistry?.GetContributor(segmentDef.SegmentCode);
            if (contributor != null && _valueSetSerializer != null)
            {
                var contributorSchema = await context.SegmentProvider!.GetSegmentAsync(segmentDef.SegmentCode);
                if (contributorSchema.IsSuccess)
                {
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("Using value contributor {Contributor} for {SegmentCode}",
                            contributor.GetType().Name, segmentDef.SegmentCode);
                    var contributorContext = context with
                    {
                        SegmentCode = segmentDef.SegmentCode,
                        SetId = setId,
                        GroupPath = segmentDef.GroupPath,
                        Key = context.Key.Derive(segmentDef.SegmentCode).Derive(setId)
                    };
                    var valueSet = await contributor.ContributeAsync(contributorContext, options, setId);
                    var line = await _valueSetSerializer.SerializeAsync(
                        valueSet, contributorSchema.Value, contributorContext, options);
                    return Result<string>.Success(
                        _fieldPinner.ApplySegmentLevelPins(line, segmentDef.SegmentCode, options));
                }
            }

            // Load segment schema at the context's HL7 version (PV1/OBR/etc. retype 2.3→2.7)
            var segmentSchemaResult = await context.SegmentProvider!.GetSegmentAsync(segmentDef.SegmentCode);
            if (segmentSchemaResult.IsFailure)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("No schema found for {SegmentCode}, generating minimal segment",
                        segmentDef.SegmentCode);
                return GenerateMinimalSegment(segmentDef.SegmentCode);
            }

            // Update context with current segment code + the segment-instance entropy coordinate
            // (setId distinguishes repeated occurrences so their field draws don't collide).
            var segmentContext = context with
            {
                SegmentCode = segmentDef.SegmentCode,
                SetId = setId,
                GroupPath = segmentDef.GroupPath,
                Key = context.Key.Derive(segmentDef.SegmentCode).Derive(setId)
            };
            return await GenerateSegmentFromSchemaDefinition(segmentSchemaResult.Value, segmentContext, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating segment {SegmentCode}", segmentDef.SegmentCode);
            return GenerateMinimalSegment(segmentDef.SegmentCode);
        }
    }

    /// <summary>
    /// Generates segment using complete schema definition from segments/*.json.
    /// Special handling for MSH segment which has unique HL7 structure.
    /// </summary>
    private async Task<Result<string>> GenerateSegmentFromSchemaDefinition(
        SegmentSchema segmentSchema,
        SegmentGenerationContext context,
        GenerationOptions options)
    {
        // Special handling for MSH segment - unique HL7 structure. MSH-1..12 are
        // resolved by the dedicated composer from MshHeaderDefaults + the per-message
        // clock/RNG; MSH-13+ run the standard schema-driven field path below.
        if (segmentSchema.Code == "MSH")
        {
            return await _mshSegmentComposer.ComposeAsync(
                segmentSchema, context, options,
                field => _fieldComposer.ComposeFieldAsync(field, context, options));
        }

        var fieldValues = new List<string> { segmentSchema.Code };

        foreach (var field in segmentSchema.Fields)
        {
            var fieldValue = await _fieldComposer.ComposeFieldAsync(field, context, options);
            fieldValues.Add(fieldValue);
        }

        return Result<string>.Success(string.Join("|", fieldValues));
    }

    /// <summary>
    /// Generates minimal segment when schema/builder unavailable.
    /// Shows segment code to help identify missing schemas during development.
    /// </summary>
    private static Result<string> GenerateMinimalSegment(string segmentCode)
    {
        return Result<string>.Success(segmentCode);
    }
}
