// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pidgeon.Core.Generation;

/// <summary>
/// The <c>.pidgeonrun</c> shareable-run envelope (SHAREABLE_RUN_STANDARD.md §2). It wraps the shipped
/// <see cref="GenerationRunManifest"/> verbatim and adds exactly the three things sharing needs that
/// reproduction does not: proof (<see cref="Verification"/>), humanity (<see cref="Meta"/>), and genealogy
/// (<see cref="Provenance"/>). The manifest gains zero fields — it is a round-trip-tested repro contract, and
/// mutating it would fork the repro unit.
///
/// A bare manifest JSON remains valid input to every run consumer (see <see cref="RunArtifactReader"/>);
/// detection is structural — an envelope carries <c>format_version</c> + <c>manifest</c> at the top level,
/// a bare manifest carries <c>root_seed</c>.
/// </summary>
public sealed record RunEnvelope
{
    /// <summary>Envelope schema version. This standard defines version 1.</summary>
    public int FormatVersion { get; init; } = 1;

    /// <summary>Content-addressed run id (§2.3). Never minted by a server.</summary>
    public required string RunId { get; init; }

    /// <summary>The embedded generation manifest, byte-compatible with <see cref="GenerationRunManifest.ToJson"/>.</summary>
    public required GenerationRunManifest Manifest { get; init; }

    /// <summary>Reproduction proof: the hashing scheme and the output hash over the message sequence.</summary>
    public required RunVerification Verification { get; init; }

    /// <summary>Human-facing metadata (title, description, created-at, optional created-by).</summary>
    public required RunMeta Meta { get; init; }

    /// <summary>Lineage of a forked run; absent on original runs.</summary>
    public RunProvenance? Provenance { get; init; }

    private static readonly JsonSerializerOptions DisplayOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Serializes the envelope to snake_case, indented JSON (the display / on-disk form).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, DisplayOptions);

    /// <summary>
    /// Serializes the envelope to canonical JSON: the same snake_case field set and enum/null rules as
    /// <see cref="ToJson"/>, but keys sorted ordinally at every level and no whitespace. This is the form the
    /// <c>r1.</c> link codec (SHAREABLE_RUN_STANDARD.md §6.2) base64url-encodes, so two envelopes equal by
    /// value encode to the same payload regardless of dictionary insertion order in the embedded manifest maps.
    /// </summary>
    public string ToCanonicalJson() => CanonicalJson.Serialize(this, DisplayOptions);

    /// <summary>Reads an envelope back from JSON. Throws for malformed or non-envelope JSON.</summary>
    public static RunEnvelope FromJson(string json)
        => JsonSerializer.Deserialize<RunEnvelope>(json, DisplayOptions)
           ?? throw new ArgumentException("Envelope JSON did not deserialize to an envelope.", nameof(json));
}

/// <summary>Reproduction proof for a run (SHAREABLE_RUN_STANDARD.md §2.2).</summary>
public sealed record RunVerification
{
    /// <summary>Hash algorithm; <c>"sha256"</c> in format 1.</summary>
    public string HashAlgorithm { get; init; } = "sha256";

    /// <summary>Canonicalization scheme; <c>"utf8-message-sequence-v1"</c> in format 1.</summary>
    public string Canonicalization { get; init; } = "utf8-message-sequence-v1";

    /// <summary>Lowercase-hex SHA-256 over the run's message sequence.</summary>
    public required string OutputHash { get; init; }

    /// <summary>Per-message hex digests, included when the run has at most <see cref="RunHashing.MessageHashInclusionThreshold"/> messages.</summary>
    public IReadOnlyList<string>? MessageHashes { get; init; }
}

/// <summary>Human-facing metadata for a shared run. Every field is optional except the creation timestamp.</summary>
public sealed record RunMeta
{
    /// <summary>Short title (≤ 120 chars, single line). User-typed; never auto-filled.</summary>
    public string? Title { get; init; }

    /// <summary>Longer description (≤ 500 chars, newlines allowed). User-typed; never auto-filled.</summary>
    public string? Description { get; init; }

    /// <summary>ISO 8601 UTC creation instant.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>User-typed author string. Never auto-filled from account identity.</summary>
    public string? CreatedBy { get; init; }
}

/// <summary>Genealogy of a forked run (SHAREABLE_RUN_STANDARD.md §5.3).</summary>
public sealed record RunProvenance
{
    /// <summary>The parent run this was forked from.</summary>
    public required RunForkedFrom ForkedFrom { get; init; }
}

/// <summary>A single parent hop: the parent's run id and the determinism version it was written under.</summary>
public sealed record RunForkedFrom
{
    /// <summary>The parent's content-addressed run id.</summary>
    public required string RunId { get; init; }

    /// <summary>The determinism-contract version the parent was written under.</summary>
    public required int DeterminismVersion { get; init; }
}
