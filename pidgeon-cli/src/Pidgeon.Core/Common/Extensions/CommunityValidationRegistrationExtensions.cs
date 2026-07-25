// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Standards;
using Pidgeon.Core.Application.Interfaces.Standards.HL7;
using Pidgeon.Core.Application.Services.Validation;
using Pidgeon.Core.Infrastructure.Data;
using Pidgeon.Core.Infrastructure.Standards.Common.HL7;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation.FhirPath;
using Pidgeon.Core.Infrastructure.Standards.FHIR.Validation;
using Pidgeon.Core.Infrastructure.Standards.HL7;
using Pidgeon.Core.Infrastructure.Standards.HL7.Validation;

namespace Pidgeon.Core.Extensions;

public static class CommunityValidationRegistrationExtensions
{
    /// <summary>
    /// Registers deterministic HL7 structural validation over the package-neutral data
    /// seam: the standards-agnostic <see cref="IMessageValidationService"/> dispatcher and
    /// the HL7 <see cref="IStandardValidationPlugin"/> fed by registered
    /// <see cref="Pidgeon.Core.Application.Interfaces.Data.IDataResourceResolver"/> providers
    /// (see <c>AddPidgeonBaselineData()</c>). This is the composition's explicit registration
    /// seam - community consumers wire capability through it rather than constructing
    /// internal types.
    /// </summary>
    public static IServiceCollection AddPidgeonHl7Validation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IHl7StructuralParser, Hl7StructuralParser>();
        services.TryAddSingleton<IHL7DataProviderFactory, HL7DataProviderFactory>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IStandardValidationPlugin, HL7ValidationPlugin>());
        services.TryAddSingleton<IMessageValidationService, MessageValidationService>();

        return services;
    }

    /// <summary>
    /// Registers deterministic FHIR R4 structural validation over the package-neutral data
    /// seam: the standards-agnostic <see cref="IMessageValidationService"/> dispatcher and the
    /// FHIR <see cref="IStandardValidationPlugin"/> composing the owned FHIRPath subset engine,
    /// profile/base validators, and a resolver-fed <see cref="IStructureDefinitionLoader"/> (base
    /// R4 SDs served by registered <see cref="IDataResourceResolver"/> providers - see
    /// <c>AddPidgeonBaselineData()</c>). Firely-free per ADR-0001. The installed-IG package path
    /// (<c>IFhirIGService</c>) is deliberately NOT wired by this extension - call
    /// <see cref="AddPidgeonFhirIgPackages"/> alongside it to light IG validation from installed
    /// data packages; without that call, <c>--profile</c> against an installed IG degrades to a
    /// typed config failure rather than a wrong answer, and base + explicit-file profile
    /// validation still run.
    /// </summary>
    /// <remarks>
    /// Prerequisite: the caller must register an <see cref="IDataResourceResolver"/> (e.g. via
    /// <c>AddPidgeonBaselineData()</c>) before invoking this. Absent one, the resolver-fed loader
    /// resolves no base StructureDefinitions and validation degrades to the required-field floor
    /// (PROFILE_NOT_FOUND) - an honest "cannot verify", never a false pass.
    /// </remarks>
    public static IServiceCollection AddPidgeonFhirValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Resolver-fed base-SD loader (community ctor): base R4 SDs light up when a data
        // package supplies them, else validation degrades to the required-field floor
        // (PROFILE_NOT_FOUND) - never a false pass.
        services.TryAddSingleton<IStructureDefinitionLoader>(sp =>
            new StructureDefinitionLoader(
                sp.GetRequiredService<ILogger<StructureDefinitionLoader>>(),
                sp.GetRequiredService<IDataResourceResolver>()));

        // Owned FHIRPath subset engine + profile/base validators (all File.*/pure - no
        // reflection, no legacy Data assembly). These are stateless over the injected
        // singleton loader, so they register Singleton here (the private monorepo lane
        // uses Scoped, but the community plugin + dispatcher are Singleton, and a Singleton
        // capturing a Scoped dependency is a captive-dependency defect - Singleton throughout
        // keeps the graph scope-clean under ValidateScopes).
        services.TryAddSingleton<IFhirPathSubsetEngine, FhirPathSubsetEngine>();
        services.TryAddSingleton(sp =>
            new FhirInvariantEvaluator(
                sp.GetRequiredService<IFhirPathSubsetEngine>(),
                sp.GetRequiredService<ILogger<FhirInvariantEvaluator>>()));
        services.TryAddSingleton<IValueSetValidator, ValueSetValidator>();
        services.TryAddSingleton<IProfileValidator, ProfileValidator>();
        services.TryAddSingleton(sp =>
            new BaseStructureDefinitionValidator(
                sp.GetRequiredService<IProfileValidator>()));
        services.TryAddSingleton<IFHIRValidator>(sp =>
            new FHIRValidator(
                sp.GetService<IProfileValidator>(),
                sp.GetService<IStructureDefinitionLoader>(),
                sp.GetService<BaseStructureDefinitionValidator>()));

        // FHIR plugin registered by concrete implementation type (mirrors the HL7 plugin
        // above): TryAddEnumerable dedupes by implementation type, and a factory-only
        // descriptor is indistinguishable from other IStandardValidationPlugin factories and
        // throws. DI injects the Singleton IProfileValidator; the optional IFhirIGService
        // parameter resolves from the container when AddPidgeonFhirIgPackages() registered it
        // and defaults to null otherwise - the installed-IG path is opt-in by composition.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IStandardValidationPlugin, FHIRValidationPlugin>());
        services.TryAddSingleton<IMessageValidationService, MessageValidationService>();

        return services;
    }

    /// <summary>
    /// Lights the installed-IG package path for community compositions: a filesystem
    /// <see cref="IDataPackageCatalog"/> over the standard install root (<c>PIDGEON_DATA_DIR</c>
    /// when set, else <c>~/.pidgeon/data</c>) and the <see cref="IFhirIGService"/> that resolves
    /// <c>--profile</c> against installed IG packages. Deliberately separate from
    /// <see cref="AddPidgeonFhirValidation"/> so the default composition stays degraded-honest
    /// (no machine-local packages leak into compositions that never asked for them); call both
    /// to enable IG validation. The dependency-package walker stays unwired here (its
    /// implementation carries the network-fetch surface), so ValueSets bound from dependency
    /// packages degrade to the validator's explicit not-validated outcome.
    /// </summary>
    public static IServiceCollection AddPidgeonFhirIgPackages(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IDataPackageCatalog, InstalledDataPackageCatalog>();
        services.TryAddSingleton<IFhirIGService, FhirIGService>();

        return services;
    }
}
