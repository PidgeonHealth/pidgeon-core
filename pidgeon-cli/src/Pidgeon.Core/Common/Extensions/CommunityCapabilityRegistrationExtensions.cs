// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Artifacts;
using Pidgeon.Core.Application.Interfaces.Capability;
using Pidgeon.Core.Application.Interfaces.Conformance;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Standards;
using Pidgeon.Core.Application.Services.Artifacts;
using Pidgeon.Core.Application.Services.Capability;
using Pidgeon.Core.Application.Services.Conformance;
using Pidgeon.Core.Application.Services.Configuration;
using Pidgeon.Core.Application.Services.DeIdentification;
using Pidgeon.Core.Domain.DeIdentification;
using Pidgeon.Core.Infrastructure.Data;
using Pidgeon.Core.Infrastructure.Standards.HL7.Capability;
using Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.DeIdentification;
using Pidgeon.Core.Infrastructure.Standards.NCPDP.Capability;

namespace Pidgeon.Core.Extensions;

/// <summary>Explicit registration seams for the community CLI's additional free capabilities.</summary>
public static class CommunityCapabilityRegistrationExtensions
{
    /// <summary>Registers vendor-spec grading beside the already-composed standard validators.</summary>
    public static IServiceCollection AddPidgeonCommunityProfileValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IProfileValidationService, ProfileValidationService>();
        return services;
    }

    /// <summary>Registers the HIPAA Safe Harbor de-identification graph without reflection.</summary>
    public static IServiceCollection AddPidgeonCommunityDeIdentification(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<HL7DeIdentifier>();
        services.TryAddScoped<PhiPatternDetector>();
        services.TryAddScoped<SafeHarborFieldMapper>();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IStandardDeIdentificationPlugin, HL7DeIdentificationPlugin>());

        services.TryAddScoped<IPhiDetector, PhiDetector>();
        services.TryAddScoped<IDeIdentificationService, DeIdentificationService>();
        services.TryAddScoped<ComplianceValidationService>();
        services.TryAddScoped<AuditReportService>();
        services.TryAddScoped<ResourceEstimationService>();
        services.TryAddScoped<IDeIdentificationEngine, DeIdentificationEngine>();
        return services;
    }

    /// <summary>
    /// Registers the public data-package manager and package-backed artifact catalog. The only
    /// network path is an explicit install of a registry-declared CC0 FHIR package; licensed
    /// packages remain member-supplied and pass through the acceptance gate before any fetch.
    /// </summary>
    public static IServiceCollection AddPidgeonCommunityDataPackages(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFhirPackageFetcher, HttpFhirPackageFetcher>();
        services.TryAddSingleton<IFhirIgDependencyResolver, FhirIgDependencyResolver>();
        services.AddHttpClient(nameof(HttpFhirPackageFetcher), client =>
        {
            var version = typeof(HttpFhirPackageFetcher).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"pidgeon/{version}");
        });
        services.TryAddSingleton<IDataPackageManager>(provider =>
            new CommunityDataPackageManager(
                provider.GetRequiredService<ILogger<CommunityDataPackageManager>>(),
                fetcher: provider.GetRequiredService<IFhirPackageFetcher>(),
                dependencyResolver: provider.GetRequiredService<IFhirIgDependencyResolver>()));
        services.TryAddScoped<IArtifactCatalogService, CommunityArtifactCatalogService>();
        return services;
    }

    /// <summary>
    /// Registers the per-(type, version) capability matrix for the community CLI. The signaling
    /// service runs the same live generate→validate→oracle sweep as the commercial build, over the
    /// community graph. The conformance service is registered with no independent oracles: the
    /// HL7/NCPDP XSD oracles depend on the member-only schema payloads (restricted resources absent
    /// from this composition), so a community cell is graded only up to its own spec validation.
    /// This is the intended honest floor — capability levels can only understate, never overstate,
    /// what this binary actually proves. The validate axis (C-CDA, X12) is likewise omitted because
    /// those validators are not part of the community composition.
    /// </summary>
    public static IServiceCollection AddPidgeonCommunityCapabilitySignaling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICapabilitySignalingService, CapabilitySignalingService>();
        services.TryAddScoped<ICapabilityEvidenceSource, RuntimeCapabilityEvidenceSource>();
        services.TryAddSingleton<IVersionedConformanceService, VersionedConformanceService>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStandardVersionSource, Hl7VersionSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStandardVersionSource, NcpdpVersionSource>());
        return services;
    }
}
