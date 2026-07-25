// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using Pidgeon.Core.Infrastructure.Standards.Common.HL7.Utilities;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default <see cref="ITemporalCoherencePass"/>. Reads MSH-7 and every clinical-event
/// timestamp from the assembled message; when any event post-dates MSH-7 it shifts the
/// whole clinical timeline backward by a single offset so the latest event lands exactly
/// at MSH-7. One uniform shift keeps the resolver's relative ordering intact (it only
/// moves events earlier together, never reorders them).
///
/// Message intent matters. Transaction/event times — ORC-9 (order transaction),
/// EVN-2 (event), PV1-44/45 (admit/discharge), RXA-3 (administration), DG1-5
/// (diagnosis) — record something that already happened, so they are clamped for every
/// message type. The requested/scheduled specimen and result times an order carries —
/// OBR-7, OBR-22, OBX-14 — may legitimately post-date MSH-7 in an order message (a
/// collection ordered now, to be drawn later), so they are excluded from the clamp for
/// order messages (RDE / ORM / OMx …) and clamped only for result/event messages where
/// they record an actual collection.
///
/// Thread-safety: no mutable instance state (the field tables are static readonly), so the
/// DI registration is a Singleton even though the consuming composer is Scoped.
/// </summary>
public class TemporalCoherencePass : ITemporalCoherencePass
{
    // Clinical-event timestamp fields, by segment code and 1-based field position.
    // Requestable = the field may carry a legitimately-future requested/scheduled time in
    // an order message, so the clamp skips it there.
    private static readonly (string Segment, int Field, bool Requestable)[] EventFields =
    {
        ("EVN", 2, false),
        ("PV1", 44, false),
        ("PV1", 45, false),
        ("DG1", 5, false),
        ("ORC", 9, false),
        ("RXA", 3, false),
        ("RXE", 32, false),
        ("OBR", 7, true),
        ("OBR", 22, true),
        ("OBX", 14, true),
    };

    public string Apply(string assembledMessage)
    {
        if (string.IsNullOrEmpty(assembledMessage))
            return assembledMessage;

        // Preserve the original line breaks exactly so the rewrite is otherwise byte-identical.
        var lineEnding = assembledMessage.Contains("\r\n") ? "\r\n"
            : assembledMessage.Contains('\r') ? "\r"
            : "\n";
        var lines = assembledMessage.Split(lineEnding);

        var msh7 = ReadMsh7(lines);
        if (msh7 is null)
            return assembledMessage;

        var isOrderMessage = IsOrderMessage(lines);

        // Find the latest in-scope event time across all segments. Requestable times are
        // out of scope for order messages — they may legitimately post-date MSH-7 there.
        DateTime? latest = null;
        for (int li = 0; li < lines.Length; li++)
        {
            foreach (var (segment, field, requestable) in EventFields)
            {
                if (isOrderMessage && requestable)
                    continue;

                var value = ReadField(lines[li], segment, field);
                var parsed = ParseTimestamp(value);
                if (parsed is { } t && (latest is null || t > latest))
                    latest = t;
            }
        }

        if (latest is not { } latestEvent || latestEvent <= msh7.Value)
            return assembledMessage; // nothing in scope post-dates the message — leave it untouched.

        var shift = latestEvent - msh7.Value;

        // Apply the same backward shift to every in-scope event timestamp.
        for (int li = 0; li < lines.Length; li++)
        {
            foreach (var (segment, field, requestable) in EventFields)
            {
                if (isOrderMessage && requestable)
                    continue;

                lines[li] = ShiftField(lines[li], segment, field, shift);
            }
        }

        return string.Join(lineEnding, lines);
    }

    private static bool IsOrderMessage(string[] lines)
    {
        foreach (var line in lines)
        {
            if (!line.StartsWith("MSH", StringComparison.Ordinal))
                continue;

            // MSH-9 is the 9th HL7 field; in the pipe-split array MSH-1 is the field
            // separator itself, so Fields[8] is MSH-9 (e.g. "ORU^R01").
            var fields = line.Split('|');
            if (fields.Length <= 8)
                return false;

            var messageCode = fields[8].Split('^')[0].Trim();
            return Hl7MessageIntent.IsOrderMessageType(messageCode);
        }

        return false;
    }

    private static DateTime? ReadMsh7(string[] lines)
    {
        foreach (var line in lines)
        {
            if (!line.StartsWith("MSH", StringComparison.Ordinal))
                continue;

            var fields = line.Split('|');
            // MSH-7 maps to Fields[6]: Fields[0]="MSH", Fields[1]=encoding chars (MSH-2).
            if (fields.Length <= 6)
                return null;

            return ParseTimestamp(fields[6]);
        }

        return null;
    }

    // Returns the raw value of SEG-field, or null when the segment/field is absent.
    private static string? ReadField(string line, string segment, int field)
    {
        if (!line.StartsWith(segment + "|", StringComparison.Ordinal))
            return null;

        var fields = line.Split('|');
        // Non-MSH segments map SEG-N to Fields[N]. (MSH is never passed here.)
        if (field < 1 || field >= fields.Length)
            return null;

        var value = fields[field];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    // Rewrites SEG-field with its timestamp shifted backward by 'shift', preserving any
    // trailing components (e.g. "^precision") the field carried. No-op if absent/unparseable.
    private static string ShiftField(string line, string segment, int field, TimeSpan shift)
    {
        if (!line.StartsWith(segment + "|", StringComparison.Ordinal))
            return line;

        var fields = line.Split('|');
        if (field < 1 || field >= fields.Length)
            return line;

        var raw = fields[field];
        if (string.IsNullOrEmpty(raw))
            return line;

        var components = raw.Split('^');
        var parsed = ParseTimestamp(components[0]);
        if (parsed is not { } original)
            return line;

        components[0] = FormatTimestamp(original - shift, components[0]);
        fields[field] = string.Join('^', components);
        return string.Join('|', fields);
    }

    // Parses the leading DTM of a timestamp this pass reads from generator output, which is
    // always local YYYYMMDD[HHMMSS] with no timezone offset or fractional second — so the four
    // precision formats below are exhaustive for this input. (SemanticHl7Time covers the wider
    // offset/fraction forms for the validator's parse of arbitrary inbound messages; this pass
    // only ever re-reads what the composer just wrote, so it stays narrow and offset-free.)
    private static DateTime? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var dtm = value.Split('^')[0].Trim();
        if (dtm.Length < 8)
            return null;

        // Trim to the longest recognized precision and parse.
        string[] formats =
        {
            "yyyyMMddHHmmss", "yyyyMMddHHmm", "yyyyMMddHH", "yyyyMMdd"
        };

        foreach (var format in formats)
        {
            if (dtm.Length >= format.Length &&
                DateTime.TryParseExact(dtm[..format.Length], format,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed;
        }

        return null;
    }

    // Re-emits at the same precision the original carried so the rewrite does not widen
    // or narrow a date-only / minute-precision timestamp.
    private static string FormatTimestamp(DateTime value, string original)
    {
        var dtmLength = original.Trim().Length;
        var format = dtmLength switch
        {
            >= 14 => "yyyyMMddHHmmss",
            12 or 13 => "yyyyMMddHHmm",
            10 or 11 => "yyyyMMddHH",
            _ => "yyyyMMdd",
        };
        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}
