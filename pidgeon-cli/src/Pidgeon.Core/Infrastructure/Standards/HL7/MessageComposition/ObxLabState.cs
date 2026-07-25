// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Services.Clinical;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Per-message lab cache shared by the OBR lab resolvers (ClinicalLabResultResolver populates it;
/// ClinicalCodedElementResolver reads it for the OBR-4 LOINC), so the order's labs are selected from
/// one list and the coordinator's lab RNG is drawn once. OBX content comes from
/// <c>ObxValueContributor</c>; the order's universal service identifier (OBR-4) is the first lab.
///
/// Lives on <see cref="SegmentGenerationContext"/>, so it flows through <c>await</c> with the
/// generation regardless of which thread-pool thread a continuation lands on. This keeps labs
/// stable across the <c>Task.Yield()</c> hops in the resolver chain, so seeded output stays
/// reproducible.
/// </summary>
public sealed class ObxLabState
{
    /// <summary>The labs selected once for this message (null until first resolved).</summary>
    public List<LabTestResult>? Results { get; set; }

    /// <summary>
    /// Cross-cell observation cursor: the 0-based ordinal of the NEXT OBX to emit, counted across
    /// every OBX cell in the message (the ORDER OBSERVATION result group AND the SPECIMEN group at
    /// v2.5.1+, three groups at v2.7), not per cell. <c>ObxValueContributor</c> reads it to pick the
    /// observation and advances it, so a second OBX cell continues the lab cycle instead of
    /// restarting at the first lab and emitting a byte-identical duplicate. Message-scoped like
    /// <see cref="Results"/> (a fresh state per message), so seeded output stays reproducible.
    /// </summary>
    public int NextObservationOrdinal { get; set; }
}
