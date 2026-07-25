// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Services.FieldValueResolvers;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.Resolvers;

/// <summary>
/// Populates the HL7 SFT (Software Segment) fields that identify the sending
/// software. The SFT segment is emitted on v2.5+ messages; its required SFT.3
/// (Software Product Name) carries no field name the generic demographic or
/// identifier resolvers recognize. This resolver owns the software-product identity
/// so the required SFT.3 field is satisfied with the engine's own name, rather than
/// left empty to raise an HL7-REQ-001 strict error on every SFT-bearing message.
///
/// Scope is deliberately limited to SFT.3 (the required field);
/// the other SFT fields are left to the existing resolvers.
/// </summary>
public class SoftwareSegmentResolver : IFieldValueResolver
{
    // The product name emitted as SFT.3. The sending software is Pidgeon.
    private const string SoftwareProductName = "Pidgeon";

    // Priority 89: above the generic identifier (75) / demographic (80) resolvers
    // so SFT.3 is claimed here, below the MSH structural resolver (90).
    public int Priority => 89;

    public Task<string?> ResolveAsync(FieldResolutionContext context)
    {
        // HL7 segment codes are always uppercase; match exactly, as the sibling resolvers do.
        if (context.SegmentCode != "SFT")
        {
            return Task.FromResult<string?>(null);
        }

        string? value = context.FieldPosition == 3 ? SoftwareProductName : null;
        return Task.FromResult(value);
    }
}
