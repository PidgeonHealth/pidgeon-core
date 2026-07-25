// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Application.Services.Reference;
using Pidgeon.Core.Infrastructure.Reference;
using Pidgeon.Core.Infrastructure.Reference.HL7;

namespace Pidgeon.Core.Extensions;

public static class CommunityReferenceRegistrationExtensions
{
    // Same seven versions the private monorepo lane registers - the reference
    // surface must not silently narrow between compositions.
    private static readonly (string Version, string StandardId, string StandardName)[] Hl7ReferenceVersions =
    [
        ("2.3", "hl7v23", "HL7 v2.3"),
        ("2.4", "hl7v24", "HL7 v2.4"),
        ("2.5", "hl7v25", "HL7 v2.5"),
        ("2.5.1", "hl7v251", "HL7 v2.5.1"),
        ("2.6", "hl7v26", "HL7 v2.6"),
        ("2.7", "hl7v27", "HL7 v2.7"),
        ("2.8", "hl7v28", "HL7 v2.8"),
    ];

    /// <summary>
    /// Registers the HL7 reference/lookup capability over the package-neutral data
    /// seam: <see cref="IStandardReferenceService"/> dispatching to one
    /// <see cref="JsonHL7ReferencePlugin"/> per HL7 version, each reading segment /
    /// data-type / table / trigger-event JSON through the registered
    /// <see cref="IDataResourceResolver"/> at portable paths
    /// (<c>standards/hl7/v23</c> …). All Singleton - loaders are stateless, the
    /// plugin is lock-guarded, the service is stateless.
    /// </summary>
    /// <remarks>
    /// Prerequisite: the caller must register an <see cref="IDataResourceResolver"/>
    /// (e.g. via <c>AddPidgeonBaselineData()</c>) before invoking this. Absent one,
    /// lookups degrade to honest not-found results - never fabricated definitions.
    /// The seven per-version plugins register with plain <c>AddSingleton</c>
    /// factories, NOT <c>TryAddEnumerable</c>: they share one concrete type
    /// differing only by version config, and TryAddEnumerable dedupes by
    /// implementation type, which would silently collapse them to one version.
    /// </remarks>
    public static IServiceCollection AddPidgeonHl7Reference(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddMemoryCache();
        services.TryAddSingleton<HL7ReferenceJsonLoader>();
        services.TryAddSingleton<DataTypeLoader>();
        services.TryAddSingleton<SegmentDefinitionLoader>();
        services.TryAddSingleton<TableLoader>();
        services.TryAddSingleton<TriggerEventLoader>();

        foreach (var (version, standardId, standardName) in Hl7ReferenceVersions)
        {
            var config = new HL7VersionConfig(version, standardId, standardName, standardId);
            services.AddSingleton<IStandardReferencePlugin>(sp => new JsonHL7ReferencePlugin(
                config,
                sp.GetRequiredService<ILogger<JsonHL7ReferencePlugin>>(),
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<IDataResourceResolver>(),
                sp.GetRequiredService<HL7ReferenceJsonLoader>(),
                sp.GetRequiredService<SegmentDefinitionLoader>(),
                sp.GetRequiredService<DataTypeLoader>(),
                sp.GetRequiredService<TableLoader>(),
                sp.GetRequiredService<TriggerEventLoader>()));
        }

        services.TryAddSingleton<IStandardReferenceService, StandardReferenceService>();

        return services;
    }
}
