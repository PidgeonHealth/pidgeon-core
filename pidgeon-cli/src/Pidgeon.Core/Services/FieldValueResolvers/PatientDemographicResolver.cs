// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Fills the patient-identifying demographic fields that must reflect the generated
/// <see cref="Patient"/> entity rather than an independent draw: PID-8 (Administrative Sex) and
/// PID-7 (Date/Time of Birth). Keeping these coherent with the entity is what lets the semantic
/// layer reason about a message's sex and age (sem-bench F06/F07); historically PID-8 was a random
/// table-1 draw and PID-7 was the generation clock (TemporalCoherenceResolver stamps every TS field
/// with message time), both decoupled from the entity.
///
/// Priority 95 spans two chains, and BOTH claim BOTH fields because the fields change shape across
/// versions (the D-21/D-22 regression: the fix held at 2.3–2.5.1 and silently lapsed at 2.6+):
/// PID-7 is a TS composite through 2.5.1 but a primitive DTM at 2.6+; PID-8 is a simple IS through
/// 2.6 but a CWE composite at 2.7+. As an <see cref="IFieldValueResolver"/> it beats the table
/// resolver (85) and TemporalCoherenceResolver (92); as an <see cref="ICompositeAwareResolver"/> it
/// beats TemporalCoherenceResolver for the PID-7 composite and the coded-element resolvers for the
/// PID-8 CWE, claiming <em>only</em> PID-7/PID-8 so every other timestamp and coded field still
/// flows through its own chain. It stays below session/pin overrides (100), and other table-1 sex
/// fields (NK1-15, GT1-5, IN1-19 — different people) are left to the table resolver.
/// </summary>
public class PatientDemographicResolver : IFieldValueResolver, ICompositeAwareResolver
{
    public int Priority => 95;

    // Simple chain: PID-8 (Administrative Sex, IS through 2.6) and PID-7 (DTM primitive at 2.6+).
    public Task<string?> ResolveAsync(FieldResolutionContext context)
    {
        var patient = context.GenerationContext?.Patient;
        if (patient is null || context.SegmentCode != "PID")
            return Task.FromResult<string?>(null);

        return context.FieldPosition switch
        {
            8 => Task.FromResult(GenderToHl7Code(patient.Gender)),
            7 => Task.FromResult(patient.BirthDate?.ToString("yyyyMMdd")),
            _ => Task.FromResult<string?>(null)
        };
    }

    // Composite chain: PID-7 (TS composite through 2.5.1) and PID-8 (CWE composite at 2.7+).
    // Everything else defers to its own chain via the null return below.
    public bool CanHandleComposite(string dataTypeCode) => dataTypeCode is "TS" or "DTM" or "CWE" or "CE";

    public Task<Dictionary<int, string>?> ResolveCompositeAsync(
        SegmentField parentField, DataType dataType, FieldResolutionContext context)
    {
        var patient = context.GenerationContext?.Patient;
        if (patient is null || context.SegmentCode != "PID")
            return Task.FromResult<Dictionary<int, string>?>(null);

        if (context.FieldPosition == 7)
        {
            var birthDate = patient.BirthDate;
            return Task.FromResult<Dictionary<int, string>?>(
                birthDate is null
                    ? null
                    : new Dictionary<int, string> { { 1, birthDate.Value.ToString("yyyyMMdd") } });
        }

        if (context.FieldPosition == 8)
        {
            var code = GenderToHl7Code(patient.Gender);
            return Task.FromResult<Dictionary<int, string>?>(
                code is null
                    ? null
                    : new Dictionary<int, string> { { 1, code }, { 2, GenderDisplay(code) }, { 3, "HL70001" } });
        }

        return Task.FromResult<Dictionary<int, string>?>(null);
    }

    // HL7 table 0001 display names for the CWE text component.
    private static string GenderDisplay(string code) => code switch
    {
        "M" => "Male",
        "F" => "Female",
        "O" => "Other",
        _ => "Unknown"
    };

    // HL7 table 0001 (Administrative Sex) codes for the four values the Gender enum models.
    private static string? GenderToHl7Code(Gender? gender) => gender switch
    {
        Gender.Male => "M",
        Gender.Female => "F",
        Gender.Other => "O",
        Gender.Unknown => "U",
        _ => null
    };
}
