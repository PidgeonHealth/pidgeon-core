// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Pidgeon.Core.Domain.VendorIntelligence;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default <see cref="IZSegmentComposer"/>. Builds each vendor Z-segment line from the profile's field
/// specs, positioning fields by their 1-based index (ZTP.1 → first field). Field values resolve in
/// priority order: a constant <see cref="VendorFieldSpec.ExpectedValue"/>; else a clinical-context
/// binding (<see cref="VendorFieldSpec.Source"/>); else a deterministic faker for a numeric field;
/// else empty. Deterministic — faker draws are coordinate-addressed off the message key (ADR-0005), and
/// context bindings are pure functions of the seeded patient/encounter — so the same seed reproduces
/// the same Z-segments byte-for-byte.
/// </summary>
public class ZSegmentComposer : IZSegmentComposer
{
    public IReadOnlyList<string> Compose(VendorInterfaceProfile profile, SegmentGenerationContext context)
    {
        var lines = new List<string>();

        // Stable order: Z-segment codes sorted so multiple Z-segments emit deterministically.
        var zSegments = profile.Segments
            .Where(kvp => kvp.Key.StartsWith("Z", StringComparison.OrdinalIgnoreCase))
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal);

        foreach (var (segmentCode, spec) in zSegments)
            lines.Add(ComposeSegment(segmentCode, spec, context));

        return lines;
    }

    private static string ComposeSegment(string segmentCode, VendorSegmentSpec spec, SegmentGenerationContext context)
    {
        var maxPosition = spec.Fields.Keys
            .Select(key => TryParseFieldPosition(key, out var p) ? p : 0)
            .DefaultIfEmpty(0)
            .Max();

        if (maxPosition == 0)
            return segmentCode;

        var fields = new string[maxPosition];
        for (var i = 0; i < fields.Length; i++)
            fields[i] = string.Empty;

        foreach (var (fieldKey, fieldSpec) in spec.Fields)
        {
            if (!TryParseFieldPosition(fieldKey, out var position) || position < 1 || position > maxPosition)
                continue;

            fields[position - 1] = ResolveFieldValue(segmentCode, position, fieldSpec, context);
        }

        return segmentCode + "|" + string.Join("|", fields);
    }

    // Constant > clinical-context binding > deterministic numeric faker > empty.
    private static string ResolveFieldValue(
        string segmentCode, int position, VendorFieldSpec fieldSpec, SegmentGenerationContext context)
    {
        if (fieldSpec.ExpectedValue is { } constant)
            return constant;

        if (!string.IsNullOrWhiteSpace(fieldSpec.Source))
        {
            var bound = ResolveSource(fieldSpec.Source!, context);
            if (bound is not null)
                return bound;
        }

        if (string.Equals(fieldSpec.Format, "numeric", StringComparison.OrdinalIgnoreCase))
        {
            var rng = context.Key.Derive("vendor-z").Derive(segmentCode).Derive(position).AsRandom();
            return rng.Next(10_000_000, 100_000_000).ToString(CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    // Closed value-source vocabulary, resolved from the message's clinical context. Returns null for an
    // unknown token (the caller falls through), or an empty string when the bound value is absent.
    private static string? ResolveSource(string source, SegmentGenerationContext context)
    {
        var patient = context.Patient;
        return source.Trim().ToLowerInvariant() switch
        {
            "patient.id" => patient.Id,
            "patient.mrn" => patient.MedicalRecordNumber ?? patient.Id,
            "patient.phone" => patient.PhoneNumber ?? string.Empty,
            "patient.dob" => patient.BirthDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty,
            "encounter.id" => context.Encounter?.Id ?? string.Empty,
            "now" => context.Clock.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            _ => null
        };
    }

    // "ZTP.1" / "ZPM.3" → the trailing field position.
    private static bool TryParseFieldPosition(string fieldKey, out int position)
    {
        var dot = fieldKey.LastIndexOf('.');
        var token = dot >= 0 ? fieldKey[(dot + 1)..] : fieldKey;
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out position);
    }
}
