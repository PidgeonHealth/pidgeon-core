// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Common;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default <see cref="IHL7FieldPinner"/>: the HL7-positional pin workbench. Separates the
/// pin overlay from message orchestration. Every method is a pure function of its inputs;
/// the type carries no generation state.
/// </summary>
public class HL7FieldPinner : IHL7FieldPinner
{
    public bool HasAnyPinForSegment(string segmentCode, GenerationOptions options)
    {
        if (options.AdhocLockedValues is null || options.AdhocLockedValues.Count == 0) return false;
        foreach (var pin in options.AdhocLockedValues)
        {
            if (Hl7Path.TryParse(pin.FieldPath, out var path) &&
                string.Equals(path.Segment, segmentCode, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public Dictionary<int, string> GetPinsForField(string segmentCode, int fieldPosition, GenerationOptions options)
    {
        var result = new Dictionary<int, string>();
        if (options.AdhocLockedValues is null || options.AdhocLockedValues.Count == 0) return result;
        foreach (var pin in options.AdhocLockedValues)
        {
            if (!Hl7Path.TryParse(pin.FieldPath, out var path)) continue;
            if (!string.Equals(path.Segment, segmentCode, StringComparison.Ordinal)) continue;
            if (path.Field != fieldPosition) continue;
            // Subcomponent-level pins (PID.5.1.2) collapse to the component key for v1.
            var key = path.Component ?? 0;
            result[key] = pin.Value;
        }
        return result;
    }

    public string ApplySegmentLevelPins(string segmentLine, string segmentCode, GenerationOptions options)
    {
        if (string.IsNullOrEmpty(segmentLine)) return segmentLine;
        if (!HasAnyPinForSegment(segmentCode, options)) return segmentLine;

        // Group pins by field position. Within each field, key 0 = whole-field pin,
        // keys 1+ = component pins.
        var pinsByField = new Dictionary<int, Dictionary<int, string>>();
        foreach (var pin in options.AdhocLockedValues!)
        {
            if (!Hl7Path.TryParse(pin.FieldPath, out var path)) continue;
            if (!string.Equals(path.Segment, segmentCode, StringComparison.Ordinal)) continue;
            if (!pinsByField.TryGetValue(path.Field, out var componentMap))
            {
                componentMap = new Dictionary<int, string>();
                pinsByField[path.Field] = componentMap;
            }
            componentMap[path.Component ?? 0] = pin.Value;
        }

        if (pinsByField.Count == 0) return segmentLine;

        // fields[0] is the segment code; fields[1] = field 1, fields[2] = field 2, etc.
        var fields = segmentLine.Split('|');

        // Extend if a pin reaches beyond the generated field count.
        var maxField = 0;
        foreach (var p in pinsByField.Keys) if (p > maxField) maxField = p;
        if (maxField + 1 > fields.Length)
        {
            var extended = new string[maxField + 1];
            Array.Copy(fields, extended, fields.Length);
            for (var i = fields.Length; i < extended.Length; i++) extended[i] = string.Empty;
            fields = extended;
        }

        foreach (var (fieldPos, componentMap) in pinsByField)
        {
            if (componentMap.TryGetValue(0, out var wholeFieldPin))
            {
                fields[fieldPos] = wholeFieldPin;
            }
            else
            {
                fields[fieldPos] = ApplyComponentPins(fields[fieldPos], componentMap);
            }
        }

        return string.Join("|", fields);
    }

    public string ApplyComponentPins(string fieldValue, Dictionary<int, string> fieldPins)
    {
        if (fieldPins.Count == 0) return fieldValue;
        // Whole-field pins were handled earlier; component pins are keys >= 1.
        var componentPins = new List<KeyValuePair<int, string>>();
        foreach (var kv in fieldPins)
        {
            if (kv.Key > 0) componentPins.Add(kv);
        }
        if (componentPins.Count == 0) return fieldValue;

        var parts = fieldValue.Split('^');
        var maxComponent = 0;
        foreach (var kv in componentPins)
        {
            if (kv.Key > maxComponent) maxComponent = kv.Key;
        }
        if (maxComponent > parts.Length)
        {
            var extended = new string[maxComponent];
            Array.Copy(parts, extended, parts.Length);
            for (var i = parts.Length; i < maxComponent; i++) extended[i] = string.Empty;
            parts = extended;
        }
        foreach (var (componentPos, value) in componentPins)
        {
            parts[componentPos - 1] = value;
        }
        return string.Join("^", parts);
    }
}
