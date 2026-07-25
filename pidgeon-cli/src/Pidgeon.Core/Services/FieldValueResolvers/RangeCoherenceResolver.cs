// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Resolves range composite types (CQ, CM_RANGE, DR, CM_ABS_RANGE) with semantic coherence:
/// - CQ: Quantity with appropriate units (e.g., 500^mg, 10^mL)
/// - CM_RANGE: Low value < high value
/// - DR: Start date/time < end date/time
/// - CM_ABS_RANGE: Contains coherent CM_RANGE + change values
///
/// Priority: 76 (after IdentifierCoherenceResolver at 78, before Demographic at 80)
/// </summary>
public class RangeCoherenceResolver : ICompositeAwareResolver
{
    private readonly ILogger<RangeCoherenceResolver> _logger;

    // Bound to the per-message deterministic RNG / clock at the start of each
    // ResolveCompositeAsync so the range helpers draw from the shared sequence.
    private Random _random = Random.Shared;
    private DateTime _clock = DateTime.Now;

    public int Priority => 76;

    // Common units for medication dosing (CQ in RXE.19)
    private static readonly string[] MedicationUnits = new[]
    {
        "mg", "g", "mcg", "mL", "L", "tablets", "capsules", "units", "mg/kg", "mEq"
    };

    // Common units for lab specimen volume (CQ in OBR.9, OM4)
    private static readonly string[] VolumeUnits = new[]
    {
        "mL", "L", "cc", "drops"
    };

    // Common units for time durations (CQ in PCR.4, PDC)
    private static readonly string[] TimeUnits = new[]
    {
        "hours", "days", "weeks", "months"
    };

    public RangeCoherenceResolver(ILogger<RangeCoherenceResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanHandleComposite(string dataTypeCode)
        => dataTypeCode is "CQ" or "CM_RANGE" or "DR" or "CM_ABS_RANGE";

    public async Task<Dictionary<int, string>?> ResolveCompositeAsync(
        SegmentField parentField,
        DataType dataType,
        FieldResolutionContext context)
    {
        await Task.Yield(); // Async for interface compliance
        _random = context.FieldRng();
        _clock = context.Clock();

        try
        {
            return dataType.Code switch
            {
                "CQ" => ResolveCQ(parentField, context),
                "CM_RANGE" => ResolveCM_RANGE(parentField, context),
                "DR" => ResolveDR(parentField, context),
                "CM_ABS_RANGE" => ResolveCM_ABS_RANGE(parentField, context),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving range composite {DataType} for field {Field}",
                dataType.Code, parentField.Name);
            return null;
        }
    }

    /// <summary>
    /// Resolves CQ (Composite Quantity With Units).
    /// Structure: Quantity^Units
    /// Examples: 500^mg, 10^mL, 2^tablets
    /// </summary>
    private Dictionary<int, string> ResolveCQ(SegmentField parentField, FieldResolutionContext context)
    {
        // Determine appropriate units based on field context
        var fieldName = parentField.Name?.ToLowerInvariant() ?? "";
        var fieldDesc = parentField.Description?.ToLowerInvariant() ?? "";

        string[] unitOptions;
        double minQuantity, maxQuantity;

        // Context-aware unit selection
        if (fieldName.Contains("dose") || fieldDesc.Contains("dose") ||
            fieldName.Contains("medication") || fieldDesc.Contains("medication"))
        {
            // Medication dosing
            unitOptions = MedicationUnits;
            minQuantity = 5;
            maxQuantity = 1000;
        }
        else if (fieldName.Contains("volume") || fieldDesc.Contains("volume") ||
                 fieldName.Contains("collection") || fieldDesc.Contains("specimen"))
        {
            // Lab specimen volume
            unitOptions = VolumeUnits;
            minQuantity = 1;
            maxQuantity = 50;
        }
        else if (fieldName.Contains("duration") || fieldDesc.Contains("duration") ||
                 fieldName.Contains("time") || fieldName.Contains("shelf") ||
                 fieldDesc.Contains("retention"))
        {
            // Time duration
            unitOptions = TimeUnits;
            minQuantity = 1;
            maxQuantity = 365;
        }
        else
        {
            // Default to medication units
            unitOptions = MedicationUnits;
            minQuantity = 1;
            maxQuantity = 100;
        }

        // Generate quantity (with decimals for small units like mg)
        var quantity = _random.NextDouble() * (maxQuantity - minQuantity) + minQuantity;
        var quantityStr = quantity < 10 ? quantity.ToString("F1") : Math.Round(quantity).ToString();

        // Select appropriate unit
        var unit = unitOptions[_random.Next(unitOptions.Length)];

        _logger.LogDebug("Resolved CQ for {Field}: {Quantity}^{Unit}",
            parentField.Name, quantityStr, unit);

        return new Dictionary<int, string>
        {
            { 1, quantityStr },     // CQ.1: Quantity
            { 2, unit }             // CQ.2: Units
        };
    }

    /// <summary>
    /// Resolves CM_RANGE (Numeric Range).
    /// Structure: LowValue^HighValue
    /// Ensures: Low < High
    /// Examples: 60^100 (heart rate), 3.5^5.0 (potassium), 10^50 (age)
    /// </summary>
    private Dictionary<int, string> ResolveCM_RANGE(SegmentField parentField, FieldResolutionContext context)
    {
        // Generate range with low < high
        var fieldName = parentField.Name?.ToLowerInvariant() ?? "";
        var fieldDesc = parentField.Description?.ToLowerInvariant() ?? "";

        // Context-aware range generation
        double lowValue, highValue;

        if (fieldName.Contains("critical") || fieldDesc.Contains("critical"))
        {
            // Critical ranges tend to be outside normal ranges
            // Generate wider range for critical values
            var baseValue = _random.Next(20, 200);
            lowValue = baseValue;
            highValue = baseValue + _random.Next(50, 200);
        }
        else if (fieldName.Contains("normal") || fieldDesc.Contains("normal") ||
                 fieldName.Contains("reference") || fieldDesc.Contains("reference"))
        {
            // Normal reference ranges (narrower)
            var baseValue = _random.Next(10, 100);
            lowValue = baseValue;
            highValue = baseValue + _random.Next(10, 50);
        }
        else
        {
            // Generic range
            lowValue = _random.Next(1, 100);
            highValue = lowValue + _random.Next(10, 100);
        }

        _logger.LogDebug("Resolved CM_RANGE for {Field}: {Low}^{High}",
            parentField.Name, lowValue, highValue);

        return new Dictionary<int, string>
        {
            { 1, lowValue.ToString("F1") },    // CM_RANGE.1: Low Value
            { 2, highValue.ToString("F1") }    // CM_RANGE.2: High Value
        };
    }

    /// <summary>
    /// Resolves DR (Date Range).
    /// Structure: StartDateTime^EndDateTime
    /// Ensures: Start < End
    /// </summary>
    private Dictionary<int, string> ResolveDR(SegmentField parentField, FieldResolutionContext context)
    {
        // Generate date range where start < end
        var startDate = _clock.AddDays(-_random.Next(1, 30));
        var endDate = startDate.AddDays(_random.Next(1, 14)); // 1-14 days after start

        var startDateStr = startDate.ToString("yyyyMMddHHmmss");
        var endDateStr = endDate.ToString("yyyyMMddHHmmss");

        _logger.LogDebug("Resolved DR for {Field}: {Start}^{End}",
            parentField.Name, startDateStr, endDateStr);

        return new Dictionary<int, string>
        {
            { 1, startDateStr }     // DR.1: Range Start Date/Time
            // Note: DR.2 (end date) may not be defined in JSON, but we generate it for coherence
            // The actual structure will depend on how many components are in the data type definition
        };
    }

    /// <summary>
    /// Resolves CM_ABS_RANGE (Absolute Range).
    /// Structure: Range(CM_RANGE)^NumericChange^PercentPerChange^Days
    /// Contains a CM_RANGE plus change metrics
    /// </summary>
    private Dictionary<int, string> ResolveCM_ABS_RANGE(SegmentField parentField, FieldResolutionContext context)
    {
        // First resolve the CM_RANGE component
        var range = ResolveCM_RANGE(parentField, context);
        var lowValue = double.Parse(range[1]);
        var highValue = double.Parse(range[2]);

        // Calculate change metrics relative to the range
        var rangeSpan = highValue - lowValue;
        var numericChange = (rangeSpan * 0.1).ToString("F1"); // 10% of range
        var percentChange = _random.Next(5, 20).ToString(); // 5-20% change
        var days = _random.Next(1, 30).ToString(); // 1-30 days

        _logger.LogDebug("Resolved CM_ABS_RANGE for {Field}: {Low}-{High}, change={Change}, %={Percent}, days={Days}",
            parentField.Name, lowValue, highValue, numericChange, percentChange, days);

        // Note: CM_ABS_RANGE.1 is itself a CM_RANGE, so we need to format it as a nested composite
        var rangeStr = $"{range[1]}-{range[2]}"; // Format as "low-high"

        return new Dictionary<int, string>
        {
            { 1, rangeStr },        // CM_ABS_RANGE.1: Range (CM_RANGE)
            { 2, numericChange },   // CM_ABS_RANGE.2: Numeric Change
            { 3, percentChange },   // CM_ABS_RANGE.3: Percent Per Change
            { 4, days }             // CM_ABS_RANGE.4: Days
        };
    }
}
