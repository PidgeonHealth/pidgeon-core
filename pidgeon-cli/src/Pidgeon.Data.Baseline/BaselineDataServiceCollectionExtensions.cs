// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Infrastructure.Data;

namespace Pidgeon.Data.Baseline;

public static class BaselineDataServiceCollectionExtensions
{
    /// <summary>
    /// Adds the public baseline provider and the package-backed resource resolver.
    /// Registration order matters: the resolver snapshots every registered
    /// <see cref="IDataResourceProvider"/> when <see cref="IDataResourceResolver"/> is first
    /// resolved, so register all additional public or BYO providers BEFORE the first
    /// resolution; a provider registered after that point is silently invisible to the
    /// resolver for the lifetime of the container.
    /// </summary>
    public static IServiceCollection AddPidgeonBaselineData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDataResourceProvider, BaselineDataResourceProvider>());
        services.TryAddSingleton<IDataResourceResolver>(provider =>
            new CompositeDataResourceProvider(provider.GetServices<IDataResourceProvider>()));

        return services;
    }
}
