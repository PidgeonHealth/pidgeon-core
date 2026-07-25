// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Application.Interfaces.Standards;
using Pidgeon.Core.Application.Interfaces.VendorIntelligence;
using Pidgeon.Core.Application.Services.Clinical;
using Pidgeon.Core.Application.Services.Generation.Entities;
using Pidgeon.Core.Application.Services.Generation.Plugins;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Infrastructure.Generation.Constraints;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

namespace Pidgeon.Core.Extensions;

/// <summary>
/// Registers deterministic HL7 message generation over the package-neutral data seam:
/// <see cref="IMessageGenerationService"/> dispatching to the HL7 generation plugin, the
/// entity generators, the constraint stack, the clinical scenario core, and the message
/// composition graph. The community capability claim stays the contract matrix (HL7 v2.3
/// ADT + ORU); the composer routes other types on a per-(type, version) capability basis
/// without over-claiming.
/// </summary>
public static class CommunityGenerationRegistrationExtensions
{
    /// <summary>
    /// Registers HL7 generation for the community composition.
    /// </summary>
    /// <remarks>
    /// Prerequisite: the caller must register an
    /// <see cref="Application.Interfaces.Data.IDataResourceResolver"/> (e.g. via
    /// <c>AddPidgeonBaselineData()</c>) before invoking this. Absent installed data,
    /// generation degrades honestly: constraints relax to unconstrained, demographics
    /// fall back to hardcoded placeholder values, and note/fallback content is omitted —
    /// output stays structurally valid and deterministic, never fabricated failures.
    ///
    /// Lifetimes mirror the private monorepo composition verbatim; the Singleton/Scoped
    /// mix encodes per-message state boundaries and per-process caches.
    ///
    /// <see cref="Application.Interfaces.Reference.IStandardReferenceService"/> is a
    /// constraint-stack prerequisite; <c>AddPidgeonHl7Reference()</c> is composed in
    /// behind a guard because its seven per-version plugins register with plain
    /// <c>AddSingleton</c> and would duplicate on a second call.
    ///
    /// Deliberately absent from this composition (documented in the disposition
    /// register): the private clinical data-source family and the resolvers/contributors
    /// that require it, the vendor-profile loader (an embedded-assembly scan; a null
    /// resolver keeps <c>MessageGenerationService</c> profile-free), and AI generation
    /// modes. FHIR generation composes via <c>AddPidgeonFhirGeneration()</c>; NCPDP
    /// generation is the deliberately separate, attestation-gated
    /// <c>AddPidgeonNcpdpGeneration()</c>.
    /// </remarks>
    public static IServiceCollection AddPidgeonHl7Generation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Reference capability (constraint extraction dispatches through
        // IStandardReferenceService). Guarded for idempotency.
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IStandardReferencePlugin)))
        {
            services.AddPidgeonHl7Reference();
        }

        services.AddMemoryCache();
        services.AddCommunityHl7CompositionGraph();
        services.AddCommunityFieldValueResolvers();

        // Constraint stack: singleton helpers over the resolver-seamed data loader; the
        // per-scope plugin surfaces them to the scoped ConstraintResolver.
        services.TryAddSingleton<HL7ConstraintDataLoader>();
        services.TryAddSingleton<HL7PatternEnhancer>();
        services.TryAddSingleton<HL7ConstraintExtractor>();
        services.TryAddSingleton<HL7ConstraintValueGenerator>();
        services.TryAddSingleton<HL7ConstraintValueValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConstraintResolverPlugin, HL7ConstraintResolverPlugin>());
        services.TryAddScoped<IConstraintResolver, Application.Services.Generation.ConstraintResolver>();

        // Clinical scenario core: hardcoded scenario/reference data, coordinator falls
        // back to the repository when the optional relationship graph and lab valuer are
        // not composed.
        services.TryAddSingleton<ClinicalScenarioRepository>();
        services.TryAddSingleton<LabReferenceRangeProvider>();
        services.TryAddScoped<ClinicalScenarioCoordinator>();

        // Lab-realism valuation (RI-1/RI-4) over the resolver seam: the population-normal
        // reference-interval source, the cited lab-lab correlation seed, and the
        // coordinate-addressed marginal+copula engine. Singletons mirroring the monorepo
        // graph (cited data, process-lifetime cached; the engine is stateless over its
        // GenerationKey input). Absent clinical YAMLs degrade honestly: the copula stays
        // on the uncorrelated prior and uncovered analytes take the whole-or-absent path.
        services.TryAddSingleton<Application.Interfaces.Clinical.IReferenceIntervalProvider,
                                 Application.Services.Clinical.Realism.ReferenceIntervalProvider>();
        services.TryAddSingleton<Application.Interfaces.Clinical.ILabCorrelationProvider,
                                 Application.Services.Clinical.Realism.LabCorrelationProvider>();
        services.TryAddSingleton<Application.Services.Clinical.Realism.LabValueEngine>();
        services.TryAddSingleton<Application.Services.Clinical.Realism.ScenarioLabValuer>();

        // Demographics faker service over the resolver seam.
        services.TryAddScoped<IDemographicsDataService, Application.Services.Reference.DemographicsDataService>();

        // Lock-session stack (required by LockedValueApplier). No session is active
        // unless a host names one, so the storage provider stays untouched by default.
        services.TryAddScoped<ILockSessionService, Application.Services.Configuration.LockSessionService>();
        services.TryAddScoped<ILockStorageProvider, Infrastructure.Configuration.FileSystemLockStorageProvider>();

        // Entity generators: scoped so they share the per-generation determinism context.
        services.TryAddScoped<LockedValueApplier>();
        services.TryAddScoped<PatientGenerator>();
        services.TryAddScoped<ProviderGenerator>();
        services.TryAddScoped<MedicationGenerator>();
        services.TryAddScoped<PrescriptionGenerator>();
        services.TryAddScoped<EncounterGenerator>();
        services.TryAddScoped<ObservationResultGenerator>();
        services.TryAddScoped<IGenerationService, Application.Services.Generation.GenerationService>();

        // Plugin registry and message factory consumed by the HL7 generation plugin.
        // Registry enumerables resolve empty in this composition, which is valid.
        services.TryAddScoped<IStandardPluginRegistry, Infrastructure.Registry.StandardPluginRegistry>();
        services.TryAddScoped<IHL7MessageFactory, HL7v23MessageFactory>();

        // The generation entry points. Explicit registration replaces the monorepo's
        // reflection scan over Generation.Plugins.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMessageGenerationPlugin, HL7MessageGenerationPlugin>());
        services.TryAddSingleton<IVendorProfileResolver, NullVendorProfileResolver>();
        services.TryAddScoped<IMessageGenerationService, Application.Services.Generation.MessageGenerationService>();

        return services;
    }

    /// <summary>
    /// No-profile vendor resolver: generation runs without vendor dialects until vendor
    /// profiles ship as installable data packages. Returning the options unchanged is
    /// the monorepo's own behavior when no vendor is selected.
    /// </summary>
    private sealed class NullVendorProfileResolver : IVendorProfileResolver
    {
        public GenerationOptions? Resolve(GenerationOptions? options) => options;
    }
}
