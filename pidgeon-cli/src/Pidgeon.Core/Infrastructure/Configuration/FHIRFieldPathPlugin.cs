// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core;

namespace Pidgeon.Core.Infrastructure.Configuration;

/// <summary>
/// FHIR-specific field path resolution plugin that maps semantic paths to FHIR JSON paths.
/// Maps semantic paths like "patient.mrn" to FHIR element paths dynamically.
/// Uses user's configured FHIR version (defaults to v4).
/// </summary>
public class FHIRFieldPathPlugin : IStandardFieldPathPlugin
{
    private readonly IStandardReferenceService _referenceService;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<FHIRFieldPathPlugin> _logger;

    // Semantic path mappings - maps user-friendly terms to FHIR JSON paths
    private static readonly IReadOnlyDictionary<string, SemanticMapping> SemanticMappings =
        new Dictionary<string, SemanticMapping>(StringComparer.OrdinalIgnoreCase)
        {
            // Patient Demographics - Patient resource
            ["patient.mrn"] = new("Patient", "identifier[?(@.type.coding[0].code=='MR')].value", "Medical Record Number"),
            ["patient.lastName"] = new("Patient", "name[0].family", "Patient Last Name"),
            ["patient.firstName"] = new("Patient", "name[0].given[0]", "Patient First Name"),
            ["patient.middleName"] = new("Patient", "name[0].given[1]", "Patient Middle Name"),
            ["patient.dateOfBirth"] = new("Patient", "birthDate", "Date of Birth"),
            ["patient.sex"] = new("Patient", "gender", "Administrative Sex"),
            ["patient.address"] = new("Patient", "address[0]", "Patient Address"),
            ["patient.address.street"] = new("Patient", "address[0].line[0]", "Street Address"),
            ["patient.address.city"] = new("Patient", "address[0].city", "City"),
            ["patient.address.state"] = new("Patient", "address[0].state", "State"),
            ["patient.address.zip"] = new("Patient", "address[0].postalCode", "ZIP Code"),
            ["patient.phoneNumber"] = new("Patient", "telecom[?(@.system=='phone')].value", "Phone Number"),
            ["patient.email"] = new("Patient", "telecom[?(@.system=='email')].value", "Email Address"),

            // Encounter Information - Encounter resource
            ["encounter.class"] = new("Encounter", "class.code", "Encounter Class"),
            ["encounter.status"] = new("Encounter", "status", "Encounter Status"),
            ["encounter.type"] = new("Encounter", "type[0].coding[0].display", "Encounter Type"),
            ["encounter.location"] = new("Encounter", "location[0].location.display", "Location"),
            ["encounter.startTime"] = new("Encounter", "period.start", "Start Time"),
            ["encounter.endTime"] = new("Encounter", "period.end", "End Time"),

            // Provider Information - Practitioner resource
            ["provider.id"] = new("Practitioner", "identifier[0].value", "Provider ID"),
            ["provider.lastName"] = new("Practitioner", "name[0].family", "Provider Last Name"),
            ["provider.firstName"] = new("Practitioner", "name[0].given[0]", "Provider First Name"),

            // Observation patterns - Observation resource
            ["observation.value"] = new("Observation", "valueQuantity.value", "Observation Value"),
            ["observation.units"] = new("Observation", "valueQuantity.unit", "Units"),
            ["observation.status"] = new("Observation", "status", "Observation Status"),
            ["observation.code"] = new("Observation", "code.coding[0].code", "Observation Code"),

            // Medication patterns - MedicationRequest resource
            ["medication.name"] = new("MedicationRequest", "medicationCodeableConcept.coding[0].display", "Medication Name"),
            ["medication.dosage"] = new("MedicationRequest", "dosageInstruction[0].doseAndRate[0].doseQuantity.value", "Dosage"),
            ["medication.frequency"] = new("MedicationRequest", "dosageInstruction[0].timing.repeat.frequency", "Frequency"),
            ["medication.status"] = new("MedicationRequest", "status", "Medication Status"),

            // Week 4.1: High-ROI semantic paths for 85% coverage
            // Encounter workflow - Encounter resource extensions
            ["encounter.location"] = new("Encounter", "location[0].location.display", "Location"),
            ["encounter.admissionDate"] = new("Encounter", "period.start", "Admission Date"),
            ["encounter.class"] = new("Encounter", "class.code", "Encounter Class"),
            ["encounter.room"] = new("Encounter", "location[0].physicalType.coding[0].display", "Room"),
            ["encounter.bed"] = new("Encounter", "location[0].bed", "Bed"),
            ["encounter.facility"] = new("Encounter", "location[0].location.display", "Facility"),

            // Enhanced provider information - Practitioner resource
            ["provider.npi"] = new("Practitioner", "identifier[?(@.system=='http://hl7.org/fhir/sid/us-npi')].value", "NPI"),
            ["provider.specialty"] = new("Practitioner", "qualification[0].code.coding[0].display", "Specialty"),
            ["provider.department"] = new("Practitioner", "qualification[0].issuer.display", "Department"),

            // Patient demographics extensions - Patient resource
            ["patient.ethnicity"] = new("Patient", "extension[?(@.url=='http://hl7.org/fhir/us/core/StructureDefinition/us-core-ethnicity')].valueCoding.display", "Ethnicity"),
            ["patient.language"] = new("Patient", "communication[0].language.coding[0].code", "Language"),
            ["patient.religion"] = new("Patient", "extension[?(@.url=='http://hl7.org/fhir/StructureDefinition/patient-religion')].valueCodeableConcept.coding[0].display", "Religion"),
            ["patient.maritalStatus"] = new("Patient", "maritalStatus.coding[0].code", "Marital Status"),

            // Enhanced observation patterns - Observation resource
            ["observation.dateTime"] = new("Observation", "effectiveDateTime", "Date/Time of Observation"),
            ["observation.performer"] = new("Observation", "performer[0].display", "Performer"),
            ["observation.method"] = new("Observation", "method.coding[0].display", "Method"),

            // Insurance information - Coverage resource
            ["insurance.planName"] = new("Coverage", "type.text", "Plan Name"),
            ["insurance.groupNumber"] = new("Coverage", "subscriberId", "Group Number"),
            ["insurance.policyNumber"] = new("Coverage", "identifier[0].value", "Policy Number")
        };

    public string Standard => "FHIR"; // Family standard, not version-specific

    public FHIRFieldPathPlugin(
        IStandardReferenceService referenceService,
        IConfigurationService configurationService,
        ILogger<FHIRFieldPathPlugin> logger)
    {
        _referenceService = referenceService;
        _configurationService = configurationService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the user's configured FHIR standard version (e.g., "fhirv4", "fhirv5").
    /// Falls back to "fhirv4" if not configured.
    /// </summary>
    private async Task<string> GetConfiguredFHIRStandardAsync()
    {
        try
        {
            var versionResult = await _configurationService.GetDefaultStandardVersionAsync("FHIR");
            if (versionResult.IsSuccess)
            {
                var version = versionResult.Value;
                // Convert "v4" to "fhirv4" format for data lookup
                if (version.StartsWith("v"))
                {
                    return $"fhir{version}";
                }
                return $"fhir{version}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get configured FHIR standard, using default fhirv4");
        }

        return "fhirv4"; // Safe fallback
    }

    public async Task<Result<string>> ResolvePathAsync(string semanticPath, string messageType)
    {
        if (!SemanticMappings.TryGetValue(semanticPath, out var mapping))
        {
            return Result<string>.Failure($"Semantic path '{semanticPath}' not recognized for FHIR resources");
        }

        // Validate that the resource type is supported and matches the mapping
        var resourceValidation = await ValidateResourceTypeAsync(mapping.ResourceType, messageType);
        if (resourceValidation.IsFailure)
        {
            return Result<string>.Failure(resourceValidation.Error);
        }

        _logger.LogDebug("Resolved FHIR path {SemanticPath} → {JsonPath} for {ResourceType}",
            semanticPath, mapping.JsonPath, messageType);

        return Result<string>.Success(mapping.JsonPath);
    }

    public async Task<Result<bool>> ValidatePathAsync(string semanticPath, string messageType)
    {
        var resolveResult = await ResolvePathAsync(semanticPath, messageType);
        return Result<bool>.Success(resolveResult.IsSuccess);
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> GetAvailablePathsAsync(string messageType)
    {
        var availablePaths = new Dictionary<string, string>();

        foreach (var kvp in SemanticMappings)
        {
            var validation = await ValidateResourceTypeAsync(kvp.Value.ResourceType, messageType);
            if (validation.IsSuccess)
            {
                availablePaths[kvp.Key] = $"{kvp.Value.Description} ({kvp.Value.JsonPath})";
            }
        }

        return Result<IReadOnlyDictionary<string, string>>.Success(availablePaths);
    }

    public async Task<Result<FieldValidationResult>> ValidateValueAsync(
        string semanticPath,
        string value,
        string messageType)
    {
        if (!SemanticMappings.TryGetValue(semanticPath, out var mapping))
        {
            return Result<FieldValidationResult>.Success(new FieldValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Unknown semantic path: {semanticPath}"
            });
        }

        var suggestions = new List<string>();
        var isValid = true;

        // Basic FHIR validation rules
        switch (mapping.JsonPath)
        {
            case var path when path.Contains("gender"):
                if (!IsValidFHIRGender(value))
                {
                    isValid = false;
                    suggestions.Add("FHIR gender must be: male, female, other, or unknown");
                }
                break;

            case var path when path.Contains("birthDate"):
                if (!IsValidFHIRDate(value))
                {
                    isValid = false;
                    suggestions.Add("FHIR date format: YYYY-MM-DD (e.g., 1990-01-15)");
                }
                break;

            case var path when path.Contains("status"):
                // FHIR has specific status codes per resource - would need resource-specific validation
                _logger.LogDebug("Status field validation not yet implemented for {Path}", path);
                break;
        }

        // TODO: Implement FHIR resource schema validation using FHIR specification
        await Task.Yield();

        return Result<FieldValidationResult>.Success(new FieldValidationResult
        {
            IsValid = isValid,
            ErrorMessage = isValid ? null : $"Invalid value for {semanticPath}",
            Suggestions = suggestions,
            FieldType = InferFHIRDataType(mapping.JsonPath),
            StandardFieldPath = mapping.JsonPath
        });
    }

    private Task<Result> ValidateResourceTypeAsync(string resourceType, string messageType)
    {
        try
        {
            // FHIR resources typically match the message type directly
            var isValidResource = string.Equals(resourceType, messageType, StringComparison.OrdinalIgnoreCase);

            if (!isValidResource)
            {
                return Task.FromResult(Result.Failure($"Semantic path is for '{resourceType}' resource but message type is '{messageType}'"));
            }

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating resource type {ResourceType} for message {MessageType}",
                resourceType, messageType);
            return Task.FromResult(Result.Success()); // Permissive fallback on error
        }
    }

    private static bool IsValidFHIRGender(string value)
    {
        var validGenders = new[] { "male", "female", "other", "unknown" };
        return validGenders.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsValidFHIRDate(string value)
    {
        // FHIR date format: YYYY-MM-DD
        return DateTime.TryParseExact(value, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _);
    }

    private static string InferFHIRDataType(string jsonPath)
    {
        return jsonPath switch
        {
            var path when path.Contains("birthDate") => "date",
            var path when path.Contains("gender") => "code",
            var path when path.Contains("status") => "code",
            var path when path.Contains("value") && path.Contains("Quantity") => "Quantity",
            var path when path.Contains("identifier") => "Identifier",
            var path when path.Contains("coding") => "Coding",
            var path when path.Contains("given") || path.Contains("family") => "string",
            var path when path.Contains("city") || path.Contains("state") => "string",
            var path when path.Contains("line") || path.Contains("postalCode") => "string",
            _ => "string"
        };
    }

    private record SemanticMapping(
        string ResourceType,
        string JsonPath,
        string Description);
}