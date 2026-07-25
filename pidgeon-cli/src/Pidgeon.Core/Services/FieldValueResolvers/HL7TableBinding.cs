// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Single source of truth for "is this coded field a constrained HL7 enumeration?" — a
/// field bound to a populated HL7 table. <see cref="CodedElementResolver"/> resolves the
/// code from the returned table; <see cref="ClinicalCodedElementResolver"/> defers to it
/// (skips its greedy keyword matches) when this returns non-null. Pass the version-aware
/// provider carried on the generation context so a v2.7 message draws from v2.7's table.
/// </summary>
internal static class HL7TableBinding
{
    public static async Task<TableDefinition?> GetPopulatedTableAsync(SegmentField field, IHL7TableProvider? provider)
    {
        if (provider is null || !field.TableId.HasValue || field.TableId.Value <= 0)
        {
            return null;
        }

        var result = await provider.GetTableAsync(field.TableId.Value).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return null;
        }

        // Template-notation codes (Q<integer>J<day#>, TH<integer>, <null>) and ellipsis range
        // markers ("O1 ... O9") are spec notation, not sendable values. Selection sees only the
        // literal codes.
        var selectable = HL7TableCodeTemplates.Selectable(result.Value.Values);
        if (selectable.Count > 0)
        {
            return result.Value with { Values = selectable };
        }

        // A table whose rows are ALL range/notation (e.g. 0141 military rank) is still a populated,
        // constrained enumeration — return it with its original rows so the coded-element resolver
        // draws a real (if over-length) table member. Returning null here would route the coded
        // field to an ID/PRV identifier placeholder that fails the table (HL7-TABLE-001); a genuine
        // member is table-valid. A genuinely empty table stays null so callers fall through.
        return result.Value.Values.Count > 0 ? result.Value : null;
    }
}
