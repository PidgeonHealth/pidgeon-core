// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pidgeon.Core.Generation;

/// <summary>
/// Produces a canonical, minified JSON string for content-addressed identity: keys sorted ordinally at
/// every nesting level, no whitespace, array order preserved. It reuses the caller's serializer options
/// (naming policy, enum converters, null-omission) so the canonical form carries the same field set and
/// value encoding as the display serializer — only the key order and whitespace are normalized.
///
/// The reason this exists: the display serializer preserves dictionary insertion order (e.g. for the
/// manifest's segment-probability / segment-repeat maps), which is not stable across producers. Deriving
/// a run's identity from the display JSON would let two equal-by-value runs mint different ids. Sorting
/// keys removes that ambiguity.
/// </summary>
public static class CanonicalJson
{
    /// <summary>
    /// Serializes <paramref name="value"/> to canonical minified JSON using the supplied options for the
    /// field set and value encoding, then re-emits it with every object's keys sorted ordinally.
    /// </summary>
    public static string Serialize(object value, JsonSerializerOptions options)
    {
        var node = JsonSerializer.SerializeToNode(value, value.GetType(), options);
        var builder = new StringBuilder();
        WriteCanonical(node, builder);
        return builder.ToString();
    }

    private static void WriteCanonical(JsonNode? node, StringBuilder builder)
    {
        switch (node)
        {
            case null:
                builder.Append("null");
                break;

            case JsonObject obj:
                builder.Append('{');
                var firstMember = true;
                foreach (var member in obj.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (!firstMember)
                        builder.Append(',');
                    firstMember = false;
                    builder.Append(JsonSerializer.Serialize(member.Key));
                    builder.Append(':');
                    WriteCanonical(member.Value, builder);
                }
                builder.Append('}');
                break;

            case JsonArray array:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in array)
                {
                    if (!firstItem)
                        builder.Append(',');
                    firstItem = false;
                    WriteCanonical(item, builder);
                }
                builder.Append(']');
                break;

            default:
                // A scalar (string, number, bool). ToJsonString emits the minified, escaped form.
                builder.Append(node.ToJsonString());
                break;
        }
    }
}
