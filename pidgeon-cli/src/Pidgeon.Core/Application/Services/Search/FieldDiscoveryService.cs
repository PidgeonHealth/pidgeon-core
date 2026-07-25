// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Application.Interfaces.Search;
using Pidgeon.Core.Domain.Reference.Entities;
using Pidgeon.Core.Domain.Search;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Pidgeon.Core.Application.Services.Search;

/// <summary>
/// Service for discovering and searching healthcare message fields across standards.
/// Uses existing StandardReferenceService for data access.
/// </summary>
public class FieldDiscoveryService : IFieldDiscoveryService
{
    private readonly IStandardReferenceService _referenceService;
    private readonly ILogger<FieldDiscoveryService> _logger;
    private readonly Dictionary<string, List<string>> _semanticMappings;

    public FieldDiscoveryService(
        IStandardReferenceService referenceService,
        ILogger<FieldDiscoveryService> logger)
    {
        _referenceService = referenceService;
        _logger = logger;
        _semanticMappings = new Dictionary<string, List<string>>();

        // Build semantic mappings dynamically on first use
        InitializeSemanticMappings();
    }

    private void InitializeSemanticMappings()
    {
        // These are common mappings - we could enhance this by loading from configuration
        // or by analyzing field descriptions from the StandardReferenceService
        _semanticMappings["patient.mrn"] = ["PID.3", "Patient.identifier", "PatientId"];
        _semanticMappings["patient.name"] = ["PID.5", "Patient.name", "PatientName"];
        _semanticMappings["patient.dob"] = ["PID.7", "Patient.birthDate", "DateOfBirth"];
        _semanticMappings["patient.gender"] = ["PID.8", "Patient.gender", "Gender"];
        _semanticMappings["patient.address"] = ["PID.11", "Patient.address", "PatientAddress"];
        _semanticMappings["patient.phone"] = ["PID.13", "Patient.telecom", "PhoneNumber"];
        _semanticMappings["provider.npi"] = ["PRD.7", "Practitioner.identifier", "ProviderNPI"];
        _semanticMappings["provider.name"] = ["PRD.2", "Practitioner.name", "ProviderName"];
        _semanticMappings["encounter.id"] = ["PV1.19", "Encounter.identifier", "VisitNumber"];
        _semanticMappings["encounter.location"] = ["PV1.3", "Encounter.location", "Location"];
        _semanticMappings["medication.code"] = ["RXE.2", "MedicationRequest.medicationCodeableConcept", "DrugCode"];
        _semanticMappings["medication.dose"] = ["RXE.3", "MedicationRequest.dosageInstruction.dose", "DoseAmount"];
        _semanticMappings["allergy.type"] = ["AL1.3", "AllergyIntolerance.type", "AllergyType"];
        _semanticMappings["diagnosis.code"] = ["DG1.3", "Condition.code", "DiagnosisCode"];
        _semanticMappings["observation.value"] = ["OBX.5", "Observation.value", "ResultValue"];
    }

    public async Task<FieldSearchResult> FindBySemanticPathAsync(string semanticPath, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new FieldSearchResult();

        try
        {
            var normalizedPath = semanticPath.ToLowerInvariant().Trim();

            // Check semantic mappings first
            if (_semanticMappings.TryGetValue(normalizedPath, out var mappedPaths))
            {
                foreach (var path in mappedPaths)
                {
                    var lookupResult = await _referenceService.LookupAsync(path, cancellationToken);
                    if (lookupResult.IsSuccess)
                    {
                        result.Locations.Add(ConvertToFieldLocation(lookupResult.Value));
                    }
                }
            }

            // If no direct mapping, try searching for the semantic term
            if (result.Locations.Count == 0)
            {
                var searchTerm = ExtractSearchTerm(semanticPath);
                var searchResult = await _referenceService.SearchAsync(searchTerm, cancellationToken);

                if (searchResult.IsSuccess)
                {
                    result.Locations.AddRange(searchResult.Value.Select(ConvertToFieldLocation));
                }
            }

            // Add suggestions if no results
            if (result.Locations.Count == 0)
            {
                result.Suggestions = GenerateSuggestions(semanticPath);
            }

            result.Metrics = new SearchMetrics
            {
                SearchTime = stopwatch.Elapsed,
                StandardsSearched = 3, // HL7, FHIR, NCPDP
                FieldsExamined = result.Locations.Count,
                ResultsFound = result.Locations.Count,
                FromCache = false
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding fields by semantic path: {Path}", semanticPath);
            return result;
        }
    }

    public async Task<FieldSearchResult> FindByPatternAsync(string pattern, PatternType type, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new FieldSearchResult();

        try
        {
            // Convert pattern to regex based on type
            var regex = type switch
            {
                PatternType.Wildcard => ConvertWildcardToRegex(pattern),
                PatternType.Regex => new Regex(pattern, RegexOptions.IgnoreCase),
                PatternType.Fuzzy => throw new NotSupportedException("Fuzzy matching not yet implemented"),
                _ => new Regex(Regex.Escape(pattern), RegexOptions.IgnoreCase)
            };

            // Search all standards
            var standards = await _referenceService.GetSupportedStandardsAsync(cancellationToken);
            if (standards.IsSuccess)
            {
                foreach (var standard in standards.Value.Keys)
                {
                    var topLevelResult = await _referenceService.ListTopLevelElementsAsync(standard, cancellationToken);
                    if (topLevelResult.IsSuccess)
                    {
                        foreach (var element in topLevelResult.Value)
                        {
                            if (regex.IsMatch(element.Path) || regex.IsMatch(element.Name))
                            {
                                result.Locations.Add(ConvertToFieldLocation(element));
                            }

                            // Check child elements
                            var childrenResult = await _referenceService.ListChildrenAsync(element.Path, cancellationToken);
                            if (childrenResult.IsSuccess)
                            {
                                foreach (var child in childrenResult.Value)
                                {
                                    if (regex.IsMatch(child.Path) || regex.IsMatch(child.Name))
                                    {
                                        result.Locations.Add(ConvertToFieldLocation(child));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            result.Metrics = new SearchMetrics
            {
                SearchTime = stopwatch.Elapsed,
                StandardsSearched = standards.Value?.Count ?? 0,
                FieldsExamined = result.Locations.Count * 2, // Estimate
                ResultsFound = result.Locations.Count,
                FromCache = false
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding fields by pattern: {Pattern}", pattern);
            return result;
        }
    }

    public async Task<CrossStandardMapping> MapAcrossStandardsAsync(string fieldPath, CancellationToken cancellationToken = default)
    {
        var mapping = new CrossStandardMapping();

        try
        {
            // Look up the source field
            var sourceResult = await _referenceService.LookupAsync(fieldPath, cancellationToken);
            if (sourceResult.IsSuccess)
            {
                mapping.SourceField = ConvertToFieldLocation(sourceResult.Value);

                // Check for cross-references in the source element
                if (sourceResult.Value.CrossReferences.Any())
                {
                    foreach (var crossRef in sourceResult.Value.CrossReferences)
                    {
                        var targetResult = await _referenceService.LookupAsync(crossRef.Path, crossRef.Standard, cancellationToken);
                        if (targetResult.IsSuccess)
                        {
                            mapping.TargetFields.Add(new MappedField
                            {
                                Standard = crossRef.Standard,
                                Path = crossRef.Path,
                                Description = targetResult.Value.Description,
                                MessageType = ExtractMessageType(targetResult.Value),
                                DataType = targetResult.Value.DataType,
                                Usage = targetResult.Value.Usage,
                                Examples = targetResult.Value.Examples,
                                Type = ParseMappingType(crossRef.MappingType),
                                Confidence = CalculateConfidence(crossRef.MappingType),
                                Notes = crossRef.Notes
                            });
                        }
                    }
                }
                else
                {
                    // Try to find mappings using semantic search
                    await AddSemanticMappingsAsync(mapping, fieldPath, cancellationToken);
                }

                mapping.Quality = DetermineMappingQuality(mapping.TargetFields);
            }

            return mapping;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mapping field across standards: {Path}", fieldPath);
            return mapping;
        }
    }

    public async Task<FieldSearchResult> FindBySemanticAsync(string query, string? context = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new FieldSearchResult();

        try
        {
            // Search across all standards
            var searchResult = await _referenceService.SearchAsync(query, cancellationToken);

            if (searchResult.IsSuccess)
            {
                // Filter by context if provided
                var filteredElements = context != null
                    ? searchResult.Value.Where(e => IsRelevantToContext(e, context))
                    : searchResult.Value;

                result.Locations.AddRange(filteredElements.Select(ConvertToFieldLocation));

                // Group by semantic category
                var clinicalFields = result.Locations.Where(l => IsClinicalField(l.Path)).ToList();
                var demographicFields = result.Locations.Where(l => IsDemographicField(l.Path)).ToList();
                var administrativeFields = result.Locations.Where(l => IsAdministrativeField(l.Path)).ToList();

                // Reorder by relevance
                result.Locations.Clear();
                result.Locations.AddRange(clinicalFields);
                result.Locations.AddRange(demographicFields);
                result.Locations.AddRange(administrativeFields);
            }

            result.Metrics = new SearchMetrics
            {
                SearchTime = stopwatch.Elapsed,
                StandardsSearched = 3,
                FieldsExamined = searchResult.Value?.Count ?? 0,
                ResultsFound = result.Locations.Count,
                FromCache = false
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding fields by semantic query: {Query}", query);
            return result;
        }
    }

    private FieldLocation ConvertToFieldLocation(StandardElement element)
    {
        return new FieldLocation
        {
            Standard = element.Standard,
            Path = element.Path,
            Description = element.Description,
            MessageType = ExtractMessageType(element),
            DataType = element.DataType,
            Usage = element.Usage,
            Examples = element.Examples
        };
    }

    private string ExtractMessageType(StandardElement element)
    {
        // For HL7, extract segment name as message type indicator
        if (element.Standard.StartsWith("hl7", StringComparison.OrdinalIgnoreCase))
        {
            var parts = element.Path.Split('.');
            if (parts.Length > 0)
            {
                return parts[0] + " segment"; // e.g., "PID segment"
            }
        }
        // For FHIR, use resource type
        else if (element.Standard.StartsWith("fhir", StringComparison.OrdinalIgnoreCase))
        {
            var parts = element.Path.Split('.');
            if (parts.Length > 0)
            {
                return parts[0] + " resource"; // e.g., "Patient resource"
            }
        }

        return "Message";
    }

    private Regex ConvertWildcardToRegex(string pattern)
    {
        var regexPattern = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return new Regex($"^{regexPattern}$", RegexOptions.IgnoreCase);
    }

    private string ExtractSearchTerm(string semanticPath)
    {
        // Extract the meaningful part from semantic paths like "patient.mrn"
        var parts = semanticPath.Split('.');
        return parts.Length > 1 ? parts[1] : semanticPath;
    }

    private List<string> GenerateSuggestions(string query)
    {
        var suggestions = new List<string>();

        // Find similar semantic mappings
        foreach (var mapping in _semanticMappings.Keys)
        {
            if (mapping.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                query.Contains(mapping, StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add(mapping);
            }
        }

        // Add common field suggestions
        if (query.Contains("patient", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.AddRange(["patient.mrn", "patient.name", "patient.dob"]);
        }
        if (query.Contains("medication", StringComparison.OrdinalIgnoreCase))
        {
            suggestions.AddRange(["medication.code", "medication.dose"]);
        }

        return suggestions.Distinct().Take(5).ToList();
    }

    private async Task AddSemanticMappingsAsync(CrossStandardMapping mapping, string fieldPath, CancellationToken cancellationToken)
    {
        // Try to find semantic equivalents
        foreach (var semanticMapping in _semanticMappings)
        {
            if (semanticMapping.Value.Contains(fieldPath, StringComparer.OrdinalIgnoreCase))
            {
                // Found a semantic group containing this field
                foreach (var relatedPath in semanticMapping.Value.Where(p => p != fieldPath))
                {
                    var targetResult = await _referenceService.LookupAsync(relatedPath, cancellationToken);
                    if (targetResult.IsSuccess)
                    {
                        mapping.TargetFields.Add(new MappedField
                        {
                            Standard = targetResult.Value.Standard,
                            Path = relatedPath,
                            Description = targetResult.Value.Description,
                            MessageType = ExtractMessageType(targetResult.Value),
                            DataType = targetResult.Value.DataType,
                            Usage = targetResult.Value.Usage,
                            Examples = targetResult.Value.Examples,
                            Type = MappingType.Conceptual,
                            Confidence = 0.8,
                            Notes = "Semantic mapping based on healthcare concept"
                        });
                    }
                }
                break;
            }
        }
    }

    private MappingType ParseMappingType(string mappingType)
    {
        return mappingType?.ToLowerInvariant() switch
        {
            "exact" => MappingType.Direct,
            "approximate" => MappingType.Conceptual,
            "partial" => MappingType.Filtered,
            _ => MappingType.None
        };
    }

    private double CalculateConfidence(string mappingType)
    {
        return mappingType?.ToLowerInvariant() switch
        {
            "exact" => 1.0,
            "approximate" => 0.8,
            "partial" => 0.6,
            _ => 0.0
        };
    }

    private MappingQuality DetermineMappingQuality(List<MappedField> mappedFields)
    {
        if (!mappedFields.Any())
            return MappingQuality.Poor;

        var avgConfidence = mappedFields.Average(f => f.Confidence);
        return avgConfidence switch
        {
            >= 0.9 => MappingQuality.Excellent,
            >= 0.7 => MappingQuality.Good,
            >= 0.5 => MappingQuality.Fair,
            _ => MappingQuality.Poor
        };
    }

    private bool IsRelevantToContext(StandardElement element, string context)
    {
        var lowerContext = context.ToLowerInvariant();
        return element.Path.Contains(context, StringComparison.OrdinalIgnoreCase) ||
               element.Name.Contains(context, StringComparison.OrdinalIgnoreCase) ||
               element.Description.Contains(context, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsClinicalField(string path)
    {
        var clinicalSegments = new[] { "DG1", "RXE", "RXA", "RXO", "OBX", "OBR", "AL1", "PR1" };
        var clinicalResources = new[] { "Condition", "MedicationRequest", "Observation", "AllergyIntolerance", "Procedure" };

        return clinicalSegments.Any(s => path.StartsWith(s, StringComparison.OrdinalIgnoreCase)) ||
               clinicalResources.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsDemographicField(string path)
    {
        var demographicSegments = new[] { "PID", "NK1", "GT1" };
        var demographicResources = new[] { "Patient", "RelatedPerson" };

        return demographicSegments.Any(s => path.StartsWith(s, StringComparison.OrdinalIgnoreCase)) ||
               demographicResources.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAdministrativeField(string path)
    {
        var adminSegments = new[] { "PV1", "PV2", "IN1", "IN2", "MSH", "EVN" };
        var adminResources = new[] { "Encounter", "Coverage", "Account" };

        return adminSegments.Any(s => path.StartsWith(s, StringComparison.OrdinalIgnoreCase)) ||
               adminResources.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase));
    }
}