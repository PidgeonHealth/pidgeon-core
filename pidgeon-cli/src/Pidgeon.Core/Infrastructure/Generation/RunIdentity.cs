// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Security.Cryptography;
using System.Text;

namespace Pidgeon.Core.Generation;

/// <summary>
/// Content-addressed run identity (SHAREABLE_RUN_STANDARD.md §2.3):
/// <c>run_id = lowercase-hex( SHA-256( UTF-8(canonical_manifest_json) ‖ output_hash_bytes ) )[0..16)</c>.
/// No server mints identity — the same run shared twice yields the same id, a forked run a new one.
/// Identity derives from the <em>canonical</em> manifest JSON (sorted keys, minified) so two runs equal by
/// value cannot mint different ids through dictionary insertion-order drift. The 64-bit handle is a share
/// id, not a security boundary; the full output hash is the proof.
/// </summary>
public static class RunIdentity
{
    /// <summary>Number of leading hex characters of the SHA-256 digest that form the run id.</summary>
    public const int RunIdHexLength = 16;

    /// <summary>
    /// Computes the run id from a manifest and the run's output hash. <paramref name="outputHashHex"/> is the
    /// lowercase-hex SHA-256 from <see cref="RunHashing"/>; its raw digest bytes (not the hex string) are
    /// concatenated after the canonical manifest JSON before the final hash.
    /// </summary>
    public static string Compute(GenerationRunManifest manifest, string outputHashHex)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrEmpty(outputHashHex);

        var canonicalBytes = Encoding.UTF8.GetBytes(manifest.ToCanonicalJson());
        var outputHashBytes = Convert.FromHexString(outputHashHex);

        var combined = new byte[canonicalBytes.Length + outputHashBytes.Length];
        Buffer.BlockCopy(canonicalBytes, 0, combined, 0, canonicalBytes.Length);
        Buffer.BlockCopy(outputHashBytes, 0, combined, canonicalBytes.Length, outputHashBytes.Length);

        var digest = SHA256.HashData(combined);
        return Convert.ToHexString(digest).ToLowerInvariant()[..RunIdHexLength];
    }
}
