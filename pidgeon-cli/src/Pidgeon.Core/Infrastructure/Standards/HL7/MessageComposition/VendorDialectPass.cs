// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.VendorIntelligence;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default <see cref="IVendorDialectPass"/>. Reads the active vendor profile off the context and
/// rewrites the assembled message to its conventions:
/// <list type="bullet">
/// <item>MSH whole-field overrides from the profile's MSH <c>expected_value</c> fields (e.g. MSH-3
/// sending application = "EPIC", MSH-11 processing id = "P").</item>
/// <item>Segment ordering to the profile's <c>segment_order</c>, applied only when that order covers
/// every non-MSH segment present (otherwise left untouched — an unlisted segment has no safe slot).</item>
/// </list>
/// Vendor Z-segment emission lands on this same seam in S2. No vendor-name branches: every decision
/// reads the profile data. Stateless (Singleton); a no-op when no profile is active, so default output
/// is byte-for-byte identical.
/// </summary>
public class VendorDialectPass : IVendorDialectPass
{
    private readonly IZSegmentComposer _zSegmentComposer;

    public VendorDialectPass(IZSegmentComposer zSegmentComposer)
    {
        _zSegmentComposer = zSegmentComposer ?? throw new ArgumentNullException(nameof(zSegmentComposer));
    }

    public string Apply(string assembledMessage, SegmentGenerationContext context)
    {
        var profile = context.VendorProfile;
        if (profile is null || string.IsNullOrEmpty(assembledMessage))
            return assembledMessage;

        // Preserve the original line ending exactly so a no-shape message is byte-identical.
        var lineEnding = assembledMessage.Contains("\r\n") ? "\r\n"
            : assembledMessage.Contains('\r') ? "\r"
            : "\n";
        var lines = assembledMessage.Split(lineEnding).ToList();

        ApplyMshOverrides(lines, profile);
        ReorderSegments(lines, profile);
        AppendZSegments(lines, profile, context);

        return string.Join(lineEnding, lines);
    }

    // Vendor Z-segments trail the standard segments — interface engines expect Z-segments at the end of
    // the message, and a mid-message Z-segment trips strict schema validation (INTEROP-RESEARCH epic.md).
    private void AppendZSegments(List<string> lines, VendorInterfaceProfile profile, SegmentGenerationContext context)
    {
        lines.AddRange(_zSegmentComposer.Compose(profile, context));
    }

    // Whole-field MSH overrides from the profile's MSH expected values. MSH-N maps to the pipe-split
    // index N-1 (index 0 = "MSH", index 1 = encoding characters); MSH-1/MSH-2 are never rewritten.
    private static void ApplyMshOverrides(List<string> lines, VendorInterfaceProfile profile)
    {
        if (!profile.Segments.TryGetValue("MSH", out var msh))
            return;

        for (int i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("MSH", StringComparison.Ordinal))
                continue;

            var fields = lines[i].Split('|');
            foreach (var (fieldKey, spec) in msh.Fields)
            {
                if (spec.ExpectedValue is not { } value)
                    continue;
                if (!TryParseFieldPosition(fieldKey, out var position))
                    continue;

                var index = position - 1;
                if (index >= 2 && index < fields.Length)
                    fields[index] = value;
            }

            lines[i] = string.Join('|', fields);
            break; // one MSH per message
        }
    }

    // Stable reorder to the profile's segment_order. Applied only when every non-empty segment line is
    // covered by the order, so a segment the order does not mention is never displaced. .NET OrderBy is
    // stable, so repeated segments (DG1 #1/#2, OBX …) keep their relative order within their rank.
    private static void ReorderSegments(List<string> lines, VendorInterfaceProfile profile)
    {
        if (profile.SegmentOrder.Count == 0)
            return;

        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < profile.SegmentOrder.Count; i++)
            rank[profile.SegmentOrder[i]] = i;

        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;
            if (!rank.ContainsKey(SegmentCode(line)))
                return; // an unlisted segment is present — leave ordering untouched.
        }

        var reordered = lines
            .Where(l => l.Length > 0)
            .OrderBy(l => rank[SegmentCode(l)])
            .ToList();

        lines.Clear();
        lines.AddRange(reordered);
    }

    private static string SegmentCode(string line)
    {
        var bar = line.IndexOf('|');
        return bar >= 0 ? line[..bar] : line;
    }

    // "MSH.3" / "PID.5" → the trailing field position. Returns false for a malformed key.
    private static bool TryParseFieldPosition(string fieldKey, out int position)
    {
        var dot = fieldKey.LastIndexOf('.');
        var token = dot >= 0 ? fieldKey[(dot + 1)..] : fieldKey;
        return int.TryParse(token, out position);
    }
}
