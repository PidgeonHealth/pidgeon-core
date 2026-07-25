// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Pidgeon.Core.Generation;

/// <summary>
/// A loaded run artifact: always a manifest, plus the surrounding envelope when the source carried one.
/// A bare manifest loads with <see cref="Envelope"/> null — it can be replayed and shown, but not verified
/// (it carries no proof block).
/// </summary>
public sealed record RunArtifact
{
    /// <summary>The generation manifest (present for both envelope and bare-manifest inputs).</summary>
    public required GenerationRunManifest Manifest { get; init; }

    /// <summary>The full envelope when the source was a <c>.pidgeonrun</c>; null when the source was a bare manifest.</summary>
    public RunEnvelope? Envelope { get; init; }

    /// <summary>True when the source was a bare manifest (no verification block).</summary>
    public bool IsBareManifest => Envelope is null;
}

/// <summary>
/// Structural reader for run artifacts (SHAREABLE_RUN_STANDARD.md §2.1 backward-compatibility rule).
/// Accepts either a <c>.pidgeonrun</c> envelope (<c>format_version</c> + <c>manifest</c> at the top level)
/// or a bare <see cref="GenerationRunManifest"/> (<c>root_seed</c> at the top level), so every run consumer
/// keeps working on legacy bare manifests. Parsing is pure string work — no I/O, no network.
/// </summary>
public static class RunArtifactReader
{
    /// <summary>
    /// Reads an artifact from its JSON text, sniffing envelope vs bare manifest structurally. Returns a
    /// failure (never throws) for empty, malformed, or unrecognized JSON so callers can map it to a single
    /// invalid-artifact exit code.
    /// </summary>
    public static Result<RunArtifact> Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Result<RunArtifact>.Failure(Error.Create("RUN_ARTIFACT_EMPTY", "Artifact is empty."));

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return Result<RunArtifact>.Failure(Error.Create("RUN_ARTIFACT_INVALID", $"Not valid JSON: {ex.Message}"));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Result<RunArtifact>.Failure(Error.Create("RUN_ARTIFACT_INVALID", "Artifact JSON is not an object."));

            var looksLikeEnvelope = root.TryGetProperty("format_version", out _) && root.TryGetProperty("manifest", out _);
            var looksLikeBareManifest = root.TryGetProperty("root_seed", out _);

            if (looksLikeEnvelope)
            {
                try
                {
                    var envelope = RunEnvelope.FromJson(json);
                    return Result<RunArtifact>.Success(new RunArtifact { Manifest = envelope.Manifest, Envelope = envelope });
                }
                catch (Exception ex)
                {
                    return Result<RunArtifact>.Failure(Error.Create("RUN_ARTIFACT_INVALID", $"Malformed run envelope: {ex.Message}"));
                }
            }

            if (looksLikeBareManifest)
            {
                try
                {
                    var manifest = GenerationRunManifest.FromJson(json);
                    return Result<RunArtifact>.Success(new RunArtifact { Manifest = manifest, Envelope = null });
                }
                catch (Exception ex)
                {
                    return Result<RunArtifact>.Failure(Error.Create("RUN_ARTIFACT_INVALID", $"Malformed run manifest: {ex.Message}"));
                }
            }

            return Result<RunArtifact>.Failure(Error.Create(
                "RUN_ARTIFACT_UNRECOGNIZED",
                "Not a .pidgeonrun envelope or a bare run manifest (expected top-level 'format_version'+'manifest', or 'root_seed')."));
        }
    }

    /// <summary>
    /// Reads an artifact from an <c>r1.</c> link or a share URL (SHAREABLE_RUN_STANDARD.md §6.3): the argument
    /// is decoded locally via <see cref="RunLinkCodec"/> — pure string work, no network. The resulting
    /// artifact always carries an envelope (a link is always an envelope encoding), though with no
    /// <c>message_hashes</c> since the link form strips them. Returns a failure for a malformed link so the
    /// caller can map it to the single invalid-artifact exit code.
    /// </summary>
    public static Result<RunArtifact> ReadLink(string linkOrUrl)
    {
        var decoded = RunLinkCodec.Decode(linkOrUrl);
        if (decoded.IsFailure)
            return decoded.Error;

        var envelope = decoded.Value;
        return Result<RunArtifact>.Success(new RunArtifact { Manifest = envelope.Manifest, Envelope = envelope });
    }
}
