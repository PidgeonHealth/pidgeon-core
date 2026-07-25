// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// Matches FHIR extension elements against profile slice definitions by URL.
/// Extension slices in IGs like US Core (e.g., <c>Patient.extension:race</c>)
/// are identified on the wire by the extension's <c>url</c> property, which
/// points at the extension's own <c>StructureDefinition</c>. The canonical
/// URL for the slice lives on the slice element's <c>type[0].profile[0]</c>.
/// This class encapsulates the match check so the core <see cref="ProfileValidator"/>
/// stays readable.
/// </summary>
internal static class ExtensionValidator
{
    /// <summary>
    /// True when the element is a FHIR extension (object with a <c>url</c>
    /// property) whose <c>url</c> matches one of the profile URLs declared on
    /// the slice's <c>Extension</c>-typed slice definition. Returns false for
    /// any slice definition that isn't an extension slice — callers should
    /// fall through to other matching strategies in that case.
    /// </summary>
    public static bool MatchesExtensionSlice(JsonElement element, FHIRElementDefinition sliceDef)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (!element.TryGetProperty("url", out var urlProp) || urlProp.ValueKind != JsonValueKind.String)
            return false;

        var elementUrl = urlProp.GetString();
        if (string.IsNullOrEmpty(elementUrl))
            return false;

        foreach (var type in sliceDef.Types)
        {
            if (!string.Equals(type.Code, "Extension", StringComparison.Ordinal))
                continue;

            foreach (var profileUrl in type.Profile)
            {
                if (string.Equals(elementUrl, profileUrl, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the slice definition describes an extension slice — i.e., at
    /// least one declared type is <c>Extension</c> with a populated profile
    /// URL. Used by <see cref="ProfileValidator"/> to decide whether to try
    /// <see cref="MatchesExtensionSlice"/> before falling back to pattern /
    /// fixed value / discriminator matching.
    /// </summary>
    public static bool IsExtensionSlice(FHIRElementDefinition sliceDef)
    {
        foreach (var type in sliceDef.Types)
        {
            if (string.Equals(type.Code, "Extension", StringComparison.Ordinal) && type.Profile.Count > 0)
                return true;
        }
        return false;
    }
}
