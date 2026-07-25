// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Resolves the dedicated <see cref="IFHIRResourceBuilder"/> for a given
/// resource type, or returns null so callers can fall back to the generic
/// <see cref="IFHIRResourceFactory"/> path.
///
/// Registered as a scoped singleton collection — adding a new builder is purely additive
/// (drop an <see cref="IFHIRResourceBuilder"/> in DI, no edits here).
/// </summary>
public class FHIRResourceBuilderRegistry
{
    private readonly IEnumerable<IFHIRResourceBuilder> _builders;

    public FHIRResourceBuilderRegistry(IEnumerable<IFHIRResourceBuilder> builders)
    {
        _builders = builders ?? throw new ArgumentNullException(nameof(builders));
    }

    /// <summary>
    /// Highest-priority builder whose <see cref="IFHIRResourceBuilder.ResourceType"/>
    /// matches the requested type (case-insensitive), or null if no dedicated
    /// builder is registered.
    /// </summary>
    public IFHIRResourceBuilder? GetBuilder(string resourceType)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
            return null;

        return _builders
            .Where(b => string.Equals(b.ResourceType, resourceType, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(b => b.Priority)
            .FirstOrDefault();
    }
}
