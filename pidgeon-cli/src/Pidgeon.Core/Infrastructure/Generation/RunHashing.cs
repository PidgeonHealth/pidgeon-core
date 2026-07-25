// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;

namespace Pidgeon.Core.Generation;

/// <summary>
/// The <c>utf8-message-sequence-v1</c> hash over a generated run's in-memory message sequence
/// (SHAREABLE_RUN_STANDARD.md §2.2). Each message is hashed as its raw UTF-8 bytes — no BOM, no added
/// trailing newline, no line-ending normalization, because the engine's <c>\r</c> HL7 segment separators
/// are part of the deterministic bytes and normalizing them would weaken the reproduction claim. The
/// output hash is the SHA-256 over the concatenation of the raw per-message digests, which makes
/// verification streamable: each message is hashed as it is produced, never buffering the whole run.
/// </summary>
public static class RunHashing
{
    /// <summary>Per-message digests are recorded in the envelope only when a run has at most this many messages.</summary>
    public const int MessageHashInclusionThreshold = 64;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Computes the run hash over the message sequence. The sequence is consumed exactly once, one message
    /// at a time, so the hash path never buffers the full run — only a single message's transient bytes and
    /// a 32-byte digest are live at any moment. Per-message hex digests are collected only when
    /// <paramref name="collectPerMessageHashes"/> is set (the caller sets it for small runs so a divergence
    /// can be localized); large runs skip the collection to stay memory-bounded.
    /// </summary>
    public static RunHashResult Compute(IEnumerable<string> messages, bool collectPerMessageHashes)
    {
        ArgumentNullException.ThrowIfNull(messages);

        using var outputHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        List<string>? perMessage = collectPerMessageHashes ? new List<string>() : null;
        Span<byte> digest = stackalloc byte[32];

        foreach (var message in messages)
        {
            var bytes = Utf8NoBom.GetBytes(message);
            SHA256.HashData(bytes, digest);
            outputHasher.AppendData(digest);
            perMessage?.Add(Convert.ToHexString(digest).ToLowerInvariant());
        }

        var outputDigest = outputHasher.GetHashAndReset();
        return new RunHashResult(Convert.ToHexString(outputDigest).ToLowerInvariant(), perMessage);
    }

    /// <summary>SHA-256 (lowercase hex) over one message's raw UTF-8 bytes.</summary>
    public static string HashMessage(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Convert.ToHexString(SHA256.HashData(Utf8NoBom.GetBytes(message))).ToLowerInvariant();
    }

    /// <summary>
    /// Returns the index of the first message whose per-message digest differs between two sequences, or
    /// <c>-1</c> when one is a prefix of the other and no shared index diverges. When the sequences differ
    /// only in length, the first index past the shorter sequence is the divergence point.
    /// </summary>
    public static int FirstDivergingIndex(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var shared = Math.Min(expected.Count, actual.Count);
        for (var i = 0; i < shared; i++)
        {
            if (!string.Equals(expected[i], actual[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return expected.Count == actual.Count ? -1 : shared;
    }
}

/// <summary>
/// The result of hashing a run: the lowercase-hex output hash, plus per-message hex digests when the run
/// was small enough to record them (null otherwise).
/// </summary>
public sealed record RunHashResult(string OutputHash, IReadOnlyList<string>? MessageHashes);
