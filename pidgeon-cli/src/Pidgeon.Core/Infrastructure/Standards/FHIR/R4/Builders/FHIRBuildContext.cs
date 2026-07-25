// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Services.Clinical;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Input bundle carried into <see cref="IFHIRResourceBuilder.BuildAsync"/>.
///
/// Holds the domain entity the builder is about to serialize, the
/// generation options, a bundle-scoped <see cref="FHIRReferenceMap"/>
/// so cross-resource references (Observation.subject → Patient, etc.)
/// resolve deterministically within a bundle, and — when provided — a
/// <see cref="ClinicalScenarioCoordinator"/> so builders can pull
/// clinically coherent ICD-10 / LOINC / RxNorm codes that match what
/// the HL7 path produced for the same scenario.
/// </summary>
public sealed class FHIRBuildContext
{
    /// <summary>
    /// The domain entity to serialize. Typed by the caller; builders cast
    /// on entry and surface a clear error on type mismatch. Nullable so
    /// the context stays constructable in the rare cases where a builder
    /// derives everything from references alone (e.g., a Condition seeded
    /// purely from a registered Patient).
    /// </summary>
    public object? DomainEntity { get; init; }

    /// <summary>Generation options forwarded from the caller.</summary>
    public required GenerationOptions Options { get; init; }

    /// <summary>
    /// Bundle-scoped reference map. Fresh instance per bundle build; passed
    /// into every builder that runs in that bundle. Non-null even for
    /// single-resource generation — a fresh empty map is benign.
    /// </summary>
    public required FHIRReferenceMap References { get; init; }

    /// <summary>
    /// Per-bundle clinical scenario coordinator. When present, builders pull
    /// coherent codes (ICD-10, LOINC, RxNorm) from it so a bundle of
    /// Patient + Condition + Observation + MedicationRequest tells one
    /// consistent clinical story.
    ///
    /// Null is permitted — builders fall back to their random picks so
    /// single-resource generation still works without any scenario scope.
    /// The plugin passes a scoped coordinator when it recognises a
    /// scenario-driven entry point.
    /// </summary>
    public ClinicalScenarioCoordinator? ScenarioCoordinator { get; init; }

    /// <summary>
    /// Coordinate-addressed entropy key for this resource build. The orchestrator
    /// (FHIRResourceFactory / the generation plugin) derives it from the effective seed
    /// (<c>GenerationDeterminism.CreateKey(options).Derive("fhir").Derive(resourceType)</c>); builders
    /// draw their RNG and resource ids from it so a seeded run reproduces byte-for-byte. Defaults to a
    /// fresh coordinate-derived root for contexts built directly without the orchestrator (e.g. unit
    /// tests), mirroring the HL7 path's SegmentGenerationContext.Key.
    /// </summary>
    public GenerationKey Key { get; init; } = GenerationKey.Root(Random.Shared.Next());
}
