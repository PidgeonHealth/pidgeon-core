// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Interfaces.Generation;

/// <summary>
/// The SHARE-PROFILE gate (SHAREABLE_RUN_STANDARD.md §3). Sharing is stricter than saving: an artifact
/// crossing machines must refuse what cannot replay on someone else's device, and must keep the no-PHI
/// claim structural. This validator is a strict superset of <see cref="GenerationRunManifest.Capture"/>'s
/// rejections — it additionally refuses machine-local lock sessions, offset-less timestamps, and
/// over-length or control-character metadata, and confirms a pinned clinical scenario resolves at share
/// time. It lists <em>every</em> violated rule rather than stopping at the first.
/// </summary>
public interface IShareProfileValidator
{
    /// <summary>
    /// Validates a run about to be shared from its resolved generation options plus the intended metadata.
    /// This entry sees the full input surface (AI, adhoc-locked, Context) that never reaches a manifest, so
    /// it is the gate for <c>generate --share</c>.
    /// </summary>
    ShareProfileResult ValidateOptions(
        GenerationOptions resolvedOptions,
        RunMeta? meta = null);

    /// <summary>
    /// Validates an already-captured manifest plus its metadata. Used at the fork boundary, where the
    /// manifest is structurally free of AI / adhoc / Context inputs by construction.
    /// </summary>
    ShareProfileResult Validate(
        GenerationRunManifest manifest,
        RunMeta? meta = null);
}

/// <summary>The outcome of a SHARE-PROFILE check: shareable-or-not, every violation, and any non-blocking warnings.</summary>
public sealed record ShareProfileResult
{
    /// <summary>True when the run may be shared (no violations).</summary>
    public required bool IsShareable { get; init; }

    /// <summary>Every rule the run violated. Empty when shareable.</summary>
    public required IReadOnlyList<ShareProfileViolation> Violations { get; init; }

    /// <summary>Non-blocking advisories (e.g. a <c>dev</c> engine version). Do not affect shareability.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>A single SHARE-PROFILE violation: a stable rule id and a human-readable reason.</summary>
public sealed record ShareProfileViolation(string RuleId, string Message);
