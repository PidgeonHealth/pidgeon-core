// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Generation;

/// <summary>
/// Single source of the per-run random seed and wall-clock used during wire composition, plus the
/// run-resolution step that pins them. <see cref="ResolveRun"/> turns possibly-blank options into a
/// fully resolved run: an effective seed (the supplied <see cref="GenerationOptions.Seed"/> or a
/// freshly drawn one) and an effective clock (the supplied <see cref="GenerationOptions.AsOf"/> or the
/// real <see cref="DateTime.Now"/>). Recording those two values in a run-manifest lets any run, seeded
/// or not, replay byte-for-byte.
///
/// The composer creates one RNG/clock pair per message from the resolved options and threads it through
/// the <see cref="SegmentGenerationContext"/> so every resolver and segment builder draws from the same
/// deterministic sequence rather than its own ungoverned <c>new Random()</c>.
/// </summary>
public static class GenerationDeterminism
{
    /// <summary>
    /// Version of the deterministic generation contract. Bump whenever a change to seed handling, the
    /// clock, or a coordinate re-key alters the bytes a given (seed, options) produces, so a
    /// run-manifest written by an older engine is flagged rather than silently mis-replayed.
    ///
    /// v2 (2026-07-09): the batch message index joined the entropy coordinate
    /// (<see cref="GenerationOptions.MessageIndex"/>, folded by <see cref="CreateKey"/>). Under v1
    /// every message in a Count=N HL7/NCPDP batch shared one coordinate root — one patient, one
    /// MSH-10 control id, one draw of every field; only the separately-indexed clinical scenario
    /// varied. v2 realizes ADR-0005 §3 (the instance index is in the coordinate), so batches emit
    /// distinct patients and control ids and message i is a pure function of (seed, type, i).
    ///
    /// v3 (2026-07-09): the FHIR batch loop joined the <see cref="GenerationOptions.MessageIndex"/>
    /// coordinate. Under v2 it still reseeded per iteration
    /// (<c>Seed = batchKey.Derive(i).AsRandom().Next()</c>) — truncating the derived key to a
    /// 31-bit seed (birthday-collision odds reach ~50% around ~55k resources) with a keying
    /// scheme divergent from HL7/NCPDP. v3 sets the index per iteration like the other plugins,
    /// so FHIR bytes for a given (seed, options) change at every count (the old loop reseeded
    /// even single-resource service calls); HL7 and NCPDP bytes are unchanged.
    ///
    /// v4 (2026-07-09): the coherence-correctness wave. Three fixes move HL7 bytes for the
    /// affected coordinates. b36 (scenario-cache epoch): the AddScoped OBX/DG1 value contributors
    /// keyed their scenario draw on the scenario id alone, so a second same-scope message reusing
    /// the same scenario id inherited the previous call's lab/diagnosis values; the message key now
    /// joins the cache key, so a same-scope repeat redraws its own. b37 (cohort contract): the
    /// pharmacy plugins (RDE/RGV/RAS) now consult the shared CohortPatient like ADT/ORM/ORU, and a
    /// cohort message force-includes the PID segment and its enclosing groups so the shared patient
    /// always renders. b38 (CON schema resolution, sibling slice oracle-data-repairs): the MDM
    /// CON segment resolves to a populated schema instead of a bare line. b50 (cross-cell OBX): OBX
    /// observations are drawn against a message-wide cursor instead of the per-cell Set ID, so a
    /// message's second OBX cell (the SPECIMEN OBX after the ORDER OBSERVATION result) continues the
    /// lab cycle rather than restating the first observation; the composer walk also drops any
    /// repeatable segment whose content duplicates an earlier one message-wide (a restated OBX/ROL/
    /// ARV/TQ2). Single-OBX messages are unchanged; multi-OBX (and duplicate-carrying) coordinates
    /// move. v4 is the single version boundary for the whole wave; only the touched coordinates
    /// change bytes.
    ///
    /// v5 (2026-07-10): the realism wave (S2 realism-labs). OBX lab values become condition-conditioned
    /// draws from a population-normal reference interval + a directed sampling shift (retiring the
    /// uniform-within-band draw); the OBX-7 reference range reports the population-normal interval
    /// (reference-intervals.yaml) rather than the sampled band (so TSH reads 0.4-4.0, not 5.0-50.0);
    /// the OBX-8 flag is derived by interval membership (AbnormalFlagRule) rather than the band
    /// midpoint; and correlated analytes co-vary through a Gaussian copula keyed on a shared labs
    /// coordinate (scenarioKey.Derive("labs"), ADR-0005 §7). Only OBX-carrying messages change bytes;
    /// the OBX-5 value, OBX-7 range, and OBX-8 flag all move together for the touched coordinates. The
    /// distributional KS re-fit against the NHANES microdata is deferred (device-gated, REALISM_PROGRAM
    /// §5/§6.4) and will be its own explicit later bump, not folded into v5.
    ///
    /// v6 (2026-07-11): the flag agrees with the wire (b83). v5 evaluated the OBX-8 membership rule on
    /// the raw doubles while OBX-5/OBX-7 render at F1 precision, so a raw value just past a bound (1.32
    /// against a raw high of 1.30) emitted H while the wire read "1.3 vs 0.6-1.3" — Normal to every
    /// downstream reader, including the generation lint. The flag is now evaluated at emission precision
    /// (AbnormalFlagRule.EvaluateRendered — the same membership rule applied to the rendered numbers).
    /// Only OBX-8 flags whose value or bound is rendered-equal move (H/L to N); values and ranges are
    /// byte-identical.
    ///
    /// v7 (2026-07-11): the ordered medication treats the message's DG1 (S5 B8). OrderMedicationSource
    /// resolves the scenario's primary diagnosis (the DG1 the composer renders) and, when it indicates
    /// medications via the clinical relationship graph, names the PRIMARY order from that set — so an
    /// RXE/RXO/RXG chain's drug coheres with the diagnosis the message carries instead of drawing from a
    /// scenario-wide grab-bag or, ~half the time, an out-of-scenario broad-pool drug. The old flat 0.5
    /// broad-pool REPLACEMENT (BroadPoolFraction) is retired; the broad pool remains the primary source
    /// only in the no-DG1-med fallback, with the drug-breadth lever re-semanticised as the configurable
    /// IncidentalOrderFraction (default 0.25). The DG1 link and the incidental gate each draw off their
    /// own child coordinate of MessageKey.Derive("order-medication"), so only RX-carrying messages
    /// (RDE/RGV/RAS) move bytes — the two committed RDE^O11 snapshots (2.5.1, 2.7); no non-order golden
    /// changes. Chain-internal agreement (RXO-1/RXE-2/RXG-4 name one drug, audit D-05) is preserved.
    /// b81 folds its OBX-precision golden deltas into this same v7 re-base.
    ///
    /// v8 (2026-07-12): VXU immunization coherence (b120). Two fixes move HL7 bytes for the affected
    /// coordinates. First, a VXU^V04 no longer carries its optional OBSERVATION subgroup: that group's
    /// Required OBX was being filled with a general scenario lab (troponin, HbA1c, creatinine) that
    /// reads as a lab result on an immunization record, so the optional group is omitted rather than
    /// fabricating an out-of-context observation (RXA, the vaccination, is untouched). Only VXU messages
    /// that previously drew the OBSERVATION group move — they lose their OBX/NTE lines. Second, a
    /// generated date of birth is clamped so it never post-dates the message clock: the ±1-year birthday
    /// jitter could push a newborn's DOB past the clock and yield a negative computed age, which the
    /// vaccine age-plausibility check (SEM-F12) flagged as a clean-corpus VXU false positive once PID-7
    /// was always populated. Only patients whose jittered DOB fell in the future move (a newborn's date
    /// snaps back to the clock); every other DOB is already in the past and is byte-identical.
    ///
    /// v9 (2026-07-12): coherent narrative notes across several axes (b73 wire-up). The NTE contributor
    /// became a position-aware dispatcher: an occasional NTE now restates whichever coded fact its group
    /// carries, not only a post-OBX lab result. Three new NTE coordinates move bytes for the messages
    /// that gain a note — a medication NTE after an RXE/RXO (Derive("nte-medication")), an order-status
    /// NTE after an OBR (Derive("nte-order-status")), and, in the ORU ORDER OBSERVATION group, a
    /// non-flagged result's order↔result note (Derive("nte-order-result")). The post-OBX lab restatement
    /// is UNCHANGED: a flagged OBX — and any observation NTE outside the ORU order group — still draws
    /// Derive("nte-narrative") through the same lab composer, byte-for-byte, so the SEM-F15 anchor and
    /// the reviewer packet are preserved. Only messages that gain or change an NTE line move (ORU, ORM,
    /// RDE); VXU and every non-NTE coordinate are byte-identical. NTE inclusion odds and the runtime fact
    /// precondition are position-keyed through NarrativeNotePosition, so no note fires without its coded
    /// fact in scope. (A message-wide all-normal panel summary is deferred: a forward-pass contributor
    /// cannot see a later cell's OBX, so the claim could contradict an abnormal result it has not
    /// reached — it needs whole-message OBX visibility.)
    ///
    /// v10 (2026-07-12): specimen↔analyte and route↔form coherence in generation (clinical-review
    /// finding). Two fixes move HL7 bytes for the affected coordinates. First, the order's specimen
    /// (OBR-15 SPS.1 and SPM-4) is chosen coherent with the ordered analyte: the first lab's LOINC
    /// System axis (loinc_common) names the acceptable specimen systems, and the 0070→system crosswalk
    /// (specimen-loinc.csv) names the specimen codes in them, so a serum analyte carries a serum/plasma
    /// specimen instead of a uniform-random Vitreous Fluid / Urine / Keloid. Only OBR-15/SPM-4 on an
    /// order whose analyte is system-resolvable move — the specimen field's own coordinate, so no
    /// other field shifts; an unresolvable analyte keeps the generic table pick. loinc_common gains
    /// Troponin I (10839-9) so the flagship serum-cardiac order coheres. Second, a pharmacy order's
    /// dosage form (RXE-6) and route (RXE-7) are held compatible per route-form.yaml (SEM-F09): a
    /// route the form cannot take is replaced by an allowed one (off Derive("order-medication")
    /// → "route_harmonize"), and a curated inhaler edge (respiratory.yaml) now carries its
    /// inhalation form/route so an inhaler no longer renders as an oral tablet. Only an incompatible
    /// pair draws, so a coherent order (the committed RDE^O11 snapshots' metoprolol, TAB/PO) is
    /// byte-identical; specimen-carrying ORU/OBR coordinates re-base.
    ///
    /// v11 (2026-07-13): widen analyte-aware specimen coverage (b128). v10 wired the specimen
    /// resolver but it only fired for analytes present in loinc_common, which held only Troponin I
    /// among the corpus's serum orders — so an order whose primary analyte was an uncovered serum
    /// marker (BNP, hCG, T4 free, bicarbonate) or a whole-blood order (blood culture) still fell back
    /// to the uniform table-0070 pick and could carry an incoherent specimen. loinc_common gains
    /// those five analytes with their LOINC System axis (BNP / hCG / T4 free / bicarbonate → Ser/Plas,
    /// blood culture → Bld), so an order led by one of them now draws a serum/plasma (or whole-blood)
    /// specimen: on the clinical corpus the count of clean controls whose serum primary analyte sat on
    /// a non-serum specimen falls 15 → 0. loinc_common is also the covered-analyte gate for the OBX
    /// reference range / abnormal flag (OBX-7/OBX-8), so a newly-covered analyte's observation gains an
    /// honest range in place of the prior synthetic band — BNP resolves its curated interval (0-100
    /// pg/mL, so an elevated value now flags H) and an analyte with no curated interval (hCG) drops the
    /// fabricated band rather than inventing one; an NTE that restates such an OBX moves with it. The
    /// ordered lab set (OBX-3 identifiers), the OBX-5 values, units, and every other field stay
    /// byte-identical; only the specimen (OBR-15 SPS.1 / SPM-4), the newly-covered analytes' OBX-7/8,
    /// and any NTE restating a moved OBX re-base.
    ///
    /// v12 (2026-07-17): the counsel-mandated trim of over-500-char spec-prose description strings
    /// (license-provenance Q2, Option B) plus the segment repeat-range hardening it exposed.
    /// True clinical classifications (OBX/OBR/SPM/CSS five repeats, IAM two) moved to code-keyed
    /// ranges in HL7SegmentStatistics; segments whose ranges rode incidental prose English
    /// ("as a result of…" — ERR, ORC, OVR, REL, TQ2, PRT) correct to the (1, 2) default.
    /// (SID and IIM keep their keyword-derived (1, 5): their trimmed leading sentences still carry
    /// the triggering words, so their behavior is unchanged.) Same-seed output changes only where
    /// a corrected segment's repeat draw lands differently.
    /// </summary>
    public const int DeterminismVersion = 12;

    /// <summary>
    /// Resolves possibly-blank options into a fully reproducible run: fills
    /// <see cref="GenerationOptions.Seed"/> with a freshly drawn seed when absent, and
    /// <see cref="GenerationOptions.AsOf"/> with the real current time when absent. Callers (CLI,
    /// Bridge) resolve once up front, surface the effective seed, and record the resolved options in a
    /// run-manifest so the run replays byte-for-byte.
    /// </summary>
    public static GenerationOptions ResolveRun(GenerationOptions? options)
    {
        var resolved = options ?? new GenerationOptions();
        return resolved with
        {
            Seed = resolved.Seed ?? Random.Shared.Next(),
            // UTC so the recorded clock serialises and replays identically on any machine or timezone.
            AsOf = resolved.AsOf ?? DateTime.UtcNow
        };
    }

    /// <summary>
    /// Resolves the per-message wall-clock: the explicit <see cref="GenerationOptions.AsOf"/> when set,
    /// otherwise the real current UTC time. After <see cref="ResolveRun"/> has run, AsOf is always present
    /// so composition is reproducible; a direct caller that passes no AsOf gets the live clock.
    /// </summary>
    public static DateTime CreateClock(GenerationOptions? options)
        => options?.AsOf ?? DateTime.UtcNow;

    /// <summary>
    /// Creates the root <see cref="GenerationKey"/> for an entity-generation run from the
    /// effective seed. Callers resolve the seed first via <see cref="ResolveRun"/> (which draws a random
    /// seed when none was supplied and surfaces it), so the root is reproducible and recordable; the
    /// inline fallback keeps an unresolved call working, just unrecorded.
    ///
    /// When the options carry a <see cref="GenerationOptions.MessageIndex"/>, the root narrows to that
    /// message's coordinate (ADR-0005 §3): every downstream derivation — entity generation, segment
    /// composition, control ids — then draws from a per-message stream, so a Count=N batch cannot
    /// collapse onto one patient or duplicate MSH-10 across messages. A null index keys at the run
    /// root, matching the un-indexed (single direct call) contract.
    /// </summary>
    public static GenerationKey CreateKey(GenerationOptions? options)
    {
        var root = GenerationKey.Root(options?.Seed ?? Random.Shared.Next());
        return options?.MessageIndex is { } index ? root.Derive("message").Derive(index) : root;
    }
}
