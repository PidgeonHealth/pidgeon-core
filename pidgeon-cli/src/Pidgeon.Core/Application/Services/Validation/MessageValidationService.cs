// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces;
using Pidgeon.Core.Application.Interfaces.Semantic;
using Pidgeon.Core.Application.Interfaces.Standards;
using Pidgeon.Core.Domain.Validation;

namespace Pidgeon.Core.Application.Services.Validation;

/// <summary>
/// Standards-agnostic dispatcher that routes a raw message to the
/// correct <see cref="IStandardValidationPlugin"/>.
///
/// Holds zero knowledge of individual standards: all HL7/FHIR/NCPDP logic
/// lives in the plugins under <c>Pidgeon.Core.Infrastructure.Standards.*</c>.
/// Selection is by explicit <paramref name="standard"/> match, else by the
/// first plugin whose <see cref="IStandardValidationPlugin.CanHandle"/>
/// returns true. Adding a new standard is additive — register a new plugin,
/// no edits required here.
/// </summary>
internal sealed class MessageValidationService : IMessageValidationService
{
    private readonly IReadOnlyList<IStandardValidationPlugin> _plugins;
    private readonly ISemanticCheckRunner? _semanticChecks;
    private readonly ILogger<MessageValidationService> _logger;

    public MessageValidationService(
        IEnumerable<IStandardValidationPlugin> plugins,
        ILogger<MessageValidationService> logger,
        ISemanticCheckRunner? semanticChecks = null)
    {
        _plugins = (plugins ?? throw new ArgumentNullException(nameof(plugins)))
            .ToList()
            .AsReadOnly();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _semanticChecks = semanticChecks;
    }

    public async Task<Result<ValidationResult>> ValidateAsync(
        string messageContent,
        string? standard = null,
        string? profile = null,
        ValidationMode mode = ValidationMode.Strict)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(messageContent))
            {
                return Result<ValidationResult>.Failure("Message content cannot be empty");
            }

            var plugin = SelectPlugin(messageContent, standard);
            if (plugin == null)
            {
                return Result<ValidationResult>.Failure("Unable to detect message standard");
            }

            _logger.LogInformation("Validating {Standard} message in {Mode} mode{ProfileNote}",
                plugin.StandardName, mode,
                string.IsNullOrWhiteSpace(profile) ? "" : $" against profile '{profile}'");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await plugin.ValidateAsync(messageContent, profile, mode).ConfigureAwait(false);
            stopwatch.Stop();

            if (result.IsSuccess && result.Value is { } validation)
            {
                // Overwrite ValidationTime — plugins don't measure dispatch latency — and
                // attach the advisory semantic channel additively. SemanticFindings never
                // feed IsValid or the error/warning counts; a clinically-odd-but-structurally-
                // valid message stays valid here and surfaces the concern alongside.
                var withTiming = validation with
                {
                    Statistics = validation.Statistics with { ValidationTime = stopwatch.Elapsed },
                    SemanticFindings = MergeSemanticFindings(validation.SemanticFindings, messageContent)
                };

                _logger.LogInformation(
                    "Validation completed: IsValid={IsValid}, Errors={ErrorCount}, Warnings={WarningCount}",
                    withTiming.IsValid, withTiming.ErrorCount, withTiming.WarningCount);

                return Result<ValidationResult>.Success(withTiming);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed with exception");
            return Result<ValidationResult>.Failure($"Validation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Appends advisory semantic findings to any the plugin already produced. Returns the
    /// existing list unchanged when the semantic layer is absent or finds nothing — the
    /// channel is purely additive and never throws (the runner swallows its own faults).
    /// </summary>
    private List<SemanticFinding> MergeSemanticFindings(List<SemanticFinding> existing, string messageContent)
    {
        if (_semanticChecks is null)
            return existing;

        var findings = _semanticChecks.Run(messageContent);
        if (findings.Count == 0)
            return existing;

        return existing.Concat(findings).ToList();
    }

    private IStandardValidationPlugin? SelectPlugin(string messageContent, string? standard)
    {
        if (!string.IsNullOrWhiteSpace(standard))
        {
            return _plugins.FirstOrDefault(p =>
                p.StandardName.Equals(standard, StringComparison.OrdinalIgnoreCase));
        }

        return _plugins.FirstOrDefault(p => p.CanHandle(messageContent));
    }
}
