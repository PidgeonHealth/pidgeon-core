// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Application.Services.Generation.Plugins;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

namespace Pidgeon.Core.Extensions;

/// <summary>
/// Registers deterministic FHIR R4 generation for the community composition:
/// the resource-builder registry, the per-resource builders, the resource factory,
/// and the FHIR generation plugin behind <see cref="IMessageGenerationService"/>.
/// The community capability claim stays the contract matrix (FHIR R4
/// Patient/Observation/Bundle); the builder registry routes other resource types
/// on a per-type capability basis without over-claiming.
/// </summary>
public static class CommunityFhirGenerationRegistrationExtensions
{
    /// <summary>
    /// Registers FHIR generation for the community composition. Composes
    /// <see cref="CommunityGenerationRegistrationExtensions.AddPidgeonHl7Generation"/>
    /// (every registration there is idempotent) because the FHIR plugin shares the
    /// entity generators, clinical scenario core, and dispatcher with HL7 generation.
    /// </summary>
    /// <remarks>
    /// Prerequisite: the caller must register an
    /// <see cref="Application.Interfaces.Data.IDataResourceResolver"/> (e.g. via
    /// <c>AddPidgeonBaselineData()</c>) before invoking this. FHIR resource building
    /// is code-driven (no data-resolver dependency at build time); the resolver feeds
    /// the shared entity/demographics/lab-realism seams, all of which degrade
    /// honestly when data is absent.
    ///
    /// Explicit per-builder registration replaces the monorepo's reflection scan
    /// (<c>AddFHIRResourceBuilders</c>); lifetimes mirror the monorepo composition
    /// verbatim (factory, registry, and builders all Scoped).
    /// </remarks>
    public static IServiceCollection AddPidgeonFhirGeneration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPidgeonHl7Generation();

        services.TryAddScoped<IFHIRResourceFactory, FHIRResourceFactory>();
        services.TryAddScoped<FHIRResourceBuilderRegistry>();

        // Per-resource builders, explicit. A builder added to the monorepo's
        // reflection scan does not appear here automatically - admit + register
        // deliberately, the same discipline as the field-resolver allowlist.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, AllergyIntoleranceResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, BundleResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, CarePlanResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, CareTeamResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, ClaimResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, ConditionResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, CoverageResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, DiagnosticReportResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, DocumentReferenceResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, EncounterResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, ImmunizationResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, LocationResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, MedicationAdministrationResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, MedicationDispenseResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, MedicationRequestResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, MedicationResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, ObservationResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, OrganizationResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, PatientResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, PractitionerResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, PractitionerRoleResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, ProcedureResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, RelatedPersonResourceBuilder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFHIRResourceBuilder, ServiceRequestResourceBuilder>());

        // The generation entry point. Explicit registration replaces the monorepo's
        // reflection scan over Generation.Plugins.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMessageGenerationPlugin, FHIRMessageGenerationPlugin>());

        return services;
    }
}
