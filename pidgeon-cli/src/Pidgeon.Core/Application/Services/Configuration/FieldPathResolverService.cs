// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core;

namespace Pidgeon.Core.Application.Services.Configuration;

/// <summary>
/// Standard-agnostic field path resolver that delegates to healthcare standard plugins.
/// Respects user configuration defaults while allowing explicit standard overrides.
/// </summary>
public class FieldPathResolverService : IFieldPathResolver
{
    private readonly IConfigurationService _configurationService;
    private readonly IEnumerable<IStandardFieldPathPlugin> _plugins;
    private readonly ILogger<FieldPathResolverService> _logger;

    public FieldPathResolverService(
        IConfigurationService configurationService,
        IEnumerable<IStandardFieldPathPlugin> plugins,
        ILogger<FieldPathResolverService> logger)
    {
        _configurationService = configurationService;
        _plugins = plugins;
        _logger = logger;
    }

    public async Task<Result<string>> ResolvePathAsync(
        string semanticPath,
        string messageType,
        string? standard = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(semanticPath))
            {
                return Result<string>.Failure("Semantic path cannot be empty");
            }

            if (string.IsNullOrWhiteSpace(messageType))
            {
                return Result<string>.Failure("Message type cannot be empty");
            }

            var effectiveStandard = await GetEffectiveStandardAsync(standard, messageType);
            if (effectiveStandard.IsFailure)
            {
                return Result<string>.Failure(effectiveStandard.Error);
            }

            var plugin = GetPluginForStandard(effectiveStandard.Value);
            if (plugin == null)
            {
                return Result<string>.Failure($"No plugin available for standard: {effectiveStandard.Value}");
            }

            _logger.LogDebug("Resolving path {SemanticPath} for {MessageType} using {Standard}",
                semanticPath, messageType, effectiveStandard.Value);

            return await plugin.ResolvePathAsync(semanticPath, messageType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve semantic path: {SemanticPath}", semanticPath);
            return Result<string>.Failure($"Failed to resolve path: {ex.Message}");
        }
    }

    public async Task<Result<bool>> ValidatePathAsync(
        string semanticPath,
        string messageType,
        string? standard = null)
    {
        try
        {
            var effectiveStandard = await GetEffectiveStandardAsync(standard, messageType);
            if (effectiveStandard.IsFailure)
            {
                return Result<bool>.Failure(effectiveStandard.Error);
            }

            var plugin = GetPluginForStandard(effectiveStandard.Value);
            if (plugin == null)
            {
                return Result<bool>.Failure($"No plugin available for standard: {effectiveStandard.Value}");
            }

            return await plugin.ValidatePathAsync(semanticPath, messageType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate semantic path: {SemanticPath}", semanticPath);
            return Result<bool>.Failure($"Failed to validate path: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> GetAvailablePathsAsync(
        string messageType,
        string? standard = null)
    {
        try
        {
            var effectiveStandard = await GetEffectiveStandardAsync(standard, messageType);
            if (effectiveStandard.IsFailure)
            {
                return Result<IReadOnlyDictionary<string, string>>.Failure(effectiveStandard.Error);
            }

            var plugin = GetPluginForStandard(effectiveStandard.Value);
            if (plugin == null)
            {
                return Result<IReadOnlyDictionary<string, string>>.Failure($"No plugin available for standard: {effectiveStandard.Value}");
            }

            return await plugin.GetAvailablePathsAsync(messageType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available paths for message type: {MessageType}", messageType);
            return Result<IReadOnlyDictionary<string, string>>.Failure($"Failed to get available paths: {ex.Message}");
        }
    }

    public async Task<Result<FieldValidationResult>> ValidateValueAsync(
        string semanticPath,
        string value,
        string messageType,
        string? standard = null)
    {
        try
        {
            var effectiveStandard = await GetEffectiveStandardAsync(standard, messageType);
            if (effectiveStandard.IsFailure)
            {
                return Result<FieldValidationResult>.Failure(effectiveStandard.Error);
            }

            var plugin = GetPluginForStandard(effectiveStandard.Value);
            if (plugin == null)
            {
                return Result<FieldValidationResult>.Failure($"No plugin available for standard: {effectiveStandard.Value}");
            }

            return await plugin.ValidateValueAsync(semanticPath, value, messageType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate value for path: {SemanticPath}", semanticPath);
            return Result<FieldValidationResult>.Failure($"Failed to validate value: {ex.Message}");
        }
    }

    private async Task<Result<string>> GetEffectiveStandardAsync(string? explicitStandard, string messageType)
    {
        if (!string.IsNullOrWhiteSpace(explicitStandard))
        {
            return Result<string>.Success(explicitStandard);
        }

        // Use configuration service to determine effective standard based on message type
        var effectiveStandardResult = await _configurationService.GetEffectiveStandardAsync(messageType, explicitStandard);
        if (effectiveStandardResult.IsFailure)
        {
            _logger.LogWarning("Failed to get effective standard for {MessageType}, using fallback: {Error}",
                messageType, effectiveStandardResult.Error.Message);
            return Result<string>.Success("HL7v23"); // Fallback default
        }

        _logger.LogDebug("Using effective standard for {MessageType}: {Standard}",
            messageType, effectiveStandardResult.Value);

        return effectiveStandardResult;
    }

    private IStandardFieldPathPlugin? GetPluginForStandard(string standard)
    {
        // First try exact match (for cases where standard family is passed directly)
        var exactMatch = _plugins.FirstOrDefault(p =>
            string.Equals(p.Standard, standard, StringComparison.OrdinalIgnoreCase));

        if (exactMatch != null)
        {
            return exactMatch;
        }

        // If no exact match, try family matching (HL7v23 → HL7, FHIRv4 → FHIR)
        var standardFamily = ExtractStandardFamily(standard);
        return _plugins.FirstOrDefault(p =>
            string.Equals(p.Standard, standardFamily, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractStandardFamily(string standard)
    {
        if (string.IsNullOrWhiteSpace(standard))
        {
            return standard;
        }

        // Extract family from versioned standards
        // HL7v23 → HL7, HL7v24 → HL7
        // FHIRv4 → FHIR, FHIRv4.0.1 → FHIR
        // NCPDPv2017071 → NCPDP

        if (standard.StartsWith("HL7", StringComparison.OrdinalIgnoreCase))
        {
            return "HL7";
        }

        if (standard.StartsWith("FHIR", StringComparison.OrdinalIgnoreCase))
        {
            return "FHIR";
        }

        if (standard.StartsWith("NCPDP", StringComparison.OrdinalIgnoreCase))
        {
            return "NCPDP";
        }

        // Return as-is if no pattern matches
        return standard;
    }
}