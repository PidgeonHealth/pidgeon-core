// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Domain.Messaging.FHIR.Bundles;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders;

/// <summary>
/// Input payload for <see cref="BundleResourceBuilder"/>. Bundles aggregate
/// already-built resources rather than serializing a single domain entity,
/// so the builder takes a <see cref="BundleBuildInput"/> through
/// <see cref="FHIRBuildContext.DomainEntity"/>.
/// </summary>
internal sealed record BundleBuildInput(
    IReadOnlyList<FHIRResource> Resources,
    FHIRBundleType BundleType);

/// <summary>
/// Dedicated FHIR R4 builder for <c>Bundle</c>. Accepts a
/// <see cref="BundleBuildInput"/> carrying the already-built entries and
/// the bundle type; emits transaction/searchset/collection bundles with
/// the right per-entry envelope for each type.
/// </summary>
internal sealed class BundleResourceBuilder : IFHIRResourceBuilder
{
    public string ResourceType => "Bundle";
    public int Priority => 0;

    public Task<Result<string>> BuildAsync(FHIRBuildContext context, CancellationToken cancellationToken = default)
    {
        if (context.DomainEntity is not BundleBuildInput input)
        {
            return Task.FromResult(Result<string>.Failure(
                $"BundleResourceBuilder expected BundleBuildInput, got "
                + $"{context.DomainEntity?.GetType().FullName ?? "null"}"));
        }

        try
        {
            var bundleId = FHIRBuilderSupport.GenerateDeterministicId("bundle", context.Key);
            var entries = new List<object>();

            foreach (var resource in input.Resources)
            {
                var entry = new Dictionary<string, object?>
                {
                    ["fullUrl"] = $"urn:uuid:{resource.Id}"
                };

                try
                {
                    entry["resource"] = JsonSerializer.Deserialize<object>(resource.JsonContent);
                }
                catch
                {
                    entry["resource"] = resource.JsonContent;
                }

                if (input.BundleType == FHIRBundleType.Transaction)
                {
                    entry["request"] = new
                    {
                        method = "POST",
                        url = resource.ResourceType
                    };
                }
                else if (input.BundleType == FHIRBundleType.Searchset)
                {
                    entry["search"] = new { mode = "match" };
                }

                entries.Add(entry);
            }

            var fhirBundle = new Dictionary<string, object?>
            {
                ["resourceType"] = "Bundle",
                ["id"] = bundleId,
                ["type"] = input.BundleType.ToString().ToLowerInvariant(),
                ["timestamp"] = GenerationDeterminism.CreateClock(context.Options).ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["entry"] = entries
            };

            if (input.BundleType == FHIRBundleType.Searchset)
            {
                fhirBundle["total"] = input.Resources.Count;
                fhirBundle["link"] = new object[]
                {
                    new { relation = "self", url = "http://hospital.example.org/fhir/Patient" }
                };
            }

            context.References.Register("Bundle", bundleId);
            return Task.FromResult(Result<string>.Success(
                JsonSerializer.Serialize(fhirBundle, FHIRBuilderSupport.JsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<string>.Failure($"Failed to generate FHIR Bundle: {ex.Message}"));
        }
    }
}
