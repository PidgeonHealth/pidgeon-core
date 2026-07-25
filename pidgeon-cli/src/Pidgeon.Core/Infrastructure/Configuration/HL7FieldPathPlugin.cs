// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core;

namespace Pidgeon.Core.Infrastructure.Configuration;

/// <summary>
/// HL7-specific field path resolution plugin that leverages configured HL7 JSON data.
/// Maps semantic paths like "patient.mrn" to HL7 field locations dynamically.
/// Uses user's configured HL7 version (defaults to v23).
/// </summary>
public class HL7FieldPathPlugin : IStandardFieldPathPlugin
{
    private readonly IStandardReferenceService _referenceService;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<HL7FieldPathPlugin> _logger;

    // Semantic path mappings - maps user-friendly terms to segment.field patterns
    private static readonly IReadOnlyDictionary<string, SemanticMapping> SemanticMappings =
        new Dictionary<string, SemanticMapping>(StringComparer.OrdinalIgnoreCase)
        {
            // Patient Demographics - PID segment
            ["patient.mrn"] = new("PID", 3, "Medical Record Number"),
            ["patient.lastName"] = new("PID", 5, "Patient Last Name", "XPN.1"),
            ["patient.firstName"] = new("PID", 5, "Patient First Name", "XPN.2"),
            ["patient.middleName"] = new("PID", 5, "Patient Middle Name", "XPN.3"),
            ["patient.dateOfBirth"] = new("PID", 7, "Date of Birth"),
            ["patient.sex"] = new("PID", 8, "Administrative Sex"),
            ["patient.race"] = new("PID", 10, "Race"),
            ["patient.address"] = new("PID", 11, "Patient Address"),
            ["patient.address.street"] = new("PID", 11, "Street Address", "XAD.1"),
            ["patient.address.city"] = new("PID", 11, "City", "XAD.3"),
            ["patient.address.state"] = new("PID", 11, "State", "XAD.4"),
            ["patient.address.zip"] = new("PID", 11, "ZIP Code", "XAD.5"),
            ["patient.phoneNumber"] = new("PID", 13, "Phone Number - Home"),
            ["patient.accountNumber"] = new("PID", 18, "Patient Account Number"),
            ["patient.ssn"] = new("PID", 19, "SSN Number - Patient"),

            // Encounter Information - PV1 segment
            ["encounter.patientClass"] = new("PV1", 2, "Patient Class"),
            ["encounter.location"] = new("PV1", 3, "Assigned Patient Location"),
            ["encounter.attendingProvider"] = new("PV1", 7, "Attending Doctor"),
            ["encounter.admissionDate"] = new("PV1", 44, "Admit Date/Time"),
            ["encounter.dischargeDate"] = new("PV1", 45, "Discharge Date/Time"),

            // Provider Information - varies by context, but common patterns
            ["provider.id"] = new("PV1", 7, "Provider ID", "XCN.1"), // When in PV1.7
            ["provider.lastName"] = new("PV1", 7, "Provider Last Name", "XCN.2"),
            ["provider.firstName"] = new("PV1", 7, "Provider First Name", "XCN.3"),

            // Common observation patterns - OBX segment
            ["observation.value"] = new("OBX", 5, "Observation Value"),
            ["observation.units"] = new("OBX", 6, "Units"),
            ["observation.referenceRange"] = new("OBX", 7, "Reference Range"),
            ["observation.status"] = new("OBX", 11, "Observation Result Status"),

            // Allergy information - AL1 segment
            ["allergy.type"] = new("AL1", 2, "Allergy Type"),
            ["allergy.allergen"] = new("AL1", 3, "Allergen"),
            ["allergy.severity"] = new("AL1", 4, "Allergy Severity"),

            // Week 4.1: High-ROI semantic paths for 85% coverage
            // Encounter workflow - PV1 segment extensions
            ["encounter.location"] = new("PV1", 3, "Assigned Patient Location", "PL.1"),
            ["encounter.admissionDate"] = new("PV1", 44, "Admit Date/Time"),
            ["encounter.class"] = new("PV1", 2, "Patient Class"),
            ["encounter.room"] = new("PV1", 3, "Assigned Patient Location", "PL.3"),
            ["encounter.bed"] = new("PV1", 3, "Assigned Patient Location", "PL.4"),
            ["encounter.facility"] = new("PV1", 3, "Assigned Patient Location", "PL.1"),

            // Enhanced provider information
            ["provider.npi"] = new("PV1", 7, "Attending Provider NPI", "XCN.1"),
            ["provider.specialty"] = new("PV1", 7, "Provider Specialty", "XCN.13"),
            ["provider.department"] = new("PV1", 7, "Provider Department", "XCN.14"),

            // Patient demographics extensions
            ["patient.ethnicity"] = new("PID", 22, "Ethnic Group"),
            ["patient.language"] = new("PID", 15, "Primary Language"),
            ["patient.religion"] = new("PID", 17, "Religion"),
            ["patient.maritalStatus"] = new("PID", 16, "Marital Status"),

            // Enhanced observation patterns
            ["observation.dateTime"] = new("OBX", 14, "Date/Time of Observation"),
            ["observation.performer"] = new("OBX", 16, "Responsible Observer"),
            ["observation.method"] = new("OBX", 17, "Observation Method"),

            // Insurance information - IN1 segment
            ["insurance.planName"] = new("IN1", 4, "Insurance Plan Name"),
            ["insurance.groupNumber"] = new("IN1", 8, "Group Number"),
            ["insurance.policyNumber"] = new("IN1", 36, "Policy Number")
        };

    public string Standard => "HL7"; // Family standard, not version-specific

    public HL7FieldPathPlugin(
        IStandardReferenceService referenceService,
        IConfigurationService configurationService,
        ILogger<HL7FieldPathPlugin> logger)
    {
        _referenceService = referenceService;
        _configurationService = configurationService;
        _logger = logger;
    }

    public async Task<Result<string>> ResolvePathAsync(string semanticPath, string messageType)
    {
        if (!SemanticMappings.TryGetValue(semanticPath, out var mapping))
        {
            return Result<string>.Failure($"Semantic path '{semanticPath}' not recognized for HL7 messages");
        }

        // Validate that the segment is allowed in this message type
        var messageValidation = await ValidateSegmentInMessageAsync(mapping.SegmentCode, messageType);
        if (messageValidation.IsFailure)
        {
            return Result<string>.Failure(messageValidation.Error);
        }

        // Build the field path
        var fieldPath = $"{mapping.SegmentCode}.{mapping.FieldPosition}";
        if (!string.IsNullOrEmpty(mapping.ComponentPath))
        {
            fieldPath += $".{mapping.ComponentPath}";
        }

        _logger.LogDebug("Resolved HL7 path {SemanticPath} → {FieldPath} for {MessageType}",
            semanticPath, fieldPath, messageType);

        return Result<string>.Success(fieldPath);
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
            var validation = await ValidateSegmentInMessageAsync(kvp.Value.SegmentCode, messageType);
            if (validation.IsSuccess)
            {
                var fieldPath = $"{kvp.Value.SegmentCode}.{kvp.Value.FieldPosition}";
                availablePaths[kvp.Key] = $"{kvp.Value.Description} ({fieldPath})";
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

        // Get field definition from our HL7 data
        var fieldResult = await GetFieldDefinitionAsync(mapping.SegmentCode, mapping.FieldPosition);
        if (fieldResult.IsFailure)
        {
            return Result<FieldValidationResult>.Success(new FieldValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Could not load field definition: {fieldResult.Error.Message}"
            });
        }

        var field = fieldResult.Value;
        var suggestions = new List<string>();
        var isValid = true;

        // Basic validation - check if required field is empty
        if (field.Optionality == "R" && string.IsNullOrWhiteSpace(value))
        {
            isValid = false;
            suggestions.Add($"Field {mapping.SegmentCode}.{mapping.FieldPosition} is required and cannot be empty");
        }

        // Table-based validation if field has a table reference
        if (!string.IsNullOrEmpty(field.Table) && !string.IsNullOrWhiteSpace(value))
        {
            var tableValidation = await ValidateAgainstTableAsync(value, field.Table);
            if (tableValidation.IsFailure)
            {
                isValid = false;
                suggestions.Add($"Invalid value for table {field.Table}: {tableValidation.Error.Message}");
            }
        }

        var fieldPath = $"{mapping.SegmentCode}.{mapping.FieldPosition}";
        if (!string.IsNullOrEmpty(mapping.ComponentPath))
        {
            fieldPath += $".{mapping.ComponentPath}";
        }

        return Result<FieldValidationResult>.Success(new FieldValidationResult
        {
            IsValid = isValid,
            ErrorMessage = isValid ? null : $"Invalid value for {semanticPath}",
            Suggestions = suggestions,
            FieldType = field.DataType,
            TableReference = field.Table,
            StandardFieldPath = fieldPath
        });
    }

    /// <summary>
    /// Gets the user's configured HL7 standard version (e.g., "hl7v23", "hl7v24").
    /// Falls back to "hl7v23" if not configured.
    /// </summary>
    private async Task<string> GetConfiguredHL7StandardAsync()
    {
        try
        {
            var versionResult = await _configurationService.GetDefaultStandardVersionAsync("HL7");
            if (versionResult.IsSuccess)
            {
                var version = versionResult.Value;
                // Convert "v23" to "hl7v23" format for data lookup
                if (version.StartsWith("v"))
                {
                    return $"hl7{version}";
                }
                return $"hl7{version}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get configured HL7 standard, using default hl7v23");
        }

        return "hl7v23"; // Safe fallback
    }

    private async Task<Result> ValidateSegmentInMessageAsync(string segmentCode, string messageType)
    {
        try
        {
            // Use IStandardReferenceService to validate segment in message structure using configured HL7 data
            var messageStructureResult = await GetMessageStructureAsync(messageType);
            if (messageStructureResult.IsSuccess)
            {
                var allowedSegments = messageStructureResult.Value;
                var isValidSegment = allowedSegments.Contains(segmentCode.ToUpperInvariant());

                if (!isValidSegment)
                {
                    var suggestedSegments = allowedSegments.Take(5).ToList();
                    var suggestion = suggestedSegments.Any()
                        ? $" Valid segments for {messageType}: {string.Join(", ", suggestedSegments)}"
                        : "";

                    return Result.Failure($"Segment '{segmentCode}' is not valid for message type '{messageType}'.{suggestion}");
                }
            }
            else
            {
                // Fallback to basic validation if message structure lookup fails
                _logger.LogWarning("Could not load message structure for {MessageType}, using permissive validation: {Error}",
                    messageType, messageStructureResult.Error);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating segment {SegmentCode} for message {MessageType}",
                segmentCode, messageType);
            return Result.Success(); // Permissive fallback on error
        }
    }

    private async Task<Result<IReadOnlyList<string>>> GetMessageStructureAsync(string messageType)
    {
        try
        {
            // Normalize message type (e.g., "ADT^A01" → "ADT_A01")
            var normalizedMessageType = messageType.Replace("^", "_").ToUpperInvariant();

            // Get user's configured HL7 standard version
            var hl7Standard = await GetConfiguredHL7StandardAsync();

            // Try to load message definition from configured HL7 JSON data
            var messageResult = await _referenceService.LookupAsync(normalizedMessageType, hl7Standard);
            if (messageResult.IsSuccess)
            {
                var messageElement = messageResult.Value;

                // Extract segment list from message structure
                // TODO: This assumes the message structure contains segment information in ChildPaths
                // Actual implementation may need to parse message structure differently based on JSON format
                var segments = messageElement.ChildPaths?.Select(path => path.Split('.')[0].ToUpperInvariant()).Distinct().ToList()
                    ?? new List<string>();

                if (segments.Any())
                {
                    return Result<IReadOnlyList<string>>.Success(segments);
                }
            }

            // Fallback: Return basic segments that are common to most HL7 messages
            var commonSegments = new[] { "MSH", "PID", "PV1", "OBX", "OBR", "EVN", "NTE" };
            _logger.LogDebug("Using common segment fallback for message type {MessageType}", messageType);

            return Result<IReadOnlyList<string>>.Success(commonSegments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading message structure for {MessageType}", messageType);

            // Ultimate fallback
            var fallbackSegments = new[] { "MSH", "PID", "PV1" };
            return Result<IReadOnlyList<string>>.Success(fallbackSegments);
        }
    }

    private async Task<Result<FieldDefinition>> GetFieldDefinitionAsync(string segmentCode, int fieldPosition)
    {
        try
        {
            // Get user's configured HL7 standard version
            var hl7Standard = await GetConfiguredHL7StandardAsync();

            // Use IStandardReferenceService to load field definition
            var fieldPath = $"{segmentCode}.{fieldPosition}";
            var fieldResult = await _referenceService.LookupAsync(fieldPath, hl7Standard);

            if (fieldResult.IsFailure)
            {
                return Result<FieldDefinition>.Failure($"Could not load field '{fieldPath}': {fieldResult.Error.Message}");
            }

            var element = fieldResult.Value;

            return Result<FieldDefinition>.Success(new FieldDefinition
            {
                FieldName = element.Name ?? fieldPath,
                DataType = element.DataType ?? "ST",
                Optionality = element.Usage ?? "O",
                Table = element.TableReference ?? "",
                Length = element.MaxLength,
                Repeatability = element.Repeatability ?? "-"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading field definition for {SegmentCode}.{FieldPosition}",
                segmentCode, fieldPosition);
            return Result<FieldDefinition>.Failure($"Error loading field definition: {ex.Message}");
        }
    }

    private async Task<Result> ValidateAgainstTableAsync(string value, string tableId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tableId))
            {
                return Result.Success(); // No table to validate against
            }

            // Get user's configured HL7 standard version
            var hl7Standard = await GetConfiguredHL7StandardAsync();

            // Load table definition using IStandardReferenceService
            var tableResult = await _referenceService.LookupAsync(tableId, hl7Standard);
            if (tableResult.IsFailure)
            {
                _logger.LogWarning("Could not load table '{TableId}' for validation: {Error}",
                    tableId, tableResult.Error.Message);
                return Result.Success(); // Permissive fallback
            }

            var tableElement = tableResult.Value;
            var validValues = tableElement.ValidValues?.Select(v => v.Code).ToList() ?? new List<string>();

            if (validValues.Count == 0)
            {
                _logger.LogWarning("Table '{TableId}' has no values defined", tableId);
                return Result.Success(); // Permissive if no values defined
            }

            // Check if value is in the valid list
            var isValid = validValues.Contains(value, StringComparer.OrdinalIgnoreCase);

            if (!isValid)
            {
                var suggestions = validValues.Take(5).ToList(); // Show first 5 valid values
                var suggestionText = suggestions.Count > 0
                    ? $"Valid values include: {string.Join(", ", suggestions)}"
                    : "See table definition for valid values";

                return Result.Failure($"Value '{value}' not found in table {tableId}. {suggestionText}");
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating value '{Value}' against table '{TableId}'",
                value, tableId);
            return Result.Success(); // Permissive fallback on error
        }
    }

    private record SemanticMapping(
        string SegmentCode,
        int FieldPosition,
        string Description,
        string? ComponentPath = null);

    private record FieldDefinition
    {
        public string FieldName { get; init; } = "";
        public string DataType { get; init; } = "";
        public string Optionality { get; init; } = "";
        public string Table { get; init; } = "";
        public int? Length { get; init; }
        public string Repeatability { get; init; } = "";
    }
}