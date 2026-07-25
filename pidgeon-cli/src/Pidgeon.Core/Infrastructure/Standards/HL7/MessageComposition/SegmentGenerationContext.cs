// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Domain.VendorIntelligence;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Context object for segment generation containing all available clinical data.
/// Includes temporal coherence tracking to ensure realistic timestamp relationships.
/// </summary>
public record SegmentGenerationContext(
    Patient Patient,
    Encounter? Encounter,
    Prescription? Prescription,
    ObservationResult? Observation,
    string MessageType)
{
    /// <summary>
    /// Gets the segment code being generated (set by generation process).
    /// </summary>
    public string SegmentCode { get; init; } = string.Empty;

    /// <summary>
    /// Gets the HL7 version for this generation context (e.g., "2.3", "2.5.1", "2.6").
    /// Used by resolvers and composers to emit the correct version ID.
    /// </summary>
    public string Hl7Version { get; init; } = "2.3";

    // Version-aware schema providers resolved once per message from Hl7Version and read by
    // every schema-path helper, so field data types are version-correct (PV1-2 as CWE at
    // v2.7, not the v2.3-pinned IS). Set by the composer; null for directly-built contexts.
    public IHL7SegmentProvider? SegmentProvider { get; init; }
    public IHL7DataTypeProvider? DataTypeProvider { get; init; }
    public IHL7TableProvider? TableProvider { get; init; }

    /// <summary>
    /// The active curated vendor interface profile for this message, or null for unshaped output.
    /// Set by the composer from <see cref="GenerationOptions.ActiveVendorProfile"/> and read by the
    /// post-assembly vendor-dialect pass to apply MSH conventions, segment ordering, and (S2) Z-segments.
    /// </summary>
    public VendorInterfaceProfile? VendorProfile { get; init; }

    /// <summary>
    /// When true, the caller-supplied <see cref="Prescription"/> is the clinical reality and the
    /// pharmacy order chain carries its medication verbatim (<see cref="OrderMedicationSource"/>
    /// honors it instead of redrawing). Set by the composer from
    /// <see cref="GenerationOptions.HonorSuppliedPrescription"/>.
    /// </summary>
    public bool HonorSuppliedPrescription { get; init; }

    /// <summary>
    /// Tracks generated timestamps by field path (e.g., "EVN.2", "PV1.44") for temporal coherence.
    /// Enables subsequent fields to generate timestamps relative to anchors.
    /// </summary>
    public Dictionary<string, DateTime> GeneratedTimestamps { get; init; } = new();

    /// <summary>
    /// Primary temporal anchor for the encounter (typically EVN.2 - Event Occurred).
    /// Other timestamps are generated relative to this anchor.
    /// </summary>
    public DateTime? EncounterStartTime { get; init; }

    /// <summary>
    /// The per-message random number generator: the FieldRng fallback for any resolver reached outside
    /// the coordinate-keyed field composer. Set once by the composer (derived from the message key, so it
    /// is itself coordinate-addressed). Defaults to a fresh coordinate-derived stream (mirroring
    /// <see cref="Key"/>) for callers that construct a context directly without the composer, so no bare
    /// <see cref="Random"/> enters the value path.
    /// </summary>
    public Random Rng { get; init; } = GenerationKey.Root(Random.Shared.Next()).AsRandom();

    /// <summary>
    /// The coordinate-addressed entropy key for this message. The composer derives it
    /// from <see cref="GenerationOptions.Seed"/> and narrows it per segment instance; the field
    /// composer narrows it again per field/component, so every value is a pure function of its
    /// (seed, segment, set-id, field) coordinate rather than RNG draw order. Defaults to a fresh
    /// random root for contexts built directly without the composer (mirroring <see cref="Rng"/>).
    /// </summary>
    public GenerationKey Key { get; init; } = GenerationKey.Root(Random.Shared.Next());

    /// <summary>
    /// The message-level entropy key — the root the composer narrows into <see cref="Key"/>
    /// per segment instance. Chain-level draws that must agree ACROSS segments (the pharmacy
    /// order medication shared by RXO-1/RXE-2/RXG-4) derive from this key so every segment
    /// resolves the identical value. Defaults to a fresh random root for directly-built
    /// contexts (mirroring <see cref="Key"/>); the composer sets both from the same root.
    /// </summary>
    public GenerationKey MessageKey { get; init; } = GenerationKey.Root(Random.Shared.Next());

    /// <summary>
    /// The 1-based occurrence index of the segment currently being composed (set by the segment
    /// composer). Repeated segments increment it (DG1 #1, #2, …), so resolvers can select clinical
    /// content by segment instance — a pure function of the coordinate, not a process-global counter.
    /// </summary>
    public int SetId { get; init; } = 1;

    /// <summary>
    /// The enclosing group hierarchy of the segment currently being composed (its
    /// <see cref="TriggerEventSegment.GroupPath"/>), set by the segment composer. A content
    /// contributor reads it to know WHERE in the message structure it sits — the narrative-note
    /// contributor keys on it to pick the coherent axis (an NTE in the OBSERVATION group restates a
    /// lab result; one in the ORDER OBSERVATION group restates the ordered test; one in a pharmacy
    /// ORDER group restates the ordered medication). Empty for directly-built contexts and for
    /// top-level segments.
    /// </summary>
    public IReadOnlyList<string> GroupPath { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The per-message wall-clock. Fixed when generation is seeded so timestamps are
    /// reproducible; the real current time otherwise. Replaces scattered
    /// <see cref="DateTime.Now"/> / <see cref="DateTime.Today"/> reads across the
    /// composition path.
    /// </summary>
    public DateTime Clock { get; init; } = DateTime.Now;

    /// <summary>
    /// Per-message OBX lab coherence state shared by the lab resolvers. Carried here so it
    /// survives the async thread hops in the resolver chain, keeping seeded output reproducible.
    /// </summary>
    public ObxLabState ObxLabs { get; init; } = new();

    /// <summary>
    /// Per-message resolve-once state for the pharmacy order-chain medication, shared by the
    /// RXO/RXE/RXG give-code resolvers so the whole chain names ONE drug (audit D-05).
    /// Reference-typed like <see cref="ObxLabs"/>, so per-segment `with` copies share it.
    /// </summary>
    public OrderMedicationState OrderMedication { get; init; } = new();

    /// <summary>
    /// Per-message record of the observation facts the composer emitted (OBX identity+value core),
    /// populated by <c>ObxValueContributor</c> as each OBX is composed. A narrative-note contributor
    /// reads the most recent entry to compose a note that restates a real coded fact — coherent by
    /// construction. Reference-typed like <see cref="ObxLabs"/>, so per-segment `with` copies share it.
    /// </summary>
    public Contribution.EmittedObservationState EmittedObservations { get; init; } = new();
};
