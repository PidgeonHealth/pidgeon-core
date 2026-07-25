// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Semantic;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Makes a generated order's specimen cohere with the analyte it reports. Generation otherwise
/// draws OBR-15 (Specimen Source) and SPM-4 (Specimen Type) at random from their coded tables with
/// no analyte awareness, so a serum chemistry lands on a Vitreous Fluid / Urine / Stool / Keloid
/// specimen a clinician reads as impossible — the exact fault the SEM-F08 check flags. This resolver
/// closes that gap on the generation side: the order's primary analyte (the first resolved lab,
/// which backs OBR-4) has an acceptable specimen-system set per its LOINC System axis
/// (<see cref="ILoincSystemLoader"/>), and the specimen crosswalk
/// (<see cref="ISpecimenLoincLoader"/>) names the HL7 specimen codes coherent WITH those systems. The
/// specimen is chosen from that coherent set, so a serum analyte carries a serum/plasma specimen.
///
/// It intercepts two shapes:
///   • OBR-15 SPS.1 (Specimen Source Name Or Code) via the plain resolver chain — SPS is expanded
///     component-by-component, and SPS.1 carries no bound table so any 0070-domain code stands.
///   • SPM-4 (Specimen Type, a CWE bound to table 0487) via the composite-aware path — the picked
///     code is kept a member of the field's bound table so the emission stays table-valid.
///
/// The pick is coordinate-addressed (<c>context.FieldRng()</c>), so a seeded run stays byte-identical.
/// When the analyte's system is unresolvable (its LOINC is not in the crosswalk) or no coherent code
/// is a member of the field's table, the resolver defers — the generic table pick decides the
/// specimen exactly as before. Priority 88 sits above the generic HL7 table resolver (85) and the
/// coded-element resolvers (82–84), below the clinical lab/coded resolvers that populate the shared
/// per-message lab state this reads.
/// </summary>
public class SpecimenCoherenceResolver : IFieldValueResolver, ICompositeAwareResolver
{
    private readonly ILogger<SpecimenCoherenceResolver> _logger;
    private readonly IHL7TableProvider _tableProvider;
    private readonly ISpecimenLoincLoader? _specimens;
    private readonly ILoincSystemLoader? _analyteSystems;

    public int Priority => 88;

    public SpecimenCoherenceResolver(
        ILogger<SpecimenCoherenceResolver> logger,
        IHL7TableProvider tableProvider,
        ISpecimenLoincLoader? specimens = null,
        ILoincSystemLoader? analyteSystems = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tableProvider = tableProvider ?? throw new ArgumentNullException(nameof(tableProvider));
        _specimens = specimens;
        _analyteSystems = analyteSystems;
    }

    public async Task<string?> ResolveAsync(FieldResolutionContext context)
    {
        if (!IsObrSpecimenSourceCode(context))
            return null;

        var picked = await PickCoherentSpecimenAsync(context).ConfigureAwait(false);
        return picked?.Code; // null → fall through to the generic table pick
    }

    // SPS is CWE/CE spine; claim the coded-element family, then narrow to SPM-4 in the composite
    // resolve so no other coded field is affected.
    public bool CanHandleComposite(string dataTypeCode)
        => dataTypeCode is "CE" or "CF" or "CWE" or "CNE";

    public async Task<Dictionary<int, string>?> ResolveCompositeAsync(
        SegmentField parentField,
        DataType dataType,
        FieldResolutionContext context)
    {
        if (!string.Equals(context.SegmentCode, "SPM", StringComparison.OrdinalIgnoreCase) ||
            context.FieldPosition != 4)
            return null;

        var picked = await PickCoherentSpecimenAsync(context).ConfigureAwait(false);
        if (picked is null)
            return null;

        var codingSystem = parentField.TableId is > 0 ? $"HL7{parentField.TableId:D4}" : string.Empty;
        _logger.LogDebug("SPM-4 specimen {Code} chosen coherent with the order's analyte", picked.Value.Code);
        return new Dictionary<int, string>
        {
            { 1, picked.Value.Code },        // CWE.1 Identifier (specimen code)
            { 2, picked.Value.Description },  // CWE.2 Text
            { 3, codingSystem }               // CWE.3 Name Of Coding System
        };
    }

    // The specimen-code component of OBR-15 (Specimen Source): SPS.1 at v2.5.1+ (parent type SPS) and
    // CM_SPS.1 at v2.3/2.4 (parent type CM_SPS). Keyed on the parent specimen-source type and the
    // component name so Additives / Collection Method / Body Site are never claimed.
    private static bool IsObrSpecimenSourceCode(FieldResolutionContext ctx)
    {
        if (!string.Equals(ctx.SegmentCode, "OBR", StringComparison.OrdinalIgnoreCase) || ctx.FieldPosition != 15)
            return false;
        var parent = ctx.ParentFieldDataType ?? string.Empty;
        if (!parent.Equals("SPS", StringComparison.OrdinalIgnoreCase) &&
            !parent.Equals("CM_SPS", StringComparison.OrdinalIgnoreCase))
            return false;
        return (ctx.Field.Name ?? string.Empty)
            .Contains("specimen source name", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(string Code, string Description)?> PickCoherentSpecimenAsync(FieldResolutionContext context)
    {
        // Graceful degradation: without the crosswalks (a DI graph that omits the semantic layer) or
        // an active generation context, there is no coherent pick — the generic path stands.
        if (_specimens is null || _analyteSystems is null || context.GenerationContext is null)
            return null;

        // The order's primary analyte — the same first lab the composer renders in OBR-4, drawn once
        // per message onto the shared lab state by the clinical lab resolvers that precede this one.
        var labs = context.GenerationContext.ObxLabs.Results;
        if (labs is null || labs.Count == 0)
            return null;

        var loinc = labs[0].LoincCode;
        if (string.IsNullOrWhiteSpace(loinc))
            return null;

        var systemTokens = _analyteSystems.GetSystemTokens(loinc);
        if (systemTokens.Count == 0)
            return null; // analyte system unresolvable → the generic table pick decides the specimen

        var candidates = _specimens.SpecimenCodesForSystems(systemTokens);
        if (candidates.Count == 0)
            return null;

        // A bound specimen table (SPM-4 → 0487) constrains the emission: keep only coherent codes that
        // are members of it, and carry the table's own description. SPS.1 (OBR-15) has no bound table,
        // so its 0070-domain code stands with no description.
        var tableId = context.Field.TableId is > 0 ? context.Field.TableId.Value : 0;
        IReadOnlyDictionary<string, string>? descriptions = null;
        if (tableId > 0)
        {
            var provider = context.GenerationContext.TableProvider ?? _tableProvider;
            var tableResult = await provider.GetTableAsync(tableId).ConfigureAwait(false);
            if (tableResult.IsSuccess)
            {
                var byCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var value in tableResult.Value.Values)
                    byCode[value.Code] = value.Description;

                var filtered = candidates.Where(byCode.ContainsKey).ToList();
                if (filtered.Count == 0)
                    return null; // no coherent code is a member of the field's table → generic pick

                candidates = filtered;
                descriptions = byCode;
            }
        }

        var code = candidates[context.FieldRng().Next(candidates.Count)];
        var description = descriptions != null && descriptions.TryGetValue(code, out var d) ? d : string.Empty;
        return (code, description);
    }
}
