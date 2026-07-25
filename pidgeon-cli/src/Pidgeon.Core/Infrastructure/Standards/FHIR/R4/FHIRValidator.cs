// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4;

/// <summary>
/// Validates FHIR R4 resources: a curated required-field floor unioned with oracle-driven structural
/// checks (cardinality/type/binding/fixed-pattern from the base StructureDefinition via
/// <see cref="BaseStructureDefinitionValidator"/>); profile conformance delegates to <see cref="IProfileValidator"/>.
/// </summary>
public class FHIRValidator : IFHIRValidator
{
    private readonly IProfileValidator? _profileValidator;
    private readonly IStructureDefinitionLoader? _loader;
    private readonly BaseStructureDefinitionValidator? _baseStructuralValidator;

    public FHIRValidator(
        IProfileValidator? profileValidator = null,
        IStructureDefinitionLoader? loader = null,
        BaseStructureDefinitionValidator? baseStructuralValidator = null)
    {
        _profileValidator = profileValidator;
        _loader = loader;
        _baseStructuralValidator = baseStructuralValidator;
    }

    /// <summary>
    /// Required fields per resource type — a curated floor deliberately stricter than base R4
    /// (e.g. Patient.name), unioned with the oracle-driven structural checks.
    /// </summary>
    private static readonly Dictionary<string, string[]> _requiredFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Patient"] = new[] { "resourceType", "name" },
        ["Encounter"] = new[] { "resourceType", "status", "class" },
        ["Observation"] = new[] { "resourceType", "status", "code" },
        ["Condition"] = new[] { "resourceType", "code", "subject" },
        ["MedicationRequest"] = new[] { "resourceType", "status", "intent", "subject" },
        ["Procedure"] = new[] { "resourceType", "status", "code", "subject" },
        ["DiagnosticReport"] = new[] { "resourceType", "status", "code" },
        ["AllergyIntolerance"] = new[] { "resourceType", "patient" },
        ["Organization"] = new[] { "resourceType" },
        ["Location"] = new[] { "resourceType" },
        ["Coverage"] = new[] { "resourceType", "status", "beneficiary", "payor" },
        ["Immunization"] = new[] { "resourceType", "status", "vaccineCode", "patient" },
        ["MedicationDispense"] = new[] { "resourceType", "status" },
        ["ServiceRequest"] = new[] { "resourceType", "status", "intent", "subject" },
        ["DocumentReference"] = new[] { "resourceType", "status", "content" },
        ["CarePlan"] = new[] { "resourceType", "status", "intent", "subject" },
        ["PractitionerRole"] = new[] { "resourceType" },
        ["RelatedPerson"] = new[] { "resourceType", "patient" },
        ["Claim"] = new[] { "resourceType", "status", "type", "use", "patient", "provider" },
        ["Practitioner"] = new[] { "resourceType" },
        ["Medication"] = new[] { "resourceType", "code" },
        ["Bundle"] = new[] { "resourceType", "type" }
    };

    public async Task<Result<FHIRValidationResult>> ValidateResourceAsync(string resourceJson, string resourceType)
    {
        var errors = new List<FHIRValidationError>();
        var warnings = new List<FHIRValidationError>();
        try
        {
            using var doc = JsonDocument.Parse(resourceJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("resourceType", out _))
            {
                errors.Add(new FHIRValidationError
                {
                    Code = "REQUIRED_FIELD_MISSING",
                    Message = "Missing required field: resourceType",
                    Path = "resourceType"
                });
                return Result<FHIRValidationResult>.Success(new FHIRValidationResult
                {
                    IsValid = false,
                    Errors = errors,
                    Warnings = warnings
                });
            }

            // Required-field floor for this resource type.
            if (_requiredFields.TryGetValue(resourceType, out var requiredFields))
            {
                foreach (var field in requiredFields)
                {
                    if (!root.TryGetProperty(field, out _))
                    {
                        errors.Add(new FHIRValidationError
                        {
                            Code = "REQUIRED_FIELD_MISSING",
                            Message = $"Missing required field: {field}",
                            Path = $"{resourceType}.{field}"
                        });
                    }
                }
            }

            if (!root.TryGetProperty("id", out _))
            {
                warnings.Add(new FHIRValidationError
                {
                    Code = "MISSING_ID",
                    Message = "Resource has no id field",
                    Path = $"{resourceType}.id",
                    Severity = FHIRValidationSeverity.Warning
                });
            }

            // Oracle-driven structural validation: union the base SD's cardinality/type/binding/
            // fixed-pattern constraints onto the floor, suppressing the SD's restatement of
            // fields already reported missing so each gap surfaces once.
            if (_baseStructuralValidator is not null)
            {
                var reported = new HashSet<string>(
                    errors.Where(e => e.Code == "REQUIRED_FIELD_MISSING").Select(e => e.Path),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var finding in await _baseStructuralValidator
                    .ValidateAsync(resourceJson, resourceType, reported).ConfigureAwait(false))
                {
                    if (finding.Severity == FHIRValidationSeverity.Error) errors.Add(finding);
                    else warnings.Add(finding);
                }
            }
        }
        catch (JsonException ex)
        {
            errors.Add(new FHIRValidationError
            {
                Code = "INVALID_JSON",
                Message = $"Invalid JSON: {ex.Message}",
                Path = ""
            });
        }

        return Result<FHIRValidationResult>.Success(new FHIRValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        });
    }

    public async Task<Result<FHIRValidationResult>> ValidateBundleAsync(string bundleJson)
    {
        await Task.Yield();
        var errors = new List<FHIRValidationError>();
        var warnings = new List<FHIRValidationError>();
        try
        {
            using var doc = JsonDocument.Parse(bundleJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("resourceType", out var rtElement) || rtElement.GetString() != "Bundle")
            {
                errors.Add(new FHIRValidationError
                {
                    Code = "INVALID_RESOURCE_TYPE",
                    Message = "Not a valid Bundle resource",
                    Path = "Bundle.resourceType"
                });
                return Result<FHIRValidationResult>.Success(new FHIRValidationResult
                {
                    IsValid = false,
                    Errors = errors,
                    Warnings = warnings
                });
            }

            if (!root.TryGetProperty("type", out _))
            {
                errors.Add(new FHIRValidationError
                {
                    Code = "REQUIRED_FIELD_MISSING",
                    Message = "Bundle missing required field: type",
                    Path = "Bundle.type"
                });
            }

            var resourceIds = new HashSet<string>();
            var references = new List<(string Reference, string Path)>();

            if (root.TryGetProperty("entry", out var entries) && entries.ValueKind == JsonValueKind.Array)
            {
                int entryIndex = 0;
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.TryGetProperty("resource", out var resource))
                    {
                        string? resType = null;
                        string? resId = null;
                        if (resource.TryGetProperty("resourceType", out var resTypeElement))
                            resType = resTypeElement.GetString();
                        if (resource.TryGetProperty("id", out var idElement))
                            resId = idElement.GetString();

                        if (resType != null && resId != null)
                        {
                            resourceIds.Add(resId);
                            resourceIds.Add($"{resType}/{resId}");
                        }

                        // Validate each entry resource (carries the SD structural checks through too).
                        if (resType != null)
                        {
                            var resourceJson = resource.GetRawText();
                            var resourceResult = await ValidateResourceAsync(resourceJson, resType);
                            if (resourceResult.IsSuccess && !resourceResult.Value.IsValid)
                            {
                                foreach (var err in resourceResult.Value.Errors)
                                {
                                    errors.Add(err with
                                    {
                                        Path = $"Bundle.entry[{entryIndex}].resource.{err.Path}"
                                    });
                                }
                            }
                        }

                        CollectReferences(resource, references, $"Bundle.entry[{entryIndex}].resource");
                    }

                    if (entry.TryGetProperty("fullUrl", out var fullUrlElement))
                    {
                        var fullUrl = fullUrlElement.GetString();
                        if (fullUrl != null) resourceIds.Add(fullUrl);
                    }

                    entryIndex++;
                }
            }

            // Check reference integrity
            foreach (var (reference, path) in references)
            {
                if (reference.StartsWith("http://") || reference.StartsWith("https://") || reference.StartsWith("urn:"))
                    continue;

                if (!resourceIds.Contains(reference))
                {
                    var parts = reference.Split('/');
                    if (parts.Length == 2)
                    {
                        var refId = parts[1];
                        if (!resourceIds.Any(id => id.Contains(refId)))
                        {
                            errors.Add(new FHIRValidationError
                            {
                                Code = "BROKEN_REFERENCE",
                                Message = $"Reference '{reference}' does not resolve to a resource in this bundle",
                                Path = path
                            });
                        }
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            errors.Add(new FHIRValidationError
            {
                Code = "INVALID_JSON",
                Message = $"Invalid Bundle JSON: {ex.Message}",
                Path = ""
            });
        }

        return Result<FHIRValidationResult>.Success(new FHIRValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        });
    }

    public async Task<Result<FHIRConformanceResult>> ValidateWithProfileAsync(
        string resourceJson, string profileUrlOrPath)
    {
        if (_profileValidator == null)
            return Result<FHIRConformanceResult>.Failure(
                "Profile validation requires IProfileValidator to be registered");

        // "us-core" alias — detect resource type and map to the correct US Core profile URL.
        if (string.Equals(profileUrlOrPath, "us-core", StringComparison.OrdinalIgnoreCase))
        {
            var resourceType = DetectResourceType(resourceJson);
            if (string.IsNullOrEmpty(resourceType))
                return Result<FHIRConformanceResult>.Failure("Cannot detect resource type from JSON");

            var profileUrl = MapToUSCoreProfileUrl(resourceType);
            if (profileUrl != null)
                return await _profileValidator.ValidateAsync(resourceJson, profileUrl);

            // Fall back to the base profile when no US Core profile exists for this type.
            var baseUrl = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
            return await _profileValidator.ValidateAsync(resourceJson, baseUrl);
        }

        // File path
        if (profileUrlOrPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            !profileUrlOrPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (_loader != null)
            {
                var loadResult = await _loader.LoadFromFileAsync(profileUrlOrPath);
                if (loadResult.IsSuccess)
                    return await _profileValidator.ValidateAsync(resourceJson, loadResult.Value);
                // Surface the loader's specific reason (e.g. differential-only,
                // malformed) so a probe fails with a clear diagnostic rather than
                // vacuously passing or reporting a generic load failure.
                return Result<FHIRConformanceResult>.Failure(
                    $"Could not load profile from file '{profileUrlOrPath}': {loadResult.Error.Message}");
            }
            return Result<FHIRConformanceResult>.Failure($"Could not load profile from file: {profileUrlOrPath}");
        }

        // Directory path
        if (Directory.Exists(profileUrlOrPath))
        {
            if (_loader != null)
                await _loader.LoadDirectoryAsync(profileUrlOrPath);

            var resourceType = DetectResourceType(resourceJson);
            if (!string.IsNullOrEmpty(resourceType) && _loader != null)
            {
                var profiles = await _loader.LoadProfilesForResourceAsync(resourceType);
                if (profiles.IsSuccess && profiles.Value.Count > 0)
                    return await _profileValidator.ValidateAsync(resourceJson, profiles.Value[0]);
            }

            return Result<FHIRConformanceResult>.Failure("No matching profile found in directory");
        }

        // Canonical URL
        return await _profileValidator.ValidateAsync(resourceJson, profileUrlOrPath);
    }

    private static string? DetectResourceType(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("resourceType", out var rt))
                return rt.GetString();
        }
        catch { }
        return null;
    }

    private static string? MapToUSCoreProfileUrl(string resourceType) => resourceType switch
    {
        "Patient" => "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient",
        "Encounter" => "http://hl7.org/fhir/us/core/StructureDefinition/us-core-encounter",
        "Condition" => "http://hl7.org/fhir/us/core/StructureDefinition/us-core-condition-problems-health-concerns",
        "Observation" => "http://hl7.org/fhir/us/core/StructureDefinition/us-core-observation-lab",
        "MedicationRequest" => "http://hl7.org/fhir/us/core/StructureDefinition/us-core-medicationrequest",
        "DiagnosticReport" => "http://hl7.org/fhir/us/core/StructureDefinition/us-core-diagnosticreport-lab",
        "Procedure" => "http://hl7.org/fhir/us/core/StructureDefinition/us-core-procedure",
        "AllergyIntolerance" => "http://hl7.org/fhir/us/core/StructureDefinition/us-core-allergyintolerance",
        "Immunization" => "http://hl7.org/fhir/us/core/StructureDefinition/us-core-immunization",
        "DocumentReference" => "http://hl7.org/fhir/us/core/StructureDefinition/us-core-documentreference",
        _ => null,
    };

    private static void CollectReferences(JsonElement element, List<(string, string)> references, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("reference", out var refElement) && refElement.ValueKind == JsonValueKind.String)
            {
                var refValue = refElement.GetString();
                if (refValue != null)
                    references.Add((refValue, $"{path}.reference"));
            }

            foreach (var property in element.EnumerateObject())
            {
                CollectReferences(property.Value, references, $"{path}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var item in element.EnumerateArray())
            {
                CollectReferences(item, references, $"{path}[{index}]");
                index++;
            }
        }
    }
}
