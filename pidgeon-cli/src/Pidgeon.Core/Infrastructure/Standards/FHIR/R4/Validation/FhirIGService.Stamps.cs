// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Conformance;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// IG version stamping: which IG release actually backed each validated
/// profile. Split from the load/resolve half of <see cref="FhirIGService"/>
/// so the evidence-honesty logic reads as one unit.
/// </summary>
public partial class FhirIGService
{
    public IReadOnlyList<IgVersionStamp> ResolveStampsForProfiles(IEnumerable<string> profileCanonicalUrls)
    {
        var stamps = new Dictionary<string, IgVersionStamp>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in profileCanonicalUrls)
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            // All known IGs sharing this URL's canonical base (two US Core
            // releases publish the same base). One stamp per URL: the release
            // that actually backed validation, never one stamp per known key.
            var owners = FindOwningIGs(url);
            if (owners.Count == 0)
                continue;

            // Stamp the real package version ONLY when the profile a probe
            // actually validated against came from an installed package. The
            // loader's indexed content is the source of truth — the same signal
            // CONFORM_STUB_PROFILE uses — so the stamp and the warning cannot
            // contradict each other: an installed package whose copy of this
            // profile failed to load (e.g. differential-only) falls back to the
            // embedded stub, and the stamp must say so even though the package
            // directory exists (FABLE_CONFORM_AUDIT C13). When nothing is
            // indexed for the URL, fall back to the install check. The indexed
            // StructureDefinition's own business version outranks the package
            // registry, so with both US Core releases installed the stamp names
            // the release whose snapshot graded the resource.
            var indexed = _loader.GetIndexed(url);
            string version;
            if (indexed is not null)
            {
                version = indexed.Origin == StructureDefinitionOrigin.Embedded
                    ? IgVersionStamp.EmbeddedSubsetVersion
                    : indexed.Version ?? FirstInstalledOwnerVersion(owners) ?? IgVersionStamp.EmbeddedSubsetVersion;
            }
            else
            {
                version = FirstInstalledOwnerVersion(owners) ?? IgVersionStamp.EmbeddedSubsetVersion;
            }

            // Attribute the stamp to the known key whose installed package
            // matches the backing version; fall back to the first owner.
            var chosen = owners.FirstOrDefault(o =>
                IsIGInstalled(o.IgKey)
                && _packages.FindInstalled(o.Ig.PackageName)?.Identity.Version == version);
            if (chosen == default)
                chosen = owners[0];

            stamps[chosen.IgKey] = new IgVersionStamp(chosen.IgKey, chosen.Ig.DisplayName, version);
        }

        return stamps.Values
            .OrderBy(s => s.DisplayName, StringComparer.Ordinal)
            .ThenBy(s => s.Version, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// First installed owner's package version, in known-IG listing order.
    /// Install-ness comes from the on-disk check (<see cref="IsIGInstalled"/>);
    /// <c>FindInstalled</c> alone can echo registry metadata for an absent package.
    /// </summary>
    private string? FirstInstalledOwnerVersion(IReadOnlyList<(string IgKey, KnownIG Ig)> owners)
        => owners
            .Where(o => IsIGInstalled(o.IgKey))
            .Select(o => _packages.FindInstalled(o.Ig.PackageName)?.Identity.Version)
            .FirstOrDefault(v => v is not null);
}
