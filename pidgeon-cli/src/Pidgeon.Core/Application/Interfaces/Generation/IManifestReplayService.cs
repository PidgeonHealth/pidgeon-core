// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Interfaces.Generation;

/// <summary>
/// Regenerates a run from its manifest (SHAREABLE_RUN_STANDARD.md §4.3). This is the single replay path
/// that both <c>generate --from-manifest</c> and the <c>pidgeon run</c> verbs (replay / verify / fork)
/// call, so the surfaces cannot drift into producing different bytes from the same manifest.
/// </summary>
public interface IManifestReplayService
{
    /// <summary>
    /// Regenerates the run described by <paramref name="manifest"/>. Returns the reproduced message
    /// sequence plus the effective seed and a determinism-version-skew flag the caller surfaces. A
    /// generation failure comes back as a <see cref="Result{T}"/> failure, never an exception.
    /// </summary>
    Task<Result<ManifestReplayResult>> ReplayAsync(GenerationRunManifest manifest, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a manifest replay: the reproduced messages and the version context callers report.</summary>
public sealed record ManifestReplayResult
{
    /// <summary>The reproduced message sequence, in order — the bytes SHAREABLE_RUN_STANDARD.md §2.2 hashes.</summary>
    public required IReadOnlyList<string> Messages { get; init; }

    /// <summary>The effective root seed the run used.</summary>
    public required int EffectiveSeed { get; init; }

    /// <summary>True when the manifest's determinism version differs from the running engine's.</summary>
    public required bool DeterminismVersionMismatch { get; init; }

    /// <summary>The determinism-contract version recorded in the manifest.</summary>
    public required int ManifestDeterminismVersion { get; init; }

    /// <summary>The determinism-contract version of the running engine.</summary>
    public required int EngineDeterminismVersion { get; init; }
}
