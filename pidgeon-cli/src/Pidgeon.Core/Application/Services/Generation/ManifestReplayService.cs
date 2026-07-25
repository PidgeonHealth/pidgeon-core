// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.Generation;

/// <summary>
/// The one replay path shared by <c>generate --from-manifest</c> and the <c>pidgeon run</c> verbs
/// (SHAREABLE_RUN_STANDARD.md §4.3). It rebuilds the resolved options from the manifest and regenerates,
/// so a manifest reproduces the same bytes regardless of which surface asked for the replay.
/// </summary>
public sealed class ManifestReplayService : IManifestReplayService
{
    private readonly IMessageGenerationService _generationService;

    public ManifestReplayService(IMessageGenerationService generationService)
    {
        _generationService = generationService ?? throw new ArgumentNullException(nameof(generationService));
    }

    public async Task<Result<ManifestReplayResult>> ReplayAsync(GenerationRunManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var result = await _generationService.GenerateSyntheticDataAsync(
            manifest.Standard, manifest.MessageType, manifest.Count, manifest.ToOptions());

        if (result.IsFailure)
            return result.Error;

        return Result<ManifestReplayResult>.Success(new ManifestReplayResult
        {
            Messages = result.Value,
            EffectiveSeed = manifest.RootSeed,
            DeterminismVersionMismatch = !manifest.IsCurrentDeterminismVersion,
            ManifestDeterminismVersion = manifest.DeterminismVersion,
            EngineDeterminismVersion = GenerationDeterminism.DeterminismVersion
        });
    }
}
