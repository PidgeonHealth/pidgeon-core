// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Application.Services.Clinical;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.Generation;

/// <summary>
/// Enforces the SHARE-PROFILE (SHAREABLE_RUN_STANDARD.md §3) at every share boundary. Because sharing is
/// stricter than saving, this refuses inputs that cannot replay on a recipient's device (AI, adhoc-locked
/// values, a populated Context, a machine-local lock session), inputs that would serialize ambiguously (an
/// offset-less clock), and metadata that breaks the structural no-PHI claim (over-length or
/// control-character title/description). It accepts embedded-deterministic inputs (vendor profiles, cohort
/// sequences) and a pinned clinical scenario only when the id resolves in the embedded catalog.
/// </summary>
public sealed class ShareProfileValidator : IShareProfileValidator
{
    private const int MaxTitleLength = 120;
    private const int MaxDescriptionLength = 500;
    private const string DevEngineVersion = "dev";

    private readonly ClinicalScenarioRepository _scenarioRepository;

    public ShareProfileValidator(ClinicalScenarioRepository scenarioRepository)
    {
        _scenarioRepository = scenarioRepository ?? throw new ArgumentNullException(nameof(scenarioRepository));
    }

    public ShareProfileResult ValidateOptions(GenerationOptions resolvedOptions, RunMeta? meta = null)
    {
        ArgumentNullException.ThrowIfNull(resolvedOptions);

        var violations = new List<ShareProfileViolation>();
        var warnings = new List<string>();

        // Inputs that never reach a manifest — refused here so `generate --share` fails before generation.
        if (resolvedOptions.UseAI)
            violations.Add(new ShareProfileViolation(
                "ai-nondeterministic",
                "AI-enhanced runs are non-deterministic and cannot be reproduced from a shared artifact."));

        if (resolvedOptions.AdhocLockedValues is { Count: > 0 })
            violations.Add(new ShareProfileViolation(
                "adhoc-locked",
                "Runs with inline adhoc locked values are not shareable in this format."));

        if (resolvedOptions.Context.Count > 0)
            violations.Add(new ShareProfileViolation(
                "context-populated",
                "Runs with a populated generation Context are not shareable in this format."));

        AddManifestSurfaceViolations(
            resolvedOptions.LockSessionName,
            resolvedOptions.AsOf,
            resolvedOptions.ClinicalScenarioId,
            engineVersion: null,
            violations,
            warnings);

        AddMetaViolations(meta, violations);

        return Build(violations, warnings);
    }

    public ShareProfileResult Validate(GenerationRunManifest manifest, RunMeta? meta = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var violations = new List<ShareProfileViolation>();
        var warnings = new List<string>();

        AddManifestSurfaceViolations(
            manifest.LockSessionName,
            manifest.AsOf,
            manifest.ClinicalScenarioId,
            manifest.EngineVersion,
            violations,
            warnings);

        AddMetaViolations(meta, violations);

        return Build(violations, warnings);
    }

    private void AddManifestSurfaceViolations(
        string? lockSessionName,
        DateTime? asOf,
        string? clinicalScenarioId,
        string? engineVersion,
        List<ShareProfileViolation> violations,
        List<string> warnings)
    {
        if (!string.IsNullOrEmpty(lockSessionName))
            violations.Add(new ShareProfileViolation(
                "lock-session-machine-local",
                "lock sessions are machine-local; run without a session, or wait for a format that inlines locked values."));

        // The shareable clock must serialize with an explicit UTC 'Z'; a Kind other than Utc would emit an
        // offset-less timestamp a different consumer could reinterpret.
        if (asOf is { } clock && clock.Kind != DateTimeKind.Utc)
            violations.Add(new ShareProfileViolation(
                "as-of-not-utc",
                "as_of must be an explicit UTC instant (serialized with 'Z'); an offset-less timestamp is ambiguous across machines."));

        if (!string.IsNullOrEmpty(clinicalScenarioId) && _scenarioRepository.GetScenarioById(clinicalScenarioId) is null)
            violations.Add(new ShareProfileViolation(
                "scenario-unresolved",
                $"clinical scenario '{clinicalScenarioId}' does not resolve in the embedded scenario catalog at share time."));

        if (string.Equals(engineVersion, DevEngineVersion, StringComparison.Ordinal))
            warnings.Add("engine_version is 'dev'; a dev-build artifact pins a meaningless version, which weakens version-skew diagnosis.");
    }

    private static void AddMetaViolations(RunMeta? meta, List<ShareProfileViolation> violations)
    {
        if (meta is null)
            return;

        if (meta.Title is { } title)
        {
            if (title.Length > MaxTitleLength)
                violations.Add(new ShareProfileViolation(
                    "meta-title-too-long",
                    $"meta.title exceeds {MaxTitleLength} characters ({title.Length})."));

            if (ContainsControlCharacter(title, allowNewline: false))
                violations.Add(new ShareProfileViolation(
                    "meta-title-control-chars",
                    "meta.title must be a single line with no control characters."));
        }

        if (meta.Description is { } description)
        {
            if (description.Length > MaxDescriptionLength)
                violations.Add(new ShareProfileViolation(
                    "meta-description-too-long",
                    $"meta.description exceeds {MaxDescriptionLength} characters ({description.Length})."));

            if (ContainsControlCharacter(description, allowNewline: true))
                violations.Add(new ShareProfileViolation(
                    "meta-description-control-chars",
                    "meta.description must not contain control characters other than newlines."));
        }
    }

    private static bool ContainsControlCharacter(string value, bool allowNewline)
        => value.Any(ch => char.IsControl(ch) && !(allowNewline && ch == '\n'));

    private static ShareProfileResult Build(List<ShareProfileViolation> violations, List<string> warnings)
        => new()
        {
            IsShareable = violations.Count == 0,
            Violations = violations,
            Warnings = warnings
        };
}
