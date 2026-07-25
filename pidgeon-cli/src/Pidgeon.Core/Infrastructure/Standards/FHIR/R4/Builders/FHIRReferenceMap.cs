// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Bundle-scoped registry of resource IDs so builders can emit cross-resource
/// references (e.g., <c>Observation.subject</c> → <c>Patient/{id}</c>) that
/// resolve within the same bundle.
///
/// A new map is created per bundle build; resources register their ID on
/// creation so subsequent builders can ask the map for the current Patient,
/// Encounter, Practitioner, etc.
/// </summary>
public sealed class FHIRReferenceMap
{
    private readonly Dictionary<string, string> _byType =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Record the ID of a resource just built so later resources in the same
    /// bundle can reference it.
    /// </summary>
    public void Register(string resourceType, string id)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
            throw new ArgumentException("resourceType must be non-empty", nameof(resourceType));
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id must be non-empty", nameof(id));

        _byType[resourceType] = id;
    }

    /// <summary>
    /// ID of the last-registered resource of the given type, or null if no
    /// such resource has been built in this bundle scope yet.
    /// </summary>
    public string? GetId(string resourceType) =>
        _byType.TryGetValue(resourceType, out var id) ? id : null;

    /// <summary>
    /// FHIR reference string (<c>ResourceType/id</c>) for the last-registered
    /// resource of the given type, or null if none is registered.
    /// </summary>
    public string? GetReference(string resourceType)
    {
        var id = GetId(resourceType);
        return id == null ? null : $"{resourceType}/{id}";
    }

    /// <summary>Whether a resource of the given type has been registered.</summary>
    public bool Has(string resourceType) => _byType.ContainsKey(resourceType);
}
