// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pidgeon.Core.Generation.Types;

namespace Pidgeon.Core.Generation;

/// <summary>
/// A saved record of one generation run: everything needed to reproduce its exact bytes later. A run
/// resolves its seed and clock up front (<see cref="GenerationDeterminism.ResolveRun"/>), writes this
/// manifest, and a later "--from-manifest" replay rebuilds the same request and options to regenerate
/// byte-for-byte. <see cref="DeterminismVersion"/> and <see cref="EngineVersion"/> flag a manifest written
/// by an engine whose generation contract has since changed.
///
/// The captured set is the procedural generation surface that affects output bytes. It records the
/// vendor selection (the friendly <see cref="GenerationOptions.VendorProfile"/> enum), because the
/// curated profile it resolves to is deterministic embedded data, so a vendor run replays byte-for-byte.
/// It deliberately excludes AI configuration (non-deterministic), API keys and other secrets, and the
/// free-form Context bag. Capturing a run that relies on those is rejected by <see cref="Capture"/> so a
/// manifest never claims a replay it cannot deliver.
/// </summary>
public sealed record GenerationRunManifest
{
    /// <summary>Target standard ("hl7", "fhir", "ncpdp").</summary>
    public required string Standard { get; init; }

    /// <summary>Message/resource/transaction type generated (e.g. "ADT^A01").</summary>
    public required string MessageType { get; init; }

    /// <summary>Number of messages generated in the run.</summary>
    public required int Count { get; init; }

    /// <summary>The effective root seed the run used (the supplied seed, or the one drawn for an unseeded run).</summary>
    public required int RootSeed { get; init; }

    /// <summary>The effective wall-clock the run was stamped with (the supplied <c>--as-of</c>, or the captured real time).</summary>
    public required DateTime AsOf { get; init; }

    /// <summary>The deterministic-contract version in force when the run was produced (<see cref="GenerationDeterminism.DeterminismVersion"/>).</summary>
    public required int DeterminismVersion { get; init; }

    /// <summary>The engine build that produced the run, from the assembly informational version.</summary>
    public required string EngineVersion { get; init; }

    /// <summary>
    /// True when this manifest's <see cref="DeterminismVersion"/> matches the running engine's, so a replay
    /// is expected to reproduce the same bytes. False means the determinism contract changed since the
    /// manifest was written and a replay may differ — the caller should flag that rather than mis-replay.
    /// </summary>
    [JsonIgnore]
    public bool IsCurrentDeterminismVersion => DeterminismVersion == GenerationDeterminism.DeterminismVersion;

    /// <summary>HL7 version the run targeted.</summary>
    public string Hl7Version { get; init; } = "2.3";

    /// <summary>Whether the HL7 version was explicitly pinned (affects version fallback).</summary>
    public bool Hl7VersionExplicit { get; init; }

    /// <summary>NCPDP SCRIPT version the run targeted, when the NCPDP path was used.</summary>
    public string? NcpdpVersion { get; init; }

    /// <summary>Patient type the run generated.</summary>
    public PatientType PatientType { get; init; } = PatientType.General;

    /// <summary>Whether the run produced a patient-coherent cohort sequence.</summary>
    public bool IsCohortSequence { get; init; }

    /// <summary>Whether the run force-included the PATIENT group on every message (no cohort sharing).</summary>
    public bool RequirePatientGroup { get; init; }

    /// <summary>Named clinical scenario pinned for the run, if any.</summary>
    public string? ClinicalScenarioId { get; init; }

    /// <summary>Optional-segment inclusion probabilities the run used, if overridden.</summary>
    public IReadOnlyDictionary<string, double>? SegmentProbabilities { get; init; }

    /// <summary>Segment repeat counts the run used, if overridden.</summary>
    public IReadOnlyDictionary<string, int>? SegmentRepeatCounts { get; init; }

    /// <summary>Named lock session the run applied, if any. Replay assumes the session still exists.</summary>
    public string? LockSessionName { get; init; }

    /// <summary>Vendor profile the run selected, if any. The curated profile it resolves to is embedded
    /// deterministic data, so recording the selector is enough to replay the run byte-for-byte.</summary>
    public VendorProfile? VendorProfile { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Captures a manifest from a request plus its resolved options (the output of
    /// <see cref="GenerationDeterminism.ResolveRun"/>, so Seed and AsOf are non-null). Rejects runs whose
    /// output cannot be reproduced from the captured set: AI-enhanced, vendor-profiled, adhoc-locked, or
    /// Context-driven generation.
    /// </summary>
    public static GenerationRunManifest Capture(string standard, string messageType, int count, GenerationOptions resolved)
    {
        if (resolved.Seed is not int seed)
            throw new ArgumentException("Resolved options must carry a seed.", nameof(resolved));
        if (resolved.AsOf is not DateTime asOf)
            throw new ArgumentException("Resolved options must carry an AsOf clock.", nameof(resolved));
        if (resolved.UseAI)
            throw new InvalidOperationException("AI-enhanced runs are non-deterministic and cannot be captured as a replayable manifest.");
        if (resolved.AdhocLockedValues is { Count: > 0 })
            throw new InvalidOperationException("Runs with inline adhoc locked values are not captured by a manifest in this version.");
        if (resolved.Context.Count > 0)
            throw new InvalidOperationException("Runs with a populated Context are not captured by a manifest in this version.");
        // A directly-set resolved profile with no VendorProfile enum cannot be reconstructed on replay:
        // the manifest records only the friendly enum selector (here null), so ToOptions would rebuild
        // nothing and the run would replay unshaped. Reject it like the other non-reproducible inputs.
        if (resolved.ActiveVendorProfile is not null && resolved.VendorProfile is null)
            throw new InvalidOperationException("Runs with a directly-set vendor profile (no VendorProfile enum) are not captured by a manifest: the resolved profile is not recorded and would replay unshaped.");

        return new GenerationRunManifest
        {
            Standard = standard,
            MessageType = messageType,
            Count = count,
            RootSeed = seed,
            AsOf = asOf,
            DeterminismVersion = GenerationDeterminism.DeterminismVersion,
            EngineVersion = ResolveEngineVersion(),
            Hl7Version = resolved.Hl7Version,
            Hl7VersionExplicit = resolved.Hl7VersionExplicit,
            NcpdpVersion = resolved.NcpdpVersion,
            PatientType = resolved.Type,
            IsCohortSequence = resolved.IsCohortSequence,
            RequirePatientGroup = resolved.RequirePatientGroup,
            ClinicalScenarioId = resolved.ClinicalScenarioId,
            SegmentProbabilities = resolved.SegmentProbabilities,
            SegmentRepeatCounts = resolved.SegmentRepeatCounts,
            LockSessionName = resolved.LockSessionName,
            VendorProfile = resolved.VendorProfile
        };
    }

    /// <summary>
    /// Rebuilds the fully resolved <see cref="GenerationOptions"/> for a replay. The seed and AsOf pin the
    /// entropy so the run regenerates byte-for-byte.
    /// </summary>
    public GenerationOptions ToOptions() => new()
    {
        Seed = RootSeed,
        AsOf = AsOf,
        Hl7Version = Hl7Version,
        Hl7VersionExplicit = Hl7VersionExplicit,
        NcpdpVersion = NcpdpVersion,
        Type = PatientType,
        IsCohortSequence = IsCohortSequence,
        RequirePatientGroup = RequirePatientGroup,
        ClinicalScenarioId = ClinicalScenarioId,
        SegmentProbabilities = SegmentProbabilities as Dictionary<string, double>
            ?? (SegmentProbabilities is null ? null : new Dictionary<string, double>(SegmentProbabilities)),
        SegmentRepeatCounts = SegmentRepeatCounts as Dictionary<string, int>
            ?? (SegmentRepeatCounts is null ? null : new Dictionary<string, int>(SegmentRepeatCounts)),
        LockSessionName = LockSessionName,
        VendorProfile = VendorProfile
    };

    /// <summary>Serializes the manifest to JSON (snake_case, indented).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Serializes the manifest to canonical JSON: the same snake_case field set and enum/null rules as
    /// <see cref="ToJson"/>, but keys sorted ordinally at every level and no whitespace. Required for a
    /// stable <c>run_id</c> (SHAREABLE_RUN_STANDARD.md §2.3): the display serializer preserves dictionary
    /// insertion order for <see cref="SegmentProbabilities"/> / <see cref="SegmentRepeatCounts"/>, which is
    /// not stable across producers, so identity derivation must use this canonical form.
    /// </summary>
    public string ToCanonicalJson() => CanonicalJson.Serialize(this, JsonOptions);

    /// <summary>Reads a manifest back from JSON.</summary>
    public static GenerationRunManifest FromJson(string json)
        => JsonSerializer.Deserialize<GenerationRunManifest>(json, JsonOptions)
           ?? throw new ArgumentException("Manifest JSON did not deserialize to a manifest.", nameof(json));

    private static string ResolveEngineVersion()
        => typeof(GenerationRunManifest).Assembly
               .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? "dev";
}
