// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pidgeon.Core;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Domain.Configuration;
using Pidgeon.Core.Infrastructure.Generation.Constraints;

namespace Pidgeon.Core.Application.Services.Generation;

/// <summary>
/// Orchestrates constraint resolution across multiple standard-specific plugins.
/// Provides caching, fallback mechanisms, and plugin coordination.
/// </summary>
public class ConstraintResolver : IConstraintResolver
{
    private readonly IReadOnlyList<IConstraintResolverPlugin> _plugins;
    private readonly ILogger<ConstraintResolver> _logger;
    private readonly IMemoryCache _cache;

    public ConstraintResolver(
        IEnumerable<IConstraintResolverPlugin> plugins,
        ILogger<ConstraintResolver> logger,
        IMemoryCache cache)
    {
        _plugins = plugins.OrderByDescending(p => p.Priority).ToList();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));

        _logger.LogDebug("ConstraintResolver initialized with {PluginCount} plugins", _plugins.Count);
    }

    public async Task<Result<object>> GenerateConstrainedValueAsync(
        string context,
        FieldConstraints? constraints,
        Random random)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return Result<object>.Failure("Context cannot be null or empty");
        }

        try
        {
            // If no constraints provided, fetch them
            if (constraints == null)
            {
                var constraintsResult = await GetConstraintsAsync(context);
                if (!constraintsResult.IsSuccess)
                {
                    // Per-field hot path: guard before interpolation.
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("No constraints found for {Context}, using default generation", context);
                    return GenerateDefaultValue(context, random);
                }
                constraints = constraintsResult.Value;
            }

            // Find appropriate plugin
            var plugin = _plugins.FirstOrDefault(p => p.CanResolve(context));
            if (plugin == null)
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("No plugin found for {Context}, using basic generation", context);
                return GenerateBasicValue(constraints, random);
            }

            // Generated values are intentionally not cached. A randomized value drawn from
            // the seeded RNG must be recomputed every call: caching it skips RNG draws on a
            // hit, which desynchronizes the per-entity seeded Random and breaks byte-repro,
            // and reuses one value across entities (every
            // patient the same gender). Field-constraint *definitions* are still cached in
            // GetConstraintsAsync, the cache that saves work without affecting draws.
            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace("Using plugin {PluginId} for {Context}", plugin.StandardId, context);
            return await plugin.GenerateValueAsync(constraints, random);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating constrained value for {Context}", context);
            return Result<object>.Failure($"Constraint generation failed: {ex.Message}");
        }
    }

    public async Task<Result<FieldConstraints>> GetConstraintsAsync(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return Result<FieldConstraints>.Failure("Context cannot be null or empty");
        }

        try
        {
            // Check cache first
            var cacheKey = GenerateCacheKey("constraints", context);
            if (_cache.TryGetValue<FieldConstraints>(cacheKey, out var cached))
            {
                if (_logger.IsEnabled(LogLevel.Trace))
                    _logger.LogTrace("Using cached constraints for {Context}", context);
                return Result<FieldConstraints>.Success(cached!);
            }

            // Find appropriate plugin
            var plugin = _plugins.FirstOrDefault(p => p.CanResolve(context));
            if (plugin == null)
            {
                return Result<FieldConstraints>.Failure($"No plugin can resolve context: {context}");
            }

            // Get constraints from plugin
            var result = await plugin.GetConstraintsAsync(context);

            // Cache successful constraint resolution
            if (result.IsSuccess)
            {
                _cache.Set(cacheKey, result.Value, TimeSpan.FromMinutes(30));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting constraints for {Context}", context);
            return Result<FieldConstraints>.Failure($"Constraint lookup failed: {ex.Message}");
        }
    }

    public async Task<Result<bool>> ValidateValueAsync(
        string context,
        object value,
        FieldConstraints constraints)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return Result<bool>.Failure("Context cannot be null or empty");
        }

        if (value == null && constraints.Required)
        {
            return Result<bool>.Failure("Required field cannot be null");
        }

        try
        {
            // Find appropriate plugin
            var plugin = _plugins.FirstOrDefault(p => p.CanResolve(context));
            if (plugin == null)
            {
                // Basic validation without plugin
                return ValidateBasic(value, constraints);
            }

            // Handle null value for plugin validation
            if (value == null)
            {
                return Result<bool>.Success(true); // Already checked required constraint above
            }

            return await plugin.ValidateValueAsync(value, constraints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating value for {Context}", context);
            return Result<bool>.Failure($"Validation failed: {ex.Message}");
        }
    }

    private static string GenerateCacheKey(string prefix, string context, FieldConstraints? constraints = null)
    {
        if (constraints == null)
        {
            return $"{prefix}:{context}";
        }

        return $"{prefix}:{context}:{constraints.GetHashCode()}";
    }

    private Result<object> GenerateDefaultValue(string context, Random random)
    {
        // Very basic default generation based on context
        if (context.Contains("Gender") || context.Contains("Sex"))
        {
            return Result<object>.Success(random.Next(2) == 0 ? "M" : "F");
        }

        if (context.Contains("Date") || context.Contains("Time"))
        {
            return Result<object>.Success(DateTime.Now.ToString("yyyyMMdd"));
        }

        if (context.Contains("Name"))
        {
            return Result<object>.Success("DefaultName");
        }

        if (context.Contains("Id") || context.Contains("Number"))
        {
            return Result<object>.Success(random.Next(100000, 999999).ToString());
        }

        return Result<object>.Success("DefaultValue");
    }

    private Result<object> GenerateBasicValue(FieldConstraints constraints, Random random)
    {
        // Handle explicit allowed values
        if (constraints.AllowedValues?.Any() == true)
        {
            var value = constraints.AllowedValues[random.Next(constraints.AllowedValues.Count)];
            return Result<object>.Success(value);
        }

        // Handle pattern constraints
        if (!string.IsNullOrEmpty(constraints.Pattern))
        {
            // TODO: Implement regex-based generation
            return Result<object>.Success("PatternValue");
        }

        // Handle data type constraints
        if (!string.IsNullOrEmpty(constraints.DataType))
        {
            return constraints.DataType.ToUpperInvariant() switch
            {
                "NM" or "NUMBER" => Result<object>.Success(random.Next(1, 1000)),
                "DT" or "DATE" => Result<object>.Success(DateTime.Now.ToString("yyyyMMdd")),
                "TM" or "TIME" => Result<object>.Success(DateTime.Now.ToString("HHmmss")),
                "TS" or "TIMESTAMP" => Result<object>.Success(DateTime.Now.ToString("yyyyMMddHHmmss")),
                _ => Result<object>.Success("BasicValue")
            };
        }

        // Handle length constraints
        if (constraints.MaxLength.HasValue)
        {
            var length = Math.Min(constraints.MaxLength.Value, 10);
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var result = new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
            return Result<object>.Success(result);
        }

        return Result<object>.Success("BasicValue");
    }

    private Result<bool> ValidateBasic(object? value, FieldConstraints constraints)
    {
        if (value == null)
        {
            return constraints.Required
                ? Result<bool>.Failure("Required field cannot be null")
                : Result<bool>.Success(true);
        }

        var stringValue = value.ToString() ?? string.Empty;

        // Check length constraints
        if (constraints.MaxLength.HasValue && stringValue.Length > constraints.MaxLength.Value)
        {
            return Result<bool>.Failure($"Value length {stringValue.Length} exceeds maximum {constraints.MaxLength.Value}");
        }

        if (constraints.MinLength.HasValue && stringValue.Length < constraints.MinLength.Value)
        {
            return Result<bool>.Failure($"Value length {stringValue.Length} is below minimum {constraints.MinLength.Value}");
        }

        // Check allowed values
        if (constraints.AllowedValues?.Any() == true && !constraints.AllowedValues.Contains(stringValue))
        {
            return Result<bool>.Failure($"Value '{stringValue}' is not in allowed values: {string.Join(", ", constraints.AllowedValues)}");
        }

        // Check pattern
        if (!string.IsNullOrEmpty(constraints.Pattern))
        {
            try
            {
                if (!Regex.IsMatch(stringValue, constraints.Pattern))
                {
                    return Result<bool>.Failure($"Value '{stringValue}' does not match pattern '{constraints.Pattern}'");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid regex pattern: {Pattern}", constraints.Pattern);
            }
        }

        return Result<bool>.Success(true);
    }
}