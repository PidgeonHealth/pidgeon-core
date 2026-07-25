// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Reference.HL7;

/// <summary>
/// Maps an HL7 reference standard id (<c>hl7v23</c> … <c>hl7v28</c>) to the
/// portable data-path prefix the <see cref="Pidgeon.Core.Application.Interfaces.Data.IDataResourceResolver"/>
/// serves (<c>standards/hl7/v23</c> …). Pure function (sanctioned static
/// carve-out) so the reference lane and the message-composition lane cannot
/// drift onto different path schemes.
/// </summary>
public static class Hl7ReferenceDataPath
{
    private const string StandardIdPrefix = "hl7v";

    public static string FromStandardId(string standardId)
    {
        if (string.IsNullOrWhiteSpace(standardId)
            || !standardId.StartsWith(StandardIdPrefix, StringComparison.Ordinal)
            || standardId.Length == StandardIdPrefix.Length)
        {
            throw new ArgumentException(
                $"Unknown HL7 reference standard id '{standardId}'. Expected the 'hl7v<version>' form (e.g. hl7v23).",
                nameof(standardId));
        }

        return $"standards/hl7/v{standardId[StandardIdPrefix.Length..]}";
    }
}
