// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Domain.Configuration;

namespace Pidgeon.Core.Infrastructure.Generation.Constraints;

/// <summary>
/// Validates candidate values against <see cref="FieldConstraints"/>: null,
/// table membership, allowed-value list, length bounds, and HL7 data-type
/// format rules.
/// </summary>
public sealed class HL7ConstraintValueValidator
{
    private const string StandardId = "hl7v23";

    private readonly IStandardReferenceService _referenceService;
    private readonly HL7ConstraintExtractor _extractor;
    private readonly ILogger<HL7ConstraintValueValidator> _logger;

    public HL7ConstraintValueValidator(
        IStandardReferenceService referenceService,
        HL7ConstraintExtractor extractor,
        ILogger<HL7ConstraintValueValidator> logger)
    {
        _referenceService = referenceService ?? throw new ArgumentNullException(nameof(referenceService));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates <paramref name="value"/> against <paramref name="constraints"/>,
    /// returning success on pass or a descriptive failure on the first rule
    /// that fails. Preserves the original short-circuit order: null → table →
    /// allowed values → length → data type.
    /// </summary>
    public async Task<Result<bool>> ValidateValueAsync(object value, FieldConstraints constraints)
    {
        try
        {
            if (value == null)
            {
                return constraints.Required
                    ? Result<bool>.Failure("Required field cannot be null")
                    : Result<bool>.Success(true);
            }

            var stringValue = value.ToString() ?? string.Empty;

            if (constraints.TableReference != null)
            {
                return await ValidateAgainstTableAsync(stringValue, constraints.TableReference);
            }

            if (constraints.AllowedValues?.Any() == true)
            {
                if (!constraints.AllowedValues.Contains(stringValue))
                {
                    return Result<bool>.Failure($"Value '{stringValue}' not in allowed values");
                }
            }

            if (constraints.MaxLength.HasValue && stringValue.Length > constraints.MaxLength.Value)
            {
                return Result<bool>.Failure($"Value exceeds maximum length {constraints.MaxLength.Value}");
            }

            if (constraints.MinLength.HasValue && stringValue.Length < constraints.MinLength.Value)
            {
                return Result<bool>.Failure($"Value below minimum length {constraints.MinLength.Value}");
            }

            if (constraints.DataType != null)
            {
                return ValidateDataType(stringValue, constraints.DataType);
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating value");
            return Result<bool>.Failure($"Validation failed: {ex.Message}");
        }
    }

    private async Task<Result<bool>> ValidateAgainstTableAsync(string value, string tableId)
    {
        try
        {
            var tableResult = await _referenceService.LookupAsync(StandardId, tableId);
            if (!tableResult.IsSuccess)
            {
                return Result<bool>.Success(true);
            }

            var validValues = _extractor.ExtractTableValues(tableResult.Value);
            if (!validValues.Any())
            {
                return Result<bool>.Success(true);
            }

            return validValues.Contains(value)
                ? Result<bool>.Success(true)
                : Result<bool>.Failure($"Value '{value}' not found in table {tableId}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error validating against table {TableId}", tableId);
            return Result<bool>.Success(true);
        }
    }

    private static Result<bool> ValidateDataType(string value, string dataType)
    {
        return dataType.ToUpperInvariant() switch
        {
            "NM" => decimal.TryParse(value, out _)
                ? Result<bool>.Success(true)
                : Result<bool>.Failure($"'{value}' is not a valid number"),
            "DT" => Regex.IsMatch(value, @"^\d{8}$")
                ? Result<bool>.Success(true)
                : Result<bool>.Failure($"'{value}' is not a valid date (YYYYMMDD)"),
            "TS" => Regex.IsMatch(value, @"^\d{8,14}$")
                ? Result<bool>.Success(true)
                : Result<bool>.Failure($"'{value}' is not a valid timestamp"),
            _ => Result<bool>.Success(true)
        };
    }
}
