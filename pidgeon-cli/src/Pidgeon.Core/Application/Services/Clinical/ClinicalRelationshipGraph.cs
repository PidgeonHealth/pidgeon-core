// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Clinical;
using Pidgeon.Core.Domain.Clinical;

namespace Pidgeon.Core.Application.Services.Clinical;

/// <summary>
/// Single source of truth for clinical relationships across terminology systems.
/// Merges all registered IClinicalRelationshipSource implementations by priority,
/// with lower priority numbers taking precedence (curated=0, UMLS=10, AI=100).
/// </summary>
public class ClinicalRelationshipGraph : IClinicalRelationshipGraph
{
    private readonly IReadOnlyList<IClinicalRelationshipSource> _sources;
    private readonly ILogger<ClinicalRelationshipGraph> _logger;

    public ClinicalRelationshipGraph(
        IEnumerable<IClinicalRelationshipSource> sources,
        ILogger<ClinicalRelationshipGraph> logger)
    {
        _sources = sources.OrderBy(s => s.Priority).ToList().AsReadOnly();
        _logger = logger;
    }

    public Task<Result<ClinicalRelationshipBundle>> ResolveAsync(string icd10Code)
    {
        var condition = new TerminologyConcept("ICD-10", icd10Code, icd10Code);
        return ResolveAsync(condition);
    }

    public async Task<Result<ClinicalRelationshipBundle>> ResolveAsync(TerminologyConcept condition)
    {
        try
        {
            var allRelationships = await GetMergedRelationshipsAsync(condition.System, condition.Code);
            if (allRelationships.IsFailure)
                return Result<ClinicalRelationshipBundle>.Failure(allRelationships.Error);

            var relationships = allRelationships.Value;

            var snomedMappings = relationships
                .Where(r => r.RelationshipType == "maps_to_snomed")
                .Select(r => r.Target)
                .ToList();

            var expectedLabs = relationships
                .Where(r => r.RelationshipType == "expected_lab")
                .Select(r => r.Target)
                .ToList();

            var expectedMedications = relationships
                .Where(r => r.RelationshipType == "expected_medication")
                .Select(r => r.Target)
                .ToList();

            var expectedProcedures = relationships
                .Where(r => r.RelationshipType == "expected_procedure")
                .Select(r => r.Target)
                .ToList();

            var comorbidities = relationships
                .Where(r => r.RelationshipType == "comorbidity")
                .Select(r => r.Target)
                .ToList();

            var bundle = new ClinicalRelationshipBundle
            {
                PrimaryCondition = condition,
                SnomedMappings = snomedMappings.AsReadOnly(),
                ExpectedLabs = expectedLabs.AsReadOnly(),
                ExpectedMedications = expectedMedications.AsReadOnly(),
                ExpectedProcedures = expectedProcedures.AsReadOnly(),
                Comorbidities = comorbidities.AsReadOnly()
            };

            return Result<ClinicalRelationshipBundle>.Success(bundle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve clinical bundle for {System}/{Code}",
                condition.System, condition.Code);
            return Result<ClinicalRelationshipBundle>.Failure(
                $"Failed to resolve clinical bundle: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<TerminologyRelationship>>> GetRelationshipsAsync(
        string system, string code, string? relationshipType = null)
    {
        var mergedResult = await GetMergedRelationshipsAsync(system, code);
        if (mergedResult.IsFailure)
            return mergedResult;

        var relationships = mergedResult.Value;

        if (!string.IsNullOrEmpty(relationshipType))
        {
            relationships = relationships
                .Where(r => r.RelationshipType == relationshipType)
                .ToList()
                .AsReadOnly();
        }

        return Result<IReadOnlyList<TerminologyRelationship>>.Success(relationships);
    }

    private async Task<Result<IReadOnlyList<TerminologyRelationship>>> GetMergedRelationshipsAsync(
        string system, string code)
    {
        // Merge by priority: lower priority number = higher importance.
        // For duplicate target codes within the same relationship type, the first source wins.
        var seen = new HashSet<string>(); // key: "RelType:TargetSystem:TargetCode"
        var merged = new List<TerminologyRelationship>();

        // Sources are already sorted by Priority ascending (lower = higher priority)
        foreach (var source in _sources)
        {
            try
            {
                var result = await source.GetRelationshipsAsync(system, code);
                if (result.IsFailure)
                {
                    _logger.LogWarning("Source {Source} returned failure for {System}/{Code}: {Error}",
                        source.Name, system, code, result.Error);
                    continue;
                }

                foreach (var rel in result.Value)
                {
                    var key = $"{rel.RelationshipType}:{rel.Target.System}:{rel.Target.Code}";
                    if (seen.Add(key))
                        merged.Add(rel);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Source {Source} threw exception for {System}/{Code}",
                    source.Name, system, code);
            }
        }

        return Result<IReadOnlyList<TerminologyRelationship>>.Success(merged.AsReadOnly());
    }
}
