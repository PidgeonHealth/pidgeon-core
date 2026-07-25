// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Standards;
using Pidgeon.Core.Domain.Validation;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.Validation;

/// <summary>
/// FHIR R4 validator plugin. Entry point for the
/// <see cref="IStandardValidationPlugin"/> dispatch chain.
///
/// Composes the
/// <see cref="IFHIRValidator"/> (R4 base: cardinality, references, bundle
/// entry integrity) and <see cref="IProfileValidator"/> (profile-based:
/// US Core, custom StructureDefinitions). Results are translated to the
/// shared <see cref="ValidationResult"/> shape by
/// <see cref="FHIRResultTranslator"/>.
///
/// Profile resolution accepts:
/// <list type="bullet">
///   <item><description><c>"us-core"</c> — picks the US Core profile that matches the resource's <c>resourceType</c></description></item>
///   <item><description><c>"us-core-patient"</c> or any explicit alias known to the loader</description></item>
///   <item><description>Canonical URLs like <c>http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient</c></description></item>
///   <item><description>Local file paths or directory paths (the underlying loader scans)</description></item>
/// </list>
/// </summary>
internal sealed class FHIRValidationPlugin : IStandardValidationPlugin
{
    private readonly ILogger<FHIRValidationPlugin> _logger;
    private readonly IFHIRValidator _fhirValidator;
    private readonly IProfileValidator? _profileValidator;
    private readonly Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation.IFhirIGService? _fhirIGService;

    // One-shot latch so we only scan for installed IG packages on the first
    // validation that requests a profile. Uses Interlocked to keep the check
    // cheap on the hot path once the scan has run.
    private int _igBootstrapRan;

    public string StandardName => "fhir";

    public FHIRValidationPlugin(
        ILogger<FHIRValidationPlugin> logger,
        IFHIRValidator fhirValidator,
        IProfileValidator? profileValidator = null,
        Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation.IFhirIGService? fhirIGService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fhirValidator = fhirValidator ?? throw new ArgumentNullException(nameof(fhirValidator));
        _profileValidator = profileValidator;
        _fhirIGService = fhirIGService;
    }

    public bool CanHandle(string messageContent)
    {
        if (string.IsNullOrWhiteSpace(messageContent))
            return false;

        var trimmed = messageContent.TrimStart();
        if (!trimmed.StartsWith('{'))
            return false;

        return trimmed.Contains("\"resourceType\"", StringComparison.Ordinal)
            || trimmed.Contains("\"Bundle\"", StringComparison.Ordinal);
    }

    public async Task<Result<ValidationResult>> ValidateAsync(
        string messageContent,
        string? profile,
        ValidationMode mode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageContent))
        {
            return Result<ValidationResult>.Failure("Message content cannot be empty");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Structural pre-check: well-formed JSON + resourceType presence.
        // Catches malformed input before the R4 engine sees it, with a
        // friendly error surface for parse failures.
        var (resourceType, structuralError) = ExtractResourceTypeOrError(messageContent);
        if (structuralError != null)
        {
            stopwatch.Stop();
            return Result<ValidationResult>.Success(BuildStructuralErrorResult(structuralError, mode, profile, stopwatch.Elapsed));
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(profile))
            {
                return await ValidateWithProfileAsync(messageContent, resourceType!, profile!, mode, stopwatch, cancellationToken)
                    .ConfigureAwait(false);
            }

            return await ValidateBaseAsync(messageContent, resourceType!, mode, stopwatch)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "FHIR validation failed with exception");
            return Result<ValidationResult>.Failure($"FHIR validation failed: {ex.Message}");
        }
    }

    private async Task<Result<ValidationResult>> ValidateBaseAsync(
        string messageContent, string resourceType, ValidationMode mode, System.Diagnostics.Stopwatch stopwatch)
    {
        var engineResult = string.Equals(resourceType, "Bundle", StringComparison.OrdinalIgnoreCase)
            ? await _fhirValidator.ValidateBundleAsync(messageContent).ConfigureAwait(false)
            : await _fhirValidator.ValidateResourceAsync(messageContent, resourceType).ConfigureAwait(false);

        stopwatch.Stop();

        if (!engineResult.IsSuccess)
        {
            return Result<ValidationResult>.Failure($"FHIR R4 validation failed: {engineResult.Error.Message}");
        }

        var translated = FHIRResultTranslator.FromBase(
            engineResult.Value!, mode, profile: null, elapsed: stopwatch.Elapsed);
        return Result<ValidationResult>.Success(translated);
    }

    private async Task<Result<ValidationResult>> ValidateWithProfileAsync(
        string messageContent,
        string resourceType,
        string profile,
        ValidationMode mode,
        System.Diagnostics.Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        // Lazy-load every installed IG package on the first profile validation.
        // This is what makes `pidgeon data install fhir-us-core-6.0` followed
        // by `pidgeon validate --profile us-core-patient ...` "just work" —
        // the user doesn't have to know that a service exists or explicitly
        // call LoadIGAsync. IG packages that aren't installed are skipped and
        // validation falls back to embedded stub profiles.
        if (_fhirIGService is not null
            && Interlocked.CompareExchange(ref _igBootstrapRan, 1, 0) == 0)
        {
            var igBootstrap = await _fhirIGService.EnsureAllInstalledLoadedAsync().ConfigureAwait(false);
            if (!igBootstrap.IsSuccess)
            {
                _logger.LogWarning(
                    "FHIR IG bootstrap reported failure: {Error}. Continuing with embedded profiles only.",
                    igBootstrap.Error.Message);
            }
            else if (igBootstrap.Value > 0)
            {
                _logger.LogDebug(
                    "FHIR IG bootstrap loaded {Count} StructureDefinitions from installed packages",
                    igBootstrap.Value);
            }
        }

        var profileUrl = NormalizeProfile(profile, resourceType);

        // A bare short name that NormalizeProfile could not map — Da Vinci profiles
        // like "profile-claim" carry no IG-prefix hint — is resolved against the
        // known-IG indexes now that the bootstrap above has loaded every installed
        // package. Without this the short name falls through to the validator as a
        // canonical URL and fails PROFILE_NOT_FOUND even when the owning package is
        // installed (FABLE_CONFORM_AUDIT C11).
        if (_fhirIGService is not null && IsBareShortName(profileUrl))
        {
            var resolved = await _fhirIGService.ResolveProfileCanonicalAsync(profileUrl).ConfigureAwait(false);
            if (resolved is not null)
                profileUrl = resolved;
        }

        _logger.LogDebug("FHIR profile validation: requested={RequestedProfile}, resolved={ResolvedUrl}", profile, profileUrl);

        var engineResult = await _fhirValidator.ValidateWithProfileAsync(messageContent, profileUrl).ConfigureAwait(false);
        stopwatch.Stop();

        if (!engineResult.IsSuccess)
        {
            // Profile resolution/loading failure is a config error — surface it as
            // a Result.Failure rather than a validation failure so the CLI can
            // distinguish "your profile reference is wrong" from "your resource
            // is wrong".
            return Result<ValidationResult>.Failure($"FHIR profile validation failed: {engineResult.Error.Message}");
        }

        var translated = FHIRResultTranslator.FromProfile(
            engineResult.Value!, mode, profile, elapsed: stopwatch.Elapsed);
        return Result<ValidationResult>.Success(translated);
    }

    /// <summary>
    /// Canonicalises the profile argument. US Core aliases map to the official
    /// canonical URL so the underlying <see cref="IStructureDefinitionLoader"/>
    /// can resolve them against embedded US Core StructureDefinitions.
    /// </summary>
    internal static string NormalizeProfile(string profile, string resourceType)
    {
        if (string.IsNullOrWhiteSpace(profile))
            return profile;

        // Already a canonical URL or file path — let the loader handle it.
        if (profile.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || profile.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || profile.Contains('/') || profile.Contains('\\'))
        {
            return profile;
        }

        var lower = profile.ToLowerInvariant();

        // "us-core-patient" → canonical URL directly
        if (lower.StartsWith("us-core-", StringComparison.Ordinal))
        {
            return $"http://hl7.org/fhir/us/core/StructureDefinition/{lower}";
        }

        // Bare "us-core" → pick the profile that matches the resource's type
        if (lower == "us-core" && !string.IsNullOrWhiteSpace(resourceType))
        {
            var suffix = resourceType.ToLowerInvariant() switch
            {
                "observation" => "us-core-observation-lab",       // default to lab when generic
                _ => $"us-core-{resourceType.ToLowerInvariant()}",
            };
            return $"http://hl7.org/fhir/us/core/StructureDefinition/{suffix}";
        }

        return profile;
    }

    /// <summary>
    /// True when a normalized profile reference is still a bare token — no scheme,
    /// no path separator, not a local <c>.json</c> file — i.e. a short name
    /// <see cref="NormalizeProfile"/> could not map (a Da Vinci profile name). Only
    /// these are worth resolving against the known-IG indexes.
    /// </summary>
    private static bool IsBareShortName(string profile)
        => !string.IsNullOrWhiteSpace(profile)
        && !profile.Contains('/')
        && !profile.Contains('\\')
        && !profile.StartsWith("http", StringComparison.OrdinalIgnoreCase)
        && !profile.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses the outer JSON to pull <c>resourceType</c> without loading the
    /// full resource graph. Returns the type or a reason-string for structural
    /// failure so the caller can emit a friendly ValidationIssue.
    /// </summary>
    private static (string? ResourceType, string? Error) ExtractResourceTypeOrError(string json)
    {
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return (null, $"FHIR resource is not valid JSON: {ex.Message}");
        }

        try
        {
            if (!doc.RootElement.TryGetProperty("resourceType", out var resourceTypeProp)
                || resourceTypeProp.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(resourceTypeProp.GetString()))
            {
                return (null, "FHIR resource is missing the required 'resourceType' property");
            }

            return (resourceTypeProp.GetString(), null);
        }
        finally
        {
            doc.Dispose();
        }
    }

    private static ValidationResult BuildStructuralErrorResult(
        string error, ValidationMode mode, string? profile, TimeSpan elapsed)
    {
        var issues = new List<ValidationIssue>
        {
            new ValidationIssue
            {
                Location = "Resource",
                Severity = ValidationSeverity.Error,
                Message = error,
                RuleId = "FHIR-PARSE-001",
                Suggestion = "Ensure the file is well-formed JSON with a top-level resourceType before validating",
            }
        };

        return new ValidationResult
        {
            IsValid = false,
            Standard = "fhir",
            Profile = profile,
            Mode = mode,
            Issues = issues,
            Statistics = new ValidationStatistics
            {
                TotalRulesChecked = 1,
                RulesPassed = 0,
                RulesFailed = 1,
                FieldsValidated = 0,
                ValidationTime = elapsed,
            }
        };
    }
}
