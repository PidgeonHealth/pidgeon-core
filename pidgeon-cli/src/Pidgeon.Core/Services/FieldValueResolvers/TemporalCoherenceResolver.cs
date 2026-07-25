// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Resolves timestamp fields with temporal coherence, ensuring realistic time relationships:
/// - MSH.7 (Message Date/Time) is the primary anchor, generated first by HL7MessageComposer
/// - EVN.2 (Event Occurred) within 0-5 minutes of MSH.7
/// - PV1.44 (Admit Date/Time) within 0-5 minutes of EVN.2
/// - PV1.45 (Discharge Date/Time) 1-72 hours after PV1.44
/// - DG1.5 (Diagnosis Date/Time) within 0-48 hours after EVN.2
/// - OBX.14 (Observation Date/Time) within 0-24 hours after EVN.2
/// - OBR.7 (Collection Date/Time) within 0-2 hours after EVN.2
/// - OBR.22 (Results Date/Time) 1-8 hours after OBR.7
/// - ORC.9 (Order Date/Time) within 0-24 hours after EVN.2
///
/// Priority: 92 (higher than HL7SpecificFieldResolver at 90 to intercept timestamps)
/// Implements both IFieldValueResolver and ICompositeAwareResolver because TS is defined as composite.
/// </summary>
public class TemporalCoherenceResolver : IFieldValueResolver, ICompositeAwareResolver
{
    private readonly ILogger<TemporalCoherenceResolver> _logger;

    // Bound to the per-message deterministic RNG / clock in GenerateTimestampForField
    // (the single path both entry points funnel through) so temporal jitter and the
    // MSH.7 anchor are reproducible under a seed. Safe because this resolver is Scoped and
    // generation is sequential within a scope; revisit (thread RNG/clock as parameters)
    // before making it Singleton or running resolvers in parallel.
    private Random _random = Random.Shared;
    private DateTime _clock = DateTime.Now;

    public int Priority => 92;

    /// <summary>
    /// Handles TS (Timestamp) composite type for temporal coherence.
    /// </summary>
    public bool CanHandleComposite(string dataTypeCode) => dataTypeCode == "TS" || dataTypeCode == "DTM";

    /// <summary>
    /// Defines temporal relationships between HL7 fields.
    /// Maps target field → (anchor field, min delta, max delta).
    /// MSH.7 is the primary anchor generated first; EVN.2 is relative to MSH.7.
    /// All other timestamps are relative to EVN.2.
    /// </summary>
    private static readonly Dictionary<string, (string AnchorField, TimeSpan MinDelta, TimeSpan MaxDelta)> TemporalRelationships = new()
    {
        // EVN.2 (Event Occurred) is relative to MSH.7 (message sent) within a few minutes
        ["EVN.2"] = ("MSH.7", TimeSpan.Zero, TimeSpan.FromMinutes(5)),

        // PV1.44 (Admit Date/Time) should match EVN.2 (Event Occurred) within a few minutes
        ["PV1.44"] = ("EVN.2", TimeSpan.Zero, TimeSpan.FromMinutes(5)),

        // PV1.45 (Discharge Date/Time) should be 1-72 hours after admission
        ["PV1.45"] = ("PV1.44", TimeSpan.FromHours(1), TimeSpan.FromHours(72)),

        // DG1.5 (Diagnosis Date/Time) should be within 0-48 hours after event
        ["DG1.5"] = ("EVN.2", TimeSpan.Zero, TimeSpan.FromHours(48)),

        // OBX.14 (Observation Date/Time) should be within 0-24 hours after event
        ["OBX.14"] = ("EVN.2", TimeSpan.Zero, TimeSpan.FromHours(24)),

        // ORC.9 (Order Date/Time) should be within encounter timespan
        ["ORC.9"] = ("EVN.2", TimeSpan.Zero, TimeSpan.FromHours(24)),

        // OBR.7 (Observation Date/Time) - specimen collection near event time
        ["OBR.7"] = ("EVN.2", TimeSpan.Zero, TimeSpan.FromHours(2)),

        // OBR.22 (Results Report Date/Time) - after specimen collection
        ["OBR.22"] = ("OBR.7", TimeSpan.FromHours(1), TimeSpan.FromHours(8)),

        // RXE.32 (Original Order Date/Time) should match or precede current order
        ["RXE.32"] = ("ORC.9", TimeSpan.FromHours(-24), TimeSpan.Zero),
    };

    public TemporalCoherenceResolver(ILogger<TemporalCoherenceResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles TS/DTM composite types (called for composite data types like TS).
    /// Returns timestamp as a single-component dictionary.
    /// </summary>
    public Task<Dictionary<int, string>?> ResolveCompositeAsync(
        SegmentField parentField,
        DataType dataType,
        FieldResolutionContext context)
    {
        // Build field path (e.g., "EVN.2", "PV1.44")
        var fieldPath = $"{context.SegmentCode}.{context.FieldPosition}";

        // Generate the timestamp
        var timestamp = GenerateTimestampForField(fieldPath, context);

        if (timestamp == null)
            return Task.FromResult<Dictionary<int, string>?>(null);

        // Return as single-component dictionary (TS.1 = the timestamp value)
        var result = new Dictionary<int, string>
        {
            { 1, timestamp }
        };

        return Task.FromResult<Dictionary<int, string>?>(result);
    }

    /// <summary>
    /// Handles TS/DTM simple fields (fallback for non-composite TS fields).
    /// </summary>
    public Task<string?> ResolveAsync(FieldResolutionContext context)
    {
        // Only handle timestamp fields (TS or DTM data types)
        if (context.Field.DataType != "TS" && context.Field.DataType != "DTM")
            return Task.FromResult<string?>(null);

        // Build field path (e.g., "EVN.2", "PV1.44")
        var fieldPath = $"{context.SegmentCode}.{context.FieldPosition}";

        var timestamp = GenerateTimestampForField(fieldPath, context);
        return Task.FromResult<string?>(timestamp);
    }

    /// <summary>
    /// Core timestamp generation logic used by both composite and simple field paths.
    /// </summary>
    private string? GenerateTimestampForField(string fieldPath, FieldResolutionContext context)
    {
        _random = context.FieldRng();
        _clock = context.Clock();

        // Check if this field has a temporal relationship
        if (!TemporalRelationships.TryGetValue(fieldPath, out var relationship))
        {
            // No temporal relationship - check if this is the primary anchor (MSH.7)
            if (fieldPath == "MSH.7")
            {
                return GenerateAndRecordAnchorTimestamp(fieldPath, context);
            }

            // Not a known temporal field - let it fall through to HL7SpecificFieldResolver
            return null;
        }

        // Try to get the anchor timestamp
        var anchorTime = GetAnchorTimestamp(relationship.AnchorField, context);

        if (!anchorTime.HasValue)
        {
            _logger.LogDebug("Temporal anchor {AnchorField} not yet generated for {FieldPath}, falling through",
                relationship.AnchorField, fieldPath);

            // Anchor not available yet - fall through to default timestamp generation
            return null;
        }

        // Generate timestamp relative to anchor
        var relativeTime = GenerateRelativeTimestamp(anchorTime.Value, relationship.MinDelta, relationship.MaxDelta);
        var formattedTimestamp = FormatHL7Timestamp(relativeTime, context.Field.DataType);

        // Record this timestamp for future references
        RecordTimestamp(fieldPath, relativeTime, context);

        _logger.LogDebug("Generated temporal timestamp for {FieldPath}: {Timestamp} (relative to {AnchorField})",
            fieldPath, formattedTimestamp, relationship.AnchorField);

        return formattedTimestamp;
    }

    /// <summary>
    /// Generates and records MSH.7 as the primary temporal anchor.
    /// All other timestamps flow relative to this base time.
    /// </summary>
    private string GenerateAndRecordAnchorTimestamp(string fieldPath, FieldResolutionContext context)
    {
        // Check if anchor already generated in this message
        if (context.GenerationContext.GeneratedTimestamps.TryGetValue(fieldPath, out var existingTime))
        {
            return FormatHL7Timestamp(existingTime, context.Field.DataType);
        }

        // Generate new anchor timestamp (recent past - within last 7 days)
        var daysAgo = _random.Next(0, 7);
        var hoursAgo = _random.Next(0, 24);
        var minutesAgo = _random.Next(0, 60);

        var anchorTime = _clock
            .AddDays(-daysAgo)
            .AddHours(-hoursAgo)
            .AddMinutes(-minutesAgo);

        // Record the anchor timestamp (Dictionary is mutable reference type, so this persists)
        RecordTimestamp(fieldPath, anchorTime, context);

        _logger.LogDebug("Generated encounter anchor timestamp {FieldPath}: {Timestamp}",
            fieldPath, FormatHL7Timestamp(anchorTime, context.Field.DataType));

        return FormatHL7Timestamp(anchorTime, context.Field.DataType);
    }

    /// <summary>
    /// Gets the anchor timestamp for a field from the generation context. When the requested
    /// anchor is EVN.2 — the root of the clinical chain, which only exists in ADT-family
    /// messages — and it has not been generated (ORU / ORM / RDE carry no EVN segment), derive
    /// the encounter anchor and record it under EVN.2 so the rest of the chain (and the
    /// OBR.22 → OBR.7 cascade) resolves coherently instead of every field collapsing to the
    /// bare message clock.
    /// </summary>
    private DateTime? GetAnchorTimestamp(string anchorFieldPath, FieldResolutionContext context)
    {
        // Check if the anchor timestamp has been generated yet
        if (context.GenerationContext.GeneratedTimestamps.TryGetValue(anchorFieldPath, out var timestamp))
            return timestamp;

        if (anchorFieldPath == "EVN.2")
        {
            var encounterAnchor = GetEncounterAnchor(context);
            context.GenerationContext.GeneratedTimestamps["EVN.2"] = encounterAnchor;
            return encounterAnchor;
        }

        return null;
    }

    /// <summary>
    /// The encounter event time that roots the clinical timeline for messages without an EVN
    /// segment. Roots on the message time (MSH.7, which the composer records first from the
    /// seeded clock); falls back to the per-message clock. Both are seed-deterministic.
    /// <see cref="Encounter.StartTime"/> is intentionally NOT used: it is wall-clock-derived,
    /// not seeded, so anchoring to it would desync the timeline from the rest of the message
    /// and break reproducible generation.
    /// </summary>
    private DateTime GetEncounterAnchor(FieldResolutionContext context)
    {
        if (context.GenerationContext.GeneratedTimestamps.TryGetValue("MSH.7", out var messageTime))
            return messageTime;

        return _clock;
    }

    /// <summary>
    /// Generates a timestamp relative to an anchor with realistic variation.
    /// </summary>
    private DateTime GenerateRelativeTimestamp(DateTime anchor, TimeSpan minDelta, TimeSpan maxDelta)
    {
        // Calculate random delta within the specified range
        var deltaRange = maxDelta - minDelta;
        var randomDelta = TimeSpan.FromSeconds(_random.NextDouble() * deltaRange.TotalSeconds);
        var actualDelta = minDelta + randomDelta;

        return anchor.Add(actualDelta);
    }

    /// <summary>
    /// Records a timestamp in the context for future field references.
    /// </summary>
    private void RecordTimestamp(string fieldPath, DateTime timestamp, FieldResolutionContext context)
    {
        context.GenerationContext.GeneratedTimestamps[fieldPath] = timestamp;
    }

    /// <summary>
    /// Formats a DateTime as an HL7 timestamp (TS or DTM).
    /// </summary>
    private string FormatHL7Timestamp(DateTime timestamp, string dataType)
    {
        // HL7 v2.3 timestamp format: YYYYMMDDHHMMSS
        // DTM is also in the same format
        return timestamp.ToString("yyyyMMddHHmmss");
    }
}
