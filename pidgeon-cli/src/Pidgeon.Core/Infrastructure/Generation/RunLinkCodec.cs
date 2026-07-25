// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

namespace Pidgeon.Core.Generation;

/// <summary>
/// The <c>r1.</c> link codec (SHAREABLE_RUN_STANDARD.md §6.2 / §6.3): a lossless, offline re-encoding of a
/// <see cref="RunEnvelope"/> as a compact, URL-safe string so a run travels as a link where a file cannot.
/// The payload is the minified <em>canonical</em> envelope JSON (sorted keys at every level) with
/// <c>verification.message_hashes</c> stripped, base64url-encoded (RFC 4648 §5, unpadded), behind the
/// literal <c>r1.</c> prefix that versions the codec independently of the envelope schema.
///
/// Two consumers share this contract byte-for-byte: this C# encoder/decoder (used by the CLI and the Bridge)
/// and the account-portal TypeScript decoder. They are held identical by the checked-in shared vectors
/// (SHAREABLE_RUN_STANDARD.md §6.3, R4). All work here is pure string + JSON — there is no network I/O,
/// ever (the offline invariant, §4.2 / §6.5): a link is used as an envelope <em>encoding</em>, never as an
/// address to fetch.
/// </summary>
public static class RunLinkCodec
{
    /// <summary>The codec-version prefix on every link. Bumping it versions the codec, not the envelope schema.</summary>
    public const string Prefix = "r1.";

    /// <summary>
    /// The short canonical run page a share URL points at: <c>pidgeon.health/run</c> redirects to the
    /// account-portal <c>/run</c> page that decodes + renders the artifact. The fragment (<c>#r1.…</c>) is
    /// never sent to the server — the page decodes it client-side — so a share URL carries the same no-PHI
    /// posture as the file. Older <c>account.pidgeon.health/run#r1.…</c> links still decode (the reader is
    /// host-agnostic); only the emitted host changed.
    /// </summary>
    public const string RunPageUrl = "https://pidgeon.health/run";

    /// <summary>
    /// The ceiling on the full <c>r1.&lt;payload&gt;</c> string (6 KB). Above it, encoding fails rather than
    /// emit a link that would break in chat clients; the file (L0) is always the fallback.
    /// </summary>
    public const int MaxLinkLength = 6 * 1024;

    private const string OversizeMessage = "artifact too large to link; share the file";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Encodes an envelope to an <c>r1.&lt;payload&gt;</c> link. The <c>message_hashes</c> are always stripped
    /// (§6.2), so a link-decoded envelope legitimately carries none. Fails with the oversize message when the
    /// resulting link exceeds <see cref="MaxLinkLength"/>.
    /// </summary>
    public static Result<string> Encode(RunEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var forLink = envelope with { Verification = envelope.Verification with { MessageHashes = null } };
        var canonicalJson = forLink.ToCanonicalJson();
        var link = Prefix + Base64UrlEncode(Utf8NoBom.GetBytes(canonicalJson));

        return link.Length > MaxLinkLength
            ? Result<string>.Failure(Error.Create("RUN_LINK_TOO_LARGE", OversizeMessage))
            : Result<string>.Success(link);
    }

    /// <summary>
    /// Encodes an envelope to a full canonical share URL (<c>https://pidgeon.health/run#r1.…</c>).
    /// The size ceiling applies to the <c>r1.</c> payload, so this fails for the same oversize reason.
    /// </summary>
    public static Result<string> EncodeShareUrl(RunEnvelope envelope)
        => Encode(envelope).Map(link => RunPageUrl + "#" + link);

    /// <summary>
    /// Decodes an <c>r1.</c> link back to an envelope. Accepts either a bare <c>r1.&lt;payload&gt;</c> string
    /// or a full share URL whose fragment is <c>#r1.&lt;payload&gt;</c> (the fragment is parsed locally). The
    /// reconstructed envelope carries no <c>message_hashes</c> — they were stripped at encode time, which is
    /// valid; verification still works from it (§6.3). Returns a failure (never throws) for a malformed link.
    /// </summary>
    public static Result<RunEnvelope> Decode(string linkOrUrl)
    {
        if (string.IsNullOrWhiteSpace(linkOrUrl))
            return Result<RunEnvelope>.Failure(Error.Create("RUN_LINK_EMPTY", "Run link is empty."));

        var text = linkOrUrl.Trim();
        var hashIndex = text.IndexOf('#');
        if (hashIndex >= 0)
            text = text[(hashIndex + 1)..];

        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
            return Result<RunEnvelope>.Failure(Error.Create(
                "RUN_LINK_UNRECOGNIZED",
                $"Not an r1. run link (expected the '{Prefix}' prefix, or a share URL with a '#{Prefix}…' fragment)."));

        byte[] jsonBytes;
        try
        {
            jsonBytes = Base64UrlDecode(text[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            return Result<RunEnvelope>.Failure(Error.Create("RUN_LINK_MALFORMED", $"Run link payload is not valid base64url: {ex.Message}"));
        }

        var json = Utf8NoBom.GetString(jsonBytes);
        try
        {
            return Result<RunEnvelope>.Success(RunEnvelope.FromJson(json));
        }
        catch (Exception ex)
        {
            return Result<RunEnvelope>.Failure(Error.Create("RUN_LINK_MALFORMED", $"Run link did not decode to a valid envelope: {ex.Message}"));
        }
    }

    /// <summary>
    /// True when an artifact argument is an <c>r1.</c> link rather than a file path: it starts with the
    /// <c>r1.</c> prefix, embeds a <c>#r1.</c> fragment, or is an http(s) URL. The run verbs use this to route
    /// the argument to <see cref="Decode"/> versus a file read (SHAREABLE_RUN_STANDARD.md §6.3).
    /// </summary>
    public static bool IsLink(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        var trimmed = candidate.Trim();
        return trimmed.StartsWith(Prefix, StringComparison.Ordinal)
            || trimmed.Contains("#" + Prefix, StringComparison.Ordinal)
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Base64url per RFC 4648 §5: the URL-safe alphabet (<c>-</c>/<c>_</c>) with no <c>=</c> padding.</summary>
    internal static string Base64UrlEncode(byte[] bytes)
    {
        var standard = Convert.ToBase64String(bytes);
        var builder = new StringBuilder(standard.Length);
        foreach (var c in standard)
        {
            switch (c)
            {
                case '+': builder.Append('-'); break;
                case '/': builder.Append('_'); break;
                case '=': break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Inverse of <see cref="Base64UrlEncode"/>. Rejects padding, whitespace, and standard-base64 characters
    /// (<c>+</c>/<c>/</c>/<c>=</c>) so a mangled or padded link fails cleanly rather than silently mis-decoding.
    /// </summary>
    internal static byte[] Base64UrlDecode(string payload)
    {
        var builder = new StringBuilder(payload.Length + 3);
        foreach (var c in payload)
        {
            switch (c)
            {
                case '-': builder.Append('+'); break;
                case '_': builder.Append('/'); break;
                case '=': throw new FormatException("base64url payload must be unpadded.");
                default:
                    if (!IsBase64UrlChar(c))
                        throw new FormatException($"invalid base64url character '{c}'.");
                    builder.Append(c);
                    break;
            }
        }

        switch (builder.Length % 4)
        {
            case 2: builder.Append("=="); break;
            case 3: builder.Append('='); break;
            case 1: throw new FormatException("invalid base64url length.");
        }

        return Convert.FromBase64String(builder.ToString());
    }

    private static bool IsBase64UrlChar(char c)
        => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9');
}
