// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Per-message resolve-once holder for the chain medication, carried on
/// <see cref="SegmentGenerationContext"/> (reference-typed, so every per-segment `with` copy
/// shares it — the ObxLabState pattern). Needed because the scenario coordinator's
/// GetMedications draws a fresh probability filter per call: without resolve-once, the RXO
/// call and the RXE call could see different scenario med lists and split the chain identity.
/// </summary>
public sealed class OrderMedicationState
{
    public bool Resolved { get; set; }
    public OrderMedication? Value { get; set; }
}
