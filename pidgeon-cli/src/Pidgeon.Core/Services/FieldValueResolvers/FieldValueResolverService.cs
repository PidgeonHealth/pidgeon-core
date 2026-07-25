// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Orchestrates field value resolution using priority-based resolver chain.
/// Follows chain of responsibility pattern for clean separation of concerns.
/// </summary>
public class FieldValueResolverService : IFieldValueResolverService
{
    private readonly ILogger<FieldValueResolverService> _logger;
    private readonly IEnumerable<IFieldValueResolver> _resolvers;

    public FieldValueResolverService(
        ILogger<FieldValueResolverService> logger,
        IEnumerable<IFieldValueResolver> resolvers)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resolvers = resolvers ?? throw new ArgumentNullException(nameof(resolvers));
    }

    /// <summary>
    /// Resolve field value using registered resolvers in priority order.
    /// Tries each resolver until one returns a non-null value.
    /// </summary>
    public async Task<string> ResolveFieldValueAsync(FieldResolutionContext context)
    {
        try
        {
            _logger.LogDebug("Resolving field value for {SegmentCode}.{FieldPosition} ({FieldName})",
                context.SegmentCode, context.FieldPosition, context.Field.Name);

            // Try resolvers in priority order (highest priority first)
            var sortedResolvers = _resolvers.OrderByDescending(r => r.Priority);

            foreach (var resolver in sortedResolvers)
            {
                try
                {
                    var result = await resolver.ResolveAsync(context);
                    if (result != null)
                    {
                        if (!IsAcceptableForDataType(context.Field.DataType, result))
                        {
                            _logger.LogDebug(
                                "Rejected value from {ResolverType} for datetime-typed field {SegmentCode}.{FieldPosition}",
                                resolver.GetType().Name, context.SegmentCode, context.FieldPosition);
                            continue;
                        }

                        _logger.LogDebug(
                            "Field {SegmentCode}.{FieldPosition} resolved by {ResolverType}",
                            context.SegmentCode, context.FieldPosition, resolver.GetType().Name);
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Resolver {ResolverType} failed for field {SegmentCode}.{FieldPosition}",
                        resolver.GetType().Name, context.SegmentCode, context.FieldPosition);
                    // Continue to next resolver
                }
            }

            // No resolver succeeded, return empty string as safe fallback
            _logger.LogDebug("No resolver found value for field {SegmentCode}.{FieldPosition}, using empty string",
                context.SegmentCode, context.FieldPosition);
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving field value for {SegmentCode}.{FieldPosition}",
                context.SegmentCode, context.FieldPosition);
            return string.Empty;
        }
    }

    // A datetime-typed field must never carry a non-datetime value. Keyword/coded resolvers can
    // otherwise claim TS/DTM/DT/TM fields by name-matching (e.g. a lab resolver matching
    // "Observation End Date/Time" on the word "observation" and stamping a LOINC into OBR-8, or
    // event codes landing in EVN-6). The strict validator's datetime rules were dead at 2.6+
    // until they were extended to DTM/DT/TM, which masked exactly this class on the wire.
    // Guarding at the chain's single return point fixes every resolver at once; resolvers that
    // legitimately produce timestamps pass through unchanged. Shapes mirror the validator's
    // per-type patterns in HL7FieldRuleValidator so generation and validation cannot drift.
    private static readonly System.Text.RegularExpressions.Regex TimestampShape = new(
        @"^\d{4}(\d{2}(\d{2}(\d{2}(\d{2}(\d{2}(\.\d{1,4})?)?)?)?)?)?([+-]\d{4})?$",
        System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex DateShape = new(
        @"^\d{4}(\d{2}(\d{2})?)?$",
        System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex TimeShape = new(
        @"^\d{2}(\d{2}(\d{2}(\.\d{1,4})?)?)?([+-]\d{4})?$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool IsAcceptableForDataType(string? dataType, string value)
    {
        if (string.IsNullOrEmpty(value))
            return true;

        // Grade the first component only (e.g. "20240115143022^D" carries a precision component).
        var first = value.Split('^')[0].Trim();
        if (string.IsNullOrEmpty(first))
            return true;

        return dataType switch
        {
            "TS" or "DTM" => TimestampShape.IsMatch(first),
            "DT" => DateShape.IsMatch(first),
            "TM" => TimeShape.IsMatch(first),
            _ => true
        };
    }
}
