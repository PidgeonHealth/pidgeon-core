// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Fills the RXA administration quantity pair with clinically plausible values: RXA-6
/// (Administered Amount) as a real injectable volume and RXA-7 (Administered Units) as
/// milliliters. No resolver claimed these positions, so the generic fallback chain rendered a
/// VXU^V04 vaccination as an amount no syringe holds with an internal id where the units belong.
/// Injectable administrations are overwhelmingly 0.5 mL or 1 mL, so the amount draws between
/// those two off the field coordinate (ADR-0005) and the units name mL.
///
/// Priority 94 spans both chains because RXA-7 changes shape across versions (CE through 2.5.1,
/// CWE at 2.6+) while RXA-6 stays a primitive NM: as an <see cref="IFieldValueResolver"/> it
/// claims RXA-6 ahead of the identifier/random fallbacks; as an
/// <see cref="ICompositeAwareResolver"/> it claims the RXA-7 composite ahead of the table and
/// coded-element resolvers. RXA-5 (Administered Code, the vaccine) is untouched — it keeps its
/// bound-table 0292 resolution through the coded-element chain.
/// </summary>
public class VaccineAdministrationResolver : IFieldValueResolver, ICompositeAwareResolver
{
    public int Priority => 94;

    // Simple chain: RXA-6 (Administered Amount, NM). RXA-7 is claimed here too in case a
    // schema variant carries it as a primitive; every shipped version routes it composite.
    public Task<string?> ResolveAsync(FieldResolutionContext context)
    {
        if (context.SegmentCode != "RXA")
            return Task.FromResult<string?>(null);

        return context.FieldPosition switch
        {
            6 => Task.FromResult<string?>(AdministeredAmount(context)),
            7 => Task.FromResult<string?>("mL"),
            _ => Task.FromResult<string?>(null)
        };
    }

    // Composite chain: RXA-7 (Administered Units, CE through 2.5.1 / CWE at 2.6+).
    // Everything else defers to its own chain via the null return below.
    public bool CanHandleComposite(string dataTypeCode) => dataTypeCode is "CE" or "CWE";

    public Task<Dictionary<int, string>?> ResolveCompositeAsync(
        SegmentField parentField, DataType dataType, FieldResolutionContext context)
    {
        if (context.SegmentCode != "RXA" || context.FieldPosition != 7)
            return Task.FromResult<Dictionary<int, string>?>(null);

        return Task.FromResult<Dictionary<int, string>?>(new Dictionary<int, string>
        {
            { 1, "mL" },
            { 2, "milliliters" },
            { 3, "UCUM" }
        });
    }

    // Standard injectable volumes: most vaccine formulations administer 0.5 mL; adult
    // formulations 1 mL. The draw is coordinate-addressed off the field key, so a seeded
    // corpus stays byte-reproducible.
    private static string AdministeredAmount(FieldResolutionContext context) =>
        context.FieldRng().NextDouble() < 0.7 ? "0.5" : "1";
}
