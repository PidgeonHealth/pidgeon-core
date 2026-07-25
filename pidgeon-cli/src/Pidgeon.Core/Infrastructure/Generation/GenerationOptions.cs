// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.VendorIntelligence;
using Pidgeon.Core.Generation.Types;
using Pidgeon.Core.Infrastructure.Data;

namespace Pidgeon.Core.Generation;

/// <summary>
/// Configuration options for healthcare data generation.
/// Controls both algorithmic generation (free tier) and AI enhancement (subscription tiers).
/// </summary>
public record GenerationOptions
{
    /// <summary>
    /// Gets the type of patient to generate, affecting age ranges and clinical patterns.
    /// </summary>
    public PatientType Type { get; init; } = PatientType.General;

    /// <summary>
    /// Gets the seed for deterministic generation. When specified, identical options
    /// will produce identical results for reproducible testing.
    /// </summary>
    public int? Seed { get; init; }

    /// <summary>
    /// Gets the wall-clock instant the run is stamped with (MSH-7 and other dated fields). When null,
    /// the run captures the real <see cref="DateTime.Now"/> at resolve time; set it explicitly (the CLI
    /// <c>--as-of</c> flag) to pin a specific instant. Recorded in the run-manifest so a run replays
    /// byte-for-byte. Replaces the prior hidden coupling where supplying a seed froze the clock at a
    /// fixed 2024 instant.
    /// </summary>
    public DateTime? AsOf { get; init; }

    /// <summary>
    /// Gets the vendor profile for EHR-specific formatting patterns.
    /// Basic patterns available in free tier, specific templates in subscription tiers.
    /// </summary>
    public VendorProfile? VendorProfile { get; init; }

    /// <summary>
    /// Gets the resolved curated vendor interface profile applied to this run, if any. Set by the
    /// vendor-profile resolver from <see cref="VendorProfile"/> (the friendly selector); the HL7
    /// composer's vendor-dialect pass reads it to shape MSH conventions, segment ordering, and (S2)
    /// vendor Z-segments. Null leaves generation standard/unshaped. Kept separate from the
    /// <see cref="VendorProfile"/> enum (a mapping, not a merge) so friendly-name surfaces stay intact.
    /// </summary>
    public VendorInterfaceProfile? ActiveVendorProfile { get; init; }

    /// <summary>
    /// Gets whether to use AI enhancement for generation.
    /// Available in Professional+ tiers with BYOK or Enterprise with unlimited usage.
    /// </summary>
    public bool UseAI { get; init; } = false;

    /// <summary>
    /// Gets the API key for AI providers (BYOK model for Professional tier).
    /// Not required for Enterprise tier with unlimited AI.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Gets the AI provider to use for enhanced generation.
    /// </summary>
    public AIProvider Provider { get; init; } = AIProvider.None;

    /// <summary>
    /// Gets the AI generation mode for contextual enhancement.
    /// </summary>
    public AIGenerationMode Mode { get; init; } = AIGenerationMode.Enhanced;

    /// <summary>
    /// Gets additional context for generation (diagnosis, specialty, facility type).
    /// Used by both algorithmic and AI generation for appropriate correlations.
    /// </summary>
    public Dictionary<string, object> Context { get; init; } = new();

    /// <summary>
    /// Gets the lock session name to use for field value constraints.
    /// When specified, generation will apply locked semantic path values from the session.
    /// </summary>
    public string? LockSessionName { get; init; }

    /// <summary>
    /// Gets field values pinned inline for this single generation request, bypassing any
    /// named lock session. When non-empty these take precedence over <see cref="LockSessionName"/>
    /// — the values the caller just supplied win over a saved session.
    /// </summary>
    public IReadOnlyList<AdhocLockedValue>? AdhocLockedValues { get; init; }

    /// <summary>
    /// Gets custom probabilities for optional segment inclusion (0.0 to 1.0).
    /// Allows fine-grained control over which optional segments appear in messages.
    /// </summary>
    public Dictionary<string, double>? SegmentProbabilities { get; init; }

    /// <summary>
    /// Gets custom repeat counts for repeatable segments.
    /// Overrides default repeat count logic for specific segments.
    /// </summary>
    public Dictionary<string, int>? SegmentRepeatCounts { get; init; }

    /// <summary>
    /// Gets the HL7 version to use for message generation (e.g., "2.3", "2.5.1", "2.6").
    /// Determines which version-specific data providers are used for trigger events,
    /// segments, data types, and tables.
    /// </summary>
    public string Hl7Version { get; init; } = "2.3";

    /// <summary>
    /// Indicates whether the HL7 version was explicitly specified by the caller.
    /// When true, a missing trigger event in the requested version produces a hard error.
    /// When false (default), the composer will automatically try newer versions to find
    /// the trigger event (e.g., RDE^O11 not in v2.3 → found in v2.4).
    /// </summary>
    public bool Hl7VersionExplicit { get; init; } = false;

    /// <summary>
    /// Gets the NCPDP SCRIPT version the NCPDP generation path targets (folder-style, e.g.
    /// "2017071", "2023011", "2023071"). Consulted only by the NCPDP path — it sets the generated
    /// message's release stamp so the document validates against that version's XSD oracle. Null
    /// leaves the domain default (2017071), so existing callers are unchanged.
    /// </summary>
    public string? NcpdpVersion { get; init; }

    /// <summary>
    /// Declares the caller-supplied clinical entities (the Prescription passed to a message factory)
    /// as the clinical reality: the pharmacy order chain (RXO-1/RXE-2/RXG-4) then carries THAT
    /// medication instead of redrawing from the breadth pool / clinical scenario. Default false —
    /// the generation plugin's self-generated prescription draws from a small static pool, and
    /// honoring it unconditionally would collapse corpus drug breadth and scenario coherence
    /// (see OrderMedicationSource). Set by callers that mean a specific prescription: the
    /// cross-standard equivalence oracle, Migrate conversion.
    /// </summary>
    public bool HonorSuppliedPrescription { get; init; } = false;

    /// <summary>
    /// Gets optional AMA CPT API configuration for live CPT code lookup.
    /// When set, enables downloading the latest CPT data directly from the AMA API.
    /// </summary>
    public AmaApiOptions? AmaApi { get; set; }

    /// <summary>
    /// Gets whether this generation is part of a patient-coherent sequence.
    /// When true, optional fields are always populated (instead of randomly
    /// suppressed) so demographics stay consistent across the sequential
    /// ADT → ORM → ORU messages that share one Patient.
    /// </summary>
    public bool IsCohortSequence { get; init; } = false;

    /// <summary>
    /// Force-includes the PATIENT group (PID plus the optional groups that enclose it in the trigger
    /// event) even when the seed-driven inclusion draw would roll it off, WITHOUT the cross-message
    /// patient-sharing of <see cref="IsCohortSequence"/>. Each message still draws its own patient and
    /// stays a pure function of its own seed; only the "spec-legal but patient-less" shape is removed.
    /// Default false — the normal seed-driven variety (a fraction of messages legitimately omit an
    /// optional PATIENT group) is preserved. Set by the Tracer clinical-profile corpus build so every
    /// clean control carries a patient and reads like a believable feed; unlike IsCohortSequence it does
    /// not collapse a corpus onto one cached patient. When <see cref="IsCohortSequence"/> is already
    /// true the PATIENT group is force-included regardless, so this flag is a no-op there.
    /// </summary>
    public bool RequirePatientGroup { get; init; } = false;

    /// <summary>
    /// Force-populates a message's defining clinical content so a clean control reads like a real
    /// feed instead of a spec-legal skeleton. Three guarantees today: PID-8 (Administrative Sex) and
    /// PID-7 (Date/Time of Birth) are always populated — the field-inclusion draw otherwise blanked
    /// each on ~17% of clinical-profile messages, a rate no real feed shows, and a blank DOB blocks
    /// age-dependent clinical review outright (b67 pinned the sex; the DOB pin extends the same
    /// mechanism) — and a VXU^V04 always carries at least one RXA, whose enclosing ORDER group is
    /// optional so the inclusion draw could roll off the required immunization
    /// administration and leave "an immunization record with no vaccination". Complements
    /// <see cref="RequirePatientGroup"/> (which guarantees the patient, not the clinical payload); set
    /// alongside it by the Tracer clinical-profile corpus build. Default false — the seed-driven fuzz
    /// profile keeps its variety and its published corpus byte-identical. The guarantees are
    /// coordinate-addressed (ADR-0005), so a same-seed clinical corpus stays byte-reproducible.
    /// </summary>
    public bool RequireClinicalContent { get; init; } = false;

    /// <summary>
    /// Gets this message's index within its batch — the message-instance coordinate of
    /// ADR-0005 §3 (the instance index belongs in the coordinate, so two instances cannot
    /// collide). Batch loops set it per iteration; <see cref="GenerationDeterminism.CreateKey"/>
    /// folds it into the entropy root so every message in a Count=N run draws its own patient,
    /// control id, and field values while message i stays a pure function of (seed, type, i) —
    /// a longer batch shares its prefix with a shorter one. Null means an un-indexed call (a
    /// direct single-message seam), which keys identically to the pre-index contract. Never
    /// persisted: run manifests record batch-level inputs and replay re-derives indices through
    /// the same loop.
    /// </summary>
    public int? MessageIndex { get; init; }

    /// <summary>
    /// Gets the named clinical scenario (condition) pinning coherent clinical content for this
    /// generation — diagnoses, medications, and lab panels all draw from the one named
    /// <c>ClinicalScenarioRepository</c> entry (e.g. "sepsis", "chf") instead of a random
    /// weighted scenario. Populated from a workflow step's "condition" parameter when a catalog
    /// scenario drives generation; settable directly otherwise. Null preserves the existing
    /// random scenario selection. An unknown id falls back to random selection (the
    /// coordinator's documented contract).
    /// </summary>
    public string? ClinicalScenarioId { get; init; }

    /// <summary>
    /// The active clinical scenario's demographic/physiological constraints (required sex, age band,
    /// pregnancy), consulted by the entity samplers so the patient matches the story — a pregnancy
    /// scenario lands on a female of childbearing age, a sexed diagnosis on a matching sex. Set per
    /// message by the HL7 plugin from the scenario coordinator's current scenario; null leaves the
    /// patient a free demographic draw. Transient plumbing, not a caller input: it is re-derived
    /// from the seed on every run, so it is excluded from run-manifest persistence.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Pidgeon.Core.Domain.Clinical.ScenarioConstraints? ScenarioConstraints { get; init; }

    /// <summary>
    /// The administrative-sex prevalence used when a patient's sex is a free demographic draw (no
    /// scenario pins it). Null uses <see cref="Pidgeon.Core.Domain.Clinical.SexPrevalence.Realistic"/>
    /// (near-even Female/Male, Unknown ~1%). Set it to shape a cohort (an all-female population, a
    /// higher unknown rate for a stress corpus). A pinned scenario sex (pregnancy, RequiredSex) still
    /// wins over this — the prevalence only governs the otherwise-unconstrained draw.
    /// </summary>
    public Pidgeon.Core.Domain.Clinical.SexPrevalence? SexPrevalence { get; init; }

    /// <summary>
    /// Creates default generation options for algorithmic generation.
    /// </summary>
    public static GenerationOptions Default => new();

    /// <summary>
    /// Creates options for deterministic testing with specified seed.
    /// </summary>
    public static GenerationOptions ForTesting(int seed) => new() { Seed = seed };

    /// <summary>
    /// Creates options for AI-enhanced generation with BYOK.
    /// </summary>
    public static GenerationOptions WithAI(AIProvider provider, string apiKey) => new()
    {
        UseAI = true,
        Provider = provider,
        ApiKey = apiKey
    };
}