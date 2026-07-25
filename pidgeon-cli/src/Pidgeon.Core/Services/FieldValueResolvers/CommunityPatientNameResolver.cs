// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Renders the generated patient's own XPN fields in the community composition.
/// The full composition's identifier-coherence resolver owns this responsibility
/// there, but it depends on private demographic data sources. Keeping this small
/// public resolver separate prevents that private dependency from becoming an
/// accidental prerequisite for a required PID-5.
/// </summary>
public sealed class CommunityPatientNameResolver : ICompositeAwareResolver
{
    // The full composition's richer IdentifierCoherenceResolver (78) wins when present.
    // In the community subset this is the XPN floor, ahead of component-by-component fallback.
    public int Priority => 70;

    public bool CanHandleComposite(string dataTypeCode) => dataTypeCode == "XPN";

    public Task<Dictionary<int, string>?> ResolveCompositeAsync(
        SegmentField parentField,
        DataType dataType,
        FieldResolutionContext context)
    {
        var patient = context.GenerationContext?.Patient;
        if (patient is null
            || context.SegmentCode != "PID"
            || context.FieldPosition is not (5 or 9)
            || string.IsNullOrWhiteSpace(patient.Name?.Family)
            || string.IsNullOrWhiteSpace(patient.Name.Given))
        {
            return Task.FromResult<Dictionary<int, string>?>(null);
        }

        return Task.FromResult<Dictionary<int, string>?>(new Dictionary<int, string>
        {
            { 1, patient.Name.Family },
            { 2, patient.Name.Given }
        });
    }
}
