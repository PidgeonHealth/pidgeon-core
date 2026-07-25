// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.Common.HL7;

/// <summary>
/// The one place that knows how an HL7 file splits into per-message units, on the
/// MSH-per-message rule (HL7 batch protocol, 2.10.3). Both the validation plugin
/// (segment-level partitioning) and the vendor-profile validators (string-level
/// splitting) route through here so the boundary rule is defined once: leading
/// non-MSH content stays with message 1; message k spans its MSH to the next.
/// </summary>
public static class Hl7MessagePartitioner
{
    private const string MshSegmentCode = "MSH";

    /// <summary>
    /// Splits a parsed segment list into per-message partitions, one per MSH. With zero or
    /// one MSH the entire list is a single partition (the exact input, same reference — the
    /// single-message fast path). With N MSH segments, partition 1 spans from the start of the
    /// file to the second MSH (leading non-MSH segments stay with message 1, preserving
    /// HL7-STRUCT-001 header-first semantics), and partition k spans from its MSH to the next.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<RawHl7Segment>> PartitionByMessageHeader(
        IReadOnlyList<RawHl7Segment> segments)
    {
        var mshIndices = new List<int>();
        for (int i = 0; i < segments.Count; i++)
        {
            if (segments[i].Code == MshSegmentCode)
                mshIndices.Add(i);
        }

        if (mshIndices.Count <= 1)
            return new List<IReadOnlyList<RawHl7Segment>> { segments };

        var partitions = new List<IReadOnlyList<RawHl7Segment>>(mshIndices.Count);
        var starts = new List<int>(mshIndices.Count) { 0 };
        starts.AddRange(mshIndices.Skip(1));

        for (int b = 0; b < starts.Count; b++)
        {
            int start = starts[b];
            int end = b + 1 < starts.Count ? starts[b + 1] : segments.Count;

            var partition = new List<RawHl7Segment>(end - start);
            for (int i = start; i < end; i++)
                partition.Add(segments[i]);

            partitions.Add(partition);
        }

        return partitions;
    }

    /// <summary>
    /// Splits raw HL7 file text into per-message substrings on the same MSH-boundary rule as
    /// <see cref="PartitionByMessageHeader"/>. With zero or one MSH the whole message is
    /// returned unchanged (single element), so single-message callers keep their exact prior
    /// behavior; with N MSH lines, leading lines stay with message 1 and message k spans its
    /// MSH line to the next. Segments are rejoined with CR (the HL7 segment terminator);
    /// callers re-split on CR/LF, so the join is lossless for their purposes.
    /// </summary>
    public static IReadOnlyList<string> SplitMessages(string message)
    {
        if (string.IsNullOrEmpty(message))
            return new[] { message };

        var lines = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var mshLineIndices = new List<int>();
        for (int i = 0; i < lines.Length; i++)
        {
            // Mirror the segment-level code derivation exactly: the code is the text before the
            // first field separator, so MSH boundaries agree between the two entry points.
            if (lines[i].Trim().Split('|')[0] == MshSegmentCode)
                mshLineIndices.Add(i);
        }

        if (mshLineIndices.Count <= 1)
            return new[] { message };

        var starts = new List<int>(mshLineIndices.Count) { 0 };
        starts.AddRange(mshLineIndices.Skip(1));

        var messages = new List<string>(starts.Count);
        for (int b = 0; b < starts.Count; b++)
        {
            int start = starts[b];
            int end = b + 1 < starts.Count ? starts[b + 1] : lines.Length;
            messages.Add(string.Join("\r", lines[start..end]));
        }

        return messages;
    }
}
