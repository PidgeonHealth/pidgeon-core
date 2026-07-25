// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;

namespace Pidgeon.Core.Infrastructure.Standards.Common.HL7;

/// <summary>
/// A structurally parsed HL7 segment: the segment code and the raw pipe-split fields,
/// untyped and unescaped. <c>Fields[0]</c> is the segment code itself, so MSH-N maps to
/// <c>Fields[N-1]</c> for N ≥ 2 (MSH-1 is the field separator character).
/// </summary>
public sealed record RawHl7Segment(string Code, string[] Fields);

/// <summary>
/// The HL7 v2 encoding characters declared in MSH-1/MSH-2.
/// </summary>
public sealed record Hl7Delimiters(
    char FieldSeparator = '|',
    char ComponentSeparator = '^',
    char RepetitionSeparator = '~',
    char EscapeCharacter = '\\',
    char SubcomponentSeparator = '&')
{
    /// <summary>
    /// Extracts the declared delimiters from a raw MSH segment line
    /// (<c>MSH|^~\&amp;|…</c>). Falls back to the standard defaults for any
    /// character the line is too short to declare.
    /// </summary>
    public static Hl7Delimiters FromMsh(string mshSegment)
    {
        if (string.IsNullOrEmpty(mshSegment) || !mshSegment.StartsWith("MSH", StringComparison.Ordinal))
            throw new ArgumentException("Invalid MSH segment", nameof(mshSegment));

        if (mshSegment.Length < 8)
            return new Hl7Delimiters();

        return new Hl7Delimiters(
            FieldSeparator: mshSegment[3],
            ComponentSeparator: mshSegment[4],
            RepetitionSeparator: mshSegment[5],
            EscapeCharacter: mshSegment[6],
            SubcomponentSeparator: mshSegment[7]);
    }
}

/// <summary>
/// The one structural (wire to raw segments) HL7 parse path. Splits a message into
/// <see cref="RawHl7Segment"/>s and recovers the MSH-declared version and trigger event:
/// no schema knowledge, no typing, no policy. Consumers layer their own semantics on top:
/// the validation plugin applies its version fallback; the oracle-driven model parse
/// builds the typed model from the same raw segments.
/// </summary>
public interface IHl7StructuralParser
{
    /// <summary>
    /// Splits a message into raw segments: one per non-empty line, fields pipe-split,
    /// lines trimmed. Lines without a segment code are dropped.
    /// </summary>
    IReadOnlyList<RawHl7Segment> ParseSegments(string message);

    /// <summary>
    /// Recovers the version declared in MSH-12 (first component of the VID composite,
    /// trimmed). Returns null when there is no MSH segment, MSH-12 is absent, or it is empty —
    /// the caller owns the fallback policy.
    /// </summary>
    string? GetDeclaredVersion(IReadOnlyList<RawHl7Segment> segments);

    /// <summary>
    /// Derives the trigger-event lookup code from MSH-9: "ADT^A01" → "ADT_A01"; a
    /// single-part message type passes through unchanged. Returns null when there is no
    /// MSH segment or MSH-9 is absent or empty.
    /// </summary>
    string? GetTriggerEventCode(IReadOnlyList<RawHl7Segment> segments);

    /// <summary>
    /// Resolves HL7 escape sequences (\F\ \S\ \T\ \R\ \E\ and \Xnn\ hex escapes) to their
    /// literal characters.
    /// </summary>
    string Unescape(string value, Hl7Delimiters? delimiters = null);

    /// <summary>
    /// Escapes delimiter and control characters in a value for HL7 encoding.
    /// </summary>
    string Escape(string value, Hl7Delimiters? delimiters = null);
}

/// <summary>
/// Default <see cref="IHl7StructuralParser"/>. Segment splitting reproduces the validation
/// plugin's historical inline parse exactly (split on CR/LF, trim, pipe-split, drop empty
/// codes); the escape helpers carry the spec-correct sequences from the retired HL7Parser.
/// </summary>
public class Hl7StructuralParser : IHl7StructuralParser
{
    private const string MshSegmentCode = "MSH";
    private static readonly Regex HexEscapePattern = new(@"\\X([0-9A-F]{2,4})\\", RegexOptions.Compiled);

    public IReadOnlyList<RawHl7Segment> ParseSegments(string message)
    {
        var segments = new List<RawHl7Segment>();
        if (string.IsNullOrEmpty(message))
            return segments;

        var lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var fields = trimmed.Split('|');
            if (fields.Length > 0 && !string.IsNullOrEmpty(fields[0]))
            {
                segments.Add(new RawHl7Segment(fields[0], fields));
            }
        }

        return segments;
    }

    public string? GetDeclaredVersion(IReadOnlyList<RawHl7Segment> segments)
    {
        var header = segments.FirstOrDefault(s => s.Code == MshSegmentCode);

        // MSH-12 is Fields[11] in the pipe-split array: Fields[0]="MSH",
        // Fields[1] is the encoding characters (MSH-2), so MSH-N maps to
        // Fields[N-1] for N >= 2.
        if (header == null || header.Fields.Length <= 11)
            return null;

        // MSH-12 is a VID composite; the version ID is its first component.
        var versionId = header.Fields[11].Split('^')[0].Trim();
        return string.IsNullOrEmpty(versionId) ? null : versionId;
    }

    public string? GetTriggerEventCode(IReadOnlyList<RawHl7Segment> segments)
    {
        var header = segments.FirstOrDefault(s => s.Code == MshSegmentCode);
        if (header == null) return null;

        // MSH-9 (message type) is Fields[8] in the pipe-split array.
        if (header.Fields.Length <= 8) return null;

        var messageType = header.Fields[8];
        if (string.IsNullOrEmpty(messageType)) return null;

        // Message type is typically "ADT^A01" — convert to "ADT_A01" for provider lookup.
        var parts = messageType.Split('^');
        if (parts.Length >= 2)
        {
            return $"{parts[0]}_{parts[1]}";
        }

        return messageType;
    }

    public string Unescape(string value, Hl7Delimiters? delimiters = null)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var d = delimiters ?? new Hl7Delimiters();
        var result = value
            .Replace("\\F\\", d.FieldSeparator.ToString())
            .Replace("\\S\\", d.ComponentSeparator.ToString())
            .Replace("\\T\\", d.SubcomponentSeparator.ToString())
            .Replace("\\R\\", d.RepetitionSeparator.ToString())
            .Replace("\\E\\", d.EscapeCharacter.ToString());

        // Hex character escapes (e.g., \X0D\ for carriage return)
        return HexEscapePattern.Replace(result, m =>
        {
            var charCode = Convert.ToInt32(m.Groups[1].Value, 16);
            return ((char)charCode).ToString();
        });
    }

    public string Escape(string value, Hl7Delimiters? delimiters = null)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var d = delimiters ?? new Hl7Delimiters();

        // Escape in specific order to avoid double-escaping
        return value
            .Replace(d.EscapeCharacter.ToString(), "\\E\\")
            .Replace(d.FieldSeparator.ToString(), "\\F\\")
            .Replace(d.ComponentSeparator.ToString(), "\\S\\")
            .Replace(d.SubcomponentSeparator.ToString(), "\\T\\")
            .Replace(d.RepetitionSeparator.ToString(), "\\R\\")
            .Replace("\r", "\\X0D\\")
            .Replace("\n", "\\X0A\\");
    }
}
