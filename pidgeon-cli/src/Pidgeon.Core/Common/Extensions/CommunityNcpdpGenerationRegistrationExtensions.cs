// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pidgeon.Core.Application.DTOs.Data;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Application.Services.Generation;
using Pidgeon.Core.Application.Services.Generation.Plugins;
using Pidgeon.Core.Infrastructure.Data;
using Pidgeon.Core.Infrastructure.Standards.NCPDP;
using Pidgeon.Core.Infrastructure.Standards.NCPDP.Serialization;

namespace Pidgeon.Core.Extensions;

/// <summary>
/// Registers NCPDP SCRIPT generation for the community composition — deliberately a separate
/// opt-in extension (never composed by <c>AddPidgeonHl7Generation</c> or
/// <c>AddPidgeonFhirGeneration</c>), mirroring how <c>AddPidgeonFhirIgPackages</c> is separate.
/// NCPDP is double-locked: a host must compose this extension AND the attestation gate must
/// find the member-supplied <c>ncpdp-script</c> package installed (its presence proves
/// <c>--accept-license</c> was given). Without both, NCPDP generation stays closed —
/// the plugin advertises no message types and generation returns an honest license-required
/// failure, never output.
/// </summary>
public static class CommunityNcpdpGenerationRegistrationExtensions
{
    /// <summary>
    /// Registers NCPDP SCRIPT generation behind the catalog-resolved attestation gate.
    /// </summary>
    /// <remarks>
    /// Composes <see cref="CommunityGenerationRegistrationExtensions.AddPidgeonHl7Generation"/>
    /// (idempotent) for the shared entity/determinism graph. The gate resolves against the
    /// read-only <see cref="IDataPackageCatalog"/> — the commercial package manager stays
    /// private. Lifetimes mirror the monorepo composition verbatim. RxNorm enrichment is a
    /// null object: seeded runs never consult it, and unseeded runs fall back exactly as the
    /// monorepo does with no rxnorm package installed.
    /// </remarks>
    public static IServiceCollection AddPidgeonNcpdpGeneration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddPidgeonHl7Generation();

        // Filesystem catalog over PIDGEON_DATA_DIR / ~/.pidgeon/data (mirrors
        // AddPidgeonFhirIgPackages); TryAdd lets tests/hosts pre-register a fake.
        services.TryAddSingleton<IDataPackageCatalog, InstalledDataPackageCatalog>();

        // The attestation gate, snapshotted per scope like the monorepo registration.
        services.TryAddScoped<INcpdpAccessGate>(sp =>
            NcpdpAccessGate.Resolve(sp.GetRequiredService<IDataPackageCatalog>()));

        services.TryAddSingleton<IRxNormDataSource, EmptyRxNormDataSource>();

        services.TryAddEnumerable(ServiceDescriptor.Scoped<INcpdpBodySerializer, Ncpdp2017071BodySerializer>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<INcpdpBodySerializer, Ncpdp2023BodySerializer>());
        services.TryAddScoped<INCPDPXmlSerializer, NCPDPXmlSerializer>();
        services.TryAddScoped<INCPDPDataGenerator, NCPDPDataGenerator>();

        // The generation entry point. Explicit registration replaces the monorepo's
        // reflection scan over Generation.Plugins.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMessageGenerationPlugin, NCPDPMessageGenerationPlugin>());

        return services;
    }

    /// <summary>
    /// No-package RxNorm source: community NCPDP generation runs without RxNorm enrichment
    /// until an rxnorm package ships. Seeded runs never consult RxNorm at all; unseeded runs
    /// take the same fallback the monorepo takes with no package installed.
    /// </summary>
    private sealed class EmptyRxNormDataSource : IRxNormDataSource
    {
        private static readonly Task<IReadOnlyList<RxNormDrugData>> EmptyList =
            Task.FromResult<IReadOnlyList<RxNormDrugData>>(Array.Empty<RxNormDrugData>());

        public Task<IReadOnlyList<RxNormDrugData>> GetDrugsAsync() => EmptyList;

        public Task<RxNormDrugData?> GetRandomDrugAsync() => Task.FromResult<RxNormDrugData?>(null);

        public Task<IReadOnlyList<RxNormDrugData>> GetDrugsByTermTypeAsync(string termType) => EmptyList;

        public Task<IReadOnlyList<RxNormDrugData>> SearchDrugsAsync(string keyword) => EmptyList;
    }
}
