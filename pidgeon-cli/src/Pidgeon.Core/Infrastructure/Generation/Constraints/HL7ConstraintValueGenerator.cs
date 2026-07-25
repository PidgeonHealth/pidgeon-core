// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Domain.Configuration;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Infrastructure.Generation.Constraints;

/// <summary>
/// Generates HL7 v2.3-compliant sample values from a
/// <see cref="FieldConstraints"/> record. Covers the table-reference path,
/// allowed-value selection, per-data-type generation (ST/NM/DT/TM/TS/ID/IS/CE/
/// XPN/XAD/XTN), and default random-text fallbacks.
/// </summary>
public sealed class HL7ConstraintValueGenerator
{
    private const string StandardId = "hl7v23";

    private readonly IStandardReferenceService _referenceService;
    private readonly HL7ConstraintExtractor _extractor;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HL7ConstraintValueGenerator> _logger;
    private readonly IHL7FallbackValueSource _fallbacks;

    public HL7ConstraintValueGenerator(
        IStandardReferenceService referenceService,
        HL7ConstraintExtractor extractor,
        IMemoryCache cache,
        ILogger<HL7ConstraintValueGenerator> logger,
        IHL7FallbackValueSource fallbacks)
    {
        _referenceService = referenceService ?? throw new ArgumentNullException(nameof(referenceService));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fallbacks = fallbacks ?? throw new ArgumentNullException(nameof(fallbacks));
    }

    /// <summary>
    /// Entry point: resolves the highest-priority generation strategy for the
    /// given constraints (table → allowed values → data type → default).
    /// </summary>
    public async Task<Result<object>> GenerateValueAsync(FieldConstraints constraints, Random random)
    {
        try
        {
            if (constraints.TableReference != null)
            {
                return await GenerateFromTableAsync(constraints.TableReference, random);
            }

            if (constraints.AllowedValues?.Any() == true)
            {
                var value = constraints.AllowedValues[random.Next(constraints.AllowedValues.Count)];
                return Result<object>.Success(value);
            }

            if (constraints.DataType != null)
            {
                return GenerateByDataType(constraints.DataType, constraints, random);
            }

            return GenerateDefaultValue(constraints, random);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating value for constraints");
            return Result<object>.Failure($"Value generation failed: {ex.Message}");
        }
    }

    private async Task<Result<object>> GenerateFromTableAsync(string tableId, Random random)
    {
        var cacheKey = $"hl7-table:{tableId}";
        if (_cache.TryGetValue<List<string>>(cacheKey, out var cachedValues))
        {
            if (cachedValues != null && cachedValues.Any())
            {
                return Result<object>.Success(cachedValues[random.Next(cachedValues.Count)]);
            }
        }

        try
        {
            var values = await _extractor.ExtractTableValuesAsync(tableId);

            if (!values.Any())
            {
                var tableResult = await _referenceService.LookupAsync(StandardId, tableId);
                if (tableResult.IsSuccess)
                {
                    values = _extractor.ExtractTableValues(tableResult.Value);
                }
            }

            if (!values.Any())
            {
                return GenerateFallbackTableValue(tableId, random);
            }

            _cache.Set(cacheKey, values, TimeSpan.FromHours(1));

            var selectedValue = values[random.Next(values.Count)];
            return Result<object>.Success(selectedValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading table {TableId}, using fallback", tableId);
            return GenerateFallbackTableValue(tableId, random);
        }
    }

    private Result<object> GenerateByDataType(string dataType, FieldConstraints constraints, Random random)
    {
        return dataType.ToUpperInvariant() switch
        {
            "ST" => GenerateString(constraints, random),
            "NM" => GenerateNumeric(constraints, random),
            "DT" => GenerateDate(constraints, random),
            "TM" => GenerateTime(random),
            "TS" => GenerateTimestamp(random),
            "ID" => GenerateIdentifier(constraints, random),
            "IS" => GenerateCodedValue(constraints, random),
            "CE" => Result<object>.Success("CE_VALUE^CODED_ELEMENT"),
            "XPN" => Result<object>.Success("LAST^FIRST^MIDDLE"),
            "XAD" => Result<object>.Success("123 MAIN ST^^CITY^ST^12345"),
            "XTN" => Result<object>.Success("555-1234"),
            _ => GenerateDefaultValue(constraints, random)
        };
    }

    private static Result<object> GenerateString(FieldConstraints constraints, Random random)
    {
        var maxLength = constraints.MaxLength ?? 20;
        var length = random.Next(1, Math.Min(maxLength, 20));
        var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ ";
        var result = new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray()).Trim();
        return Result<object>.Success(result);
    }

    private static Result<object> GenerateNumeric(FieldConstraints constraints, Random random)
    {
        var min = (int)(constraints.Numeric?.MinValue ?? 0);
        var max = (int)(constraints.Numeric?.MaxValue ?? 999999);
        return Result<object>.Success(random.Next(min, max));
    }

    private static Result<object> GenerateDate(FieldConstraints constraints, Random random)
    {
        var minDate = constraints.DateTime?.MinDate ?? DateTime.Today.AddYears(-50);
        var maxDate = constraints.DateTime?.MaxDate ?? DateTime.Today;
        var range = (maxDate - minDate).Days;
        var randomDate = minDate.AddDays(random.Next(range));
        return Result<object>.Success(randomDate.ToString("yyyyMMdd"));
    }

    private static Result<object> GenerateTime(Random random)
    {
        var time = TimeSpan.FromMinutes(random.Next(0, 24 * 60));
        return Result<object>.Success(time.ToString(@"hhmmss"));
    }

    private static Result<object> GenerateTimestamp(Random random)
    {
        var date = DateTime.Today.AddDays(random.Next(-365, 365));
        var time = TimeSpan.FromMinutes(random.Next(0, 24 * 60));
        var timestamp = date.Add(time);
        return Result<object>.Success(timestamp.ToString("yyyyMMddHHmmss"));
    }

    private static Result<object> GenerateIdentifier(FieldConstraints constraints, Random random)
    {
        var length = constraints.MaxLength ?? 10;
        var id = random.Next(1, (int)Math.Pow(10, Math.Min(length, 9)));
        return Result<object>.Success(id.ToString().PadLeft(length, '0'));
    }

    private static Result<object> GenerateCodedValue(FieldConstraints constraints, Random random)
    {
        var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var length = constraints.MaxLength ?? 3;
        var result = new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
        return Result<object>.Success(result);
    }

    private static Result<object> GenerateDefaultValue(FieldConstraints constraints, Random random)
    {
        var length = Math.Min(constraints.MaxLength ?? 10, 10);
        var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var result = new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
        return Result<object>.Success(result);
    }

    private Result<object> GenerateFallbackTableValue(string tableId, Random random)
    {
        var values = _fallbacks.GetConstraintTableFallback(tableId);
        return values is { Count: > 0 }
            ? Result<object>.Success(values[random.Next(values.Count)])
            : Result<object>.Success("UNK");
    }
}
