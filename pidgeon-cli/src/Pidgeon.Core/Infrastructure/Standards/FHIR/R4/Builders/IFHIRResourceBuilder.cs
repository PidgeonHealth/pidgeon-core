// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Dedicated builder for a single FHIR R4 resource type: one implementation per resource
/// type (Patient, Observation, ...), priority-based dispatch, null return
/// from <see cref="FHIRResourceBuilderRegistry.GetBuilder"/> when no
/// dedicated builder exists so callers can fall back to the generic path.
///
/// Builders own the full resource generation for their type: emit complete
/// FHIR R4 JSON, register the resource's ID on the context's
/// <see cref="FHIRReferenceMap"/> so downstream builders in the same bundle
/// can reference it, and honor the clinical scenario piped through the context.
/// </summary>
public interface IFHIRResourceBuilder
{
    /// <summary>
    /// FHIR resource type name this builder emits, e.g. "Patient",
    /// "Observation", "MedicationRequest". Case-insensitive match against
    /// the resourceType the caller requests.
    /// </summary>
    string ResourceType { get; }

    /// <summary>
    /// Higher priority wins when multiple builders claim the same
    /// ResourceType. Base builders use 0; scenario-specialised builders
    /// can register at higher priority to take over.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Produce a complete FHIR R4 JSON representation of the resource and
    /// record its ID on <c>context.References</c>. Implementations cast
    /// <c>context.DomainEntity</c> to the type they expect and return a
    /// Result.Failure with a readable error when the domain entity is
    /// missing or mistyped.
    /// </summary>
    Task<Result<string>> BuildAsync(
        FHIRBuildContext context,
        CancellationToken cancellationToken = default);
}
