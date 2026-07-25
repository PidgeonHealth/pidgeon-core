// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Messaging.FHIR.Bundles;

/// <summary>
/// Base class for all FHIR Bundle messages.
/// Captures FHIR-specific concepts like resources, Bundle types, and references.
/// </summary>
public abstract class FHIRBundle : HealthcareMessage
{
    /// <summary>
    /// Gets the FHIR Bundle identifier.
    /// </summary>
    public FHIRIdentifier? BundleIdentifier { get; init; }

    /// <summary>
    /// Gets the type of Bundle (document, message, transaction, etc.).
    /// </summary>
    public required FHIRBundleType BundleType { get; init; }

    /// <summary>
    /// Gets all resources contained in this Bundle.
    /// </summary>
    public List<FHIRBundleEntry> Entries { get; init; } = new();

    /// <summary>
    /// Gets the total number of resources in the Bundle.
    /// </summary>
    public int? Total { get; init; }

    /// <summary>
    /// Gets links related to this Bundle (self, next, previous for paging).
    /// </summary>
    public List<FHIRBundleLink> Links { get; init; } = new();

    /// <summary>
    /// Validates FHIR Bundle structure and contained resources.
    /// </summary>
    /// <returns>A result indicating whether the FHIR Bundle is valid</returns>
    public override Result<HealthcareMessage> Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.IsSuccess)
            return baseResult;

        if (!Standard.Equals("FHIR", StringComparison.OrdinalIgnoreCase))
            return Error.Validation($"Standard must be FHIR, got: {Standard}", nameof(Standard));


        // Validate all entries
        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            if (entry.Resource != null)
            {
                var resourceValidation = entry.Resource.Validate();
                if (!resourceValidation.IsSuccess)
                    return Error.Validation($"Entry {i} resource validation failed: {resourceValidation.Error.Message}", $"Entries[{i}].Resource");
            }
        }

        return Result<HealthcareMessage>.Success(this);
    }

    /// <summary>
    /// Gets the FHIR display summary including Bundle type and resource count.
    /// </summary>
    public override string GetDisplaySummary()
    {
        var resourceCount = Entries.Count;
        var resourceSummary = resourceCount == 1 ? "1 resource" : $"{resourceCount} resources";
        return $"FHIR {BundleType} Bundle {MessageControlId} with {resourceSummary} from {SendingSystem} to {ReceivingSystem}";
    }

    /// <summary>
    /// Gets all resources of a specific type from the Bundle.
    /// </summary>
    /// <typeparam name="T">The FHIR resource type</typeparam>
    /// <returns>All resources matching the specified type</returns>
    public IEnumerable<T> GetResources<T>() where T : FHIRResource
    {
        return Entries
            .Select(e => e.Resource)
            .OfType<T>();
    }

    /// <summary>
    /// Gets the first resource of a specific type from the Bundle.
    /// </summary>
    /// <typeparam name="T">The FHIR resource type</typeparam>
    /// <returns>The first resource of the specified type, or null if not found</returns>
    public T? GetResource<T>() where T : FHIRResource
    {
        return GetResources<T>().FirstOrDefault();
    }
}

/// <summary>
/// Represents a FHIR Bundle entry containing a resource and metadata.
/// </summary>
public record FHIRBundleEntry
{
    /// <summary>
    /// Gets the unique identifier for this entry within the Bundle.
    /// </summary>
    public string? FullUrl { get; init; }

    /// <summary>
    /// Gets the FHIR resource contained in this entry.
    /// </summary>
    public FHIRResource? Resource { get; init; }

    /// <summary>
    /// Gets search metadata (score, mode) if this Bundle is a search result.
    /// </summary>
    public FHIRBundleEntrySearch? Search { get; init; }

    /// <summary>
    /// Gets request information if this is a transaction Bundle.
    /// </summary>
    public FHIRBundleEntryRequest? Request { get; init; }

    /// <summary>
    /// Gets response information if this is a transaction response Bundle.
    /// </summary>
    public FHIRBundleEntryResponse? Response { get; init; }
}

/// <summary>
/// Represents FHIR Bundle types.
/// </summary>
public enum FHIRBundleType
{
    Document,
    Message,
    Transaction,
    TransactionResponse,
    Batch,
    BatchResponse,
    History,
    Searchset,
    Collection
}

/// <summary>
/// Represents search metadata for Bundle entries.
/// </summary>
public record FHIRBundleEntrySearch
{
    public string? Mode { get; init; }
    public decimal? Score { get; init; }
}

/// <summary>
/// Represents request information for transaction Bundle entries.
/// </summary>
public record FHIRBundleEntryRequest
{
    public required string Method { get; init; }
    public required string Url { get; init; }
    public string? IfNoneMatch { get; init; }
    public string? IfModifiedSince { get; init; }
}

/// <summary>
/// Represents response information for transaction response Bundle entries.
/// </summary>
public record FHIRBundleEntryResponse
{
    public required string Status { get; init; }
    public string? Location { get; init; }
    public string? Etag { get; init; }
    public DateTime? LastModified { get; init; }
}

/// <summary>
/// Represents Bundle links for pagination.
/// </summary>
public record FHIRBundleLink
{
    public required string Relation { get; init; }
    public required string Url { get; init; }
}

/// <summary>
/// Base class for all FHIR resources.
/// </summary>
public abstract record FHIRResource
{
    /// <summary>
    /// Gets the logical ID of the resource.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Gets metadata about the resource.
    /// </summary>
    public FHIRMeta? Meta { get; init; }

    /// <summary>
    /// Gets the implicit rules that constrain this resource.
    /// </summary>
    public string? ImplicitRules { get; init; }

    /// <summary>
    /// Gets the language of the resource content.
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Gets the resource type name.
    /// </summary>
    public abstract string ResourceType { get; }

    /// <summary>
    /// Validates the FHIR resource structure and business rules.
    /// Derived resources should override to add resource-specific validation.
    /// </summary>
    /// <returns>A result indicating whether the resource is valid</returns>
    public virtual Result<FHIRResource> Validate()
    {
        if (string.IsNullOrWhiteSpace(ResourceType))
            return Error.Validation("Resource type is required", nameof(ResourceType));

        return Result<FHIRResource>.Success(this);
    }
}

/// <summary>
/// Represents FHIR resource metadata.
/// </summary>
public record FHIRMeta
{
    public string? VersionId { get; init; }
    public DateTime? LastUpdated { get; init; }
    public string? Source { get; init; }
    public List<string> Profile { get; init; } = new();
    public List<FHIRCoding> Security { get; init; } = new();
    public List<FHIRCoding> Tag { get; init; } = new();
}

/// <summary>
/// Represents a FHIR coding (code from a code system).
/// </summary>
public record FHIRCoding
{
    public string? System { get; init; }
    public string? Version { get; init; }
    public string? Code { get; init; }
    public string? Display { get; init; }
    public bool? UserSelected { get; init; }
}

/// <summary>
/// Represents a FHIR identifier.
/// </summary>
public record FHIRIdentifier
{
    public string? Use { get; init; }
    public FHIRCodeableConcept? Type { get; init; }
    public string? System { get; init; }
    public string? Value { get; init; }
    public FHIRPeriod? Period { get; init; }
    public FHIRReference? Assigner { get; init; }
}

/// <summary>
/// Represents a FHIR CodeableConcept.
/// </summary>
public record FHIRCodeableConcept
{
    public List<FHIRCoding> Coding { get; init; } = new();
    public string? Text { get; init; }
}

/// <summary>
/// Represents a FHIR Period.
/// </summary>
public record FHIRPeriod
{
    public DateTime? Start { get; init; }
    public DateTime? End { get; init; }
}

/// <summary>
/// Represents a FHIR Reference to another resource.
/// </summary>
public record FHIRReference
{
    public string? Reference { get; init; }
    public string? Type { get; init; }
    public FHIRIdentifier? Identifier { get; init; }
    public string? Display { get; init; }
}