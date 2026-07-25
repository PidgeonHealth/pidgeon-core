// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.DeIdentification;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.DeIdentification;

/// <summary>
/// Service for detecting Protected Health Information (PHI) in healthcare messages.
/// Implements pattern recognition for all 18 HIPAA Safe Harbor identifiers.
/// </summary>
public interface IPhiDetector
{
    /// <summary>
    /// Scans a healthcare message for potential PHI using pattern recognition.
    /// Identifies all 18 categories of HIPAA Safe Harbor identifiers.
    /// </summary>
    /// <param name="message">Healthcare message to scan for PHI</param>
    /// <param name="options">Detection configuration options</param>
    /// <returns>Detection result with found PHI items and confidence scores</returns>
    Task<Result<PhiDetectionResult>> ScanForPhiAsync(string message, PhiDetectionOptions? options = null);

    /// <summary>
    /// Validates that a message contains no remaining PHI after de-identification.
    /// More aggressive scanning with lower confidence thresholds.
    /// </summary>
    /// <param name="deidentifiedMessage">Message to validate for PHI absence</param>
    /// <param name="options">Validation configuration options</param>
    /// <returns>Validation result indicating any potential remaining PHI</returns>
    Task<Result<PhiValidationResult>> ValidatePhiRemovalAsync(
        string deidentifiedMessage, 
        PhiDetectionOptions? options = null);

    /// <summary>
    /// Detects PHI in specific HL7 fields using field-aware pattern matching.
    /// Provides more accurate detection by understanding field context.
    /// </summary>
    /// <param name="fieldValue">HL7 field value to analyze</param>
    /// <param name="fieldPath">HL7 field path (e.g., "PID.5" for patient name)</param>
    /// <param name="options">Detection configuration options</param>
    /// <returns>Field-specific PHI detection result</returns>
    Task<Result<FieldPhiDetectionResult>> DetectFieldPhiAsync(
        string fieldValue, 
        string fieldPath, 
        PhiDetectionOptions? options = null);

    /// <summary>
    /// Analyzes free text fields for embedded PHI using natural language processing.
    /// Detects identifiers that may be embedded in clinical notes or comments.
    /// </summary>
    /// <param name="freeText">Free text content to analyze</param>
    /// <param name="context">Clinical context to improve detection accuracy</param>
    /// <param name="options">Detection configuration options</param>
    /// <returns>Free text PHI detection result with location information</returns>
    Task<Result<FreeTextPhiDetectionResult>> AnalyzeFreeTextAsync(
        string freeText, 
        ClinicalContext? context = null,
        PhiDetectionOptions? options = null);

    /// <summary>
    /// Estimates re-identification risk based on quasi-identifiers present.
    /// Supports Expert Determination method risk assessment.
    /// </summary>
    /// <param name="messages">Messages to analyze for re-identification risk</param>
    /// <param name="populationSize">Reference population size for risk calculation</param>
    /// <param name="options">Risk assessment options</param>
    /// <returns>Re-identification risk assessment</returns>
    Task<Result<RiskAssessmentResult>> AssessReIdentificationRiskAsync(
        IEnumerable<string> messages, 
        int populationSize = 320_000_000, // US population
        PhiDetectionOptions? options = null);

    /// <summary>
    /// Gets the standard HIPAA Safe Harbor field mapping for HL7 messages.
    /// Maps HL7 field paths to HIPAA identifier categories.
    /// </summary>
    /// <returns>Dictionary mapping field paths to HIPAA categories and identifier types</returns>
    IReadOnlyDictionary<string, PhiFieldMapping> GetStandardFieldMappings();

    /// <summary>
    /// Registers custom PHI detection patterns for organization-specific identifiers.
    /// Allows detection of non-standard identifiers beyond the 18 HIPAA categories.
    /// </summary>
    /// <param name="patterns">Custom PHI patterns to register</param>
    /// <returns>Success/failure result</returns>
    Result<Unit> RegisterCustomPatterns(IEnumerable<CustomPhiPattern> patterns);
}

/// <summary>
/// Configuration options for PHI detection operations.
/// </summary>
public record PhiDetectionOptions
{
    /// <summary>
    /// Minimum confidence threshold for PHI detection (0-1).
    /// Higher values reduce false positives but may miss some PHI.
    /// </summary>
    public double MinimumConfidence { get; init; } = 0.7;

    /// <summary>
    /// Whether to include low-confidence detections in results.
    /// Useful for comprehensive scanning but may include false positives.
    /// </summary>
    public bool IncludeLowConfidence { get; init; } = false;

    /// <summary>
    /// Maximum number of characters to include in PHI samples.
    /// Controls how much of detected PHI is returned in results.
    /// </summary>
    public int MaxSampleLength { get; init; } = 20;

    /// <summary>
    /// Whether to redact detected PHI in result samples.
    /// When true, partial redaction preserves some characters for validation.
    /// </summary>
    public bool RedactSamples { get; init; } = true;

    /// <summary>
    /// Additional field paths to scan beyond standard HIPAA mappings.
    /// Useful for organization-specific extensions or custom fields.
    /// </summary>
    public HashSet<string> AdditionalFieldsToScan { get; init; } = new();

    /// <summary>
    /// Custom PHI patterns to apply during detection.
    /// Supplements standard patterns with organization-specific identifiers.
    /// </summary>
    public IReadOnlyList<CustomPhiPattern> CustomPatterns { get; init; } = Array.Empty<CustomPhiPattern>();

    /// <summary>
    /// Whether to perform deep analysis of free text fields.
    /// Enables NLP-based detection but increases processing time.
    /// </summary>
    public bool EnableFreeTextAnalysis { get; init; } = true;

    /// <summary>
    /// Timeout for PHI detection operations.
    /// Prevents long-running scans from blocking processing.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Result of PHI validation after de-identification.
/// </summary>
public record PhiValidationResult
{
    /// <summary>
    /// Whether the message passed PHI validation (no PHI detected).
    /// </summary>
    public required bool PassedValidation { get; init; }

    /// <summary>
    /// Any potential PHI items that were detected.
    /// Should be empty for successful de-identification.
    /// </summary>
    public required IReadOnlyList<PhiDetectionItem> RemainingPhiItems { get; init; }

    /// <summary>
    /// Overall confidence in the validation result.
    /// </summary>
    public double ValidationConfidence { get; init; }

    /// <summary>
    /// Validation statistics and performance metrics.
    /// </summary>
    public required PhiValidationStatistics Statistics { get; init; }
}

/// <summary>
/// Statistics for PHI validation operations.
/// </summary>
public record PhiValidationStatistics
{
    /// <summary>
    /// Total number of fields validated.
    /// </summary>
    public required int FieldsValidated { get; init; }

    /// <summary>
    /// Number of potential PHI items found.
    /// </summary>
    public required int PotentialPhiItems { get; init; }

    /// <summary>
    /// Time taken for validation.
    /// </summary>
    public required TimeSpan ValidationTime { get; init; }

    /// <summary>
    /// Validation success rate.
    /// </summary>
    public double SuccessRate => FieldsValidated == 0 ? 1.0 : 
        (FieldsValidated - PotentialPhiItems) / (double)FieldsValidated;
}

/// <summary>
/// Result of PHI detection in a specific field.
/// </summary>
public record FieldPhiDetectionResult
{
    /// <summary>
    /// Field path that was analyzed.
    /// </summary>
    public required string FieldPath { get; init; }

    /// <summary>
    /// PHI items detected in this field.
    /// </summary>
    public required IReadOnlyList<PhiDetectionItem> DetectedItems { get; init; }

    /// <summary>
    /// Expected identifier type for this field based on HL7 standards.
    /// </summary>
    public IdentifierType? ExpectedType { get; init; }

    /// <summary>
    /// Whether this field contains PHI according to HIPAA Safe Harbor.
    /// </summary>
    public bool IsPhiField { get; init; }

    /// <summary>
    /// HIPAA Safe Harbor category for this field (1-18).
    /// </summary>
    public int? HipaaCategory { get; init; }
}

/// <summary>
/// Result of PHI detection in free text content.
/// </summary>
public record FreeTextPhiDetectionResult
{
    /// <summary>
    /// PHI items detected in the free text.
    /// </summary>
    public required IReadOnlyList<FreeTextPhiItem> DetectedItems { get; init; }

    /// <summary>
    /// Overall confidence in the detection results.
    /// </summary>
    public double OverallConfidence { get; init; }

    /// <summary>
    /// Named entities identified during analysis.
    /// </summary>
    public IReadOnlyList<NamedEntity> NamedEntities { get; init; } = Array.Empty<NamedEntity>();

    /// <summary>
    /// Clinical concepts identified in the text.
    /// </summary>
    public IReadOnlyList<ClinicalConcept> ClinicalConcepts { get; init; } = Array.Empty<ClinicalConcept>();
}

/// <summary>
/// PHI item detected in free text with location information.
/// </summary>
public record FreeTextPhiItem
{
    /// <summary>
    /// Type of PHI identifier detected.
    /// </summary>
    public required IdentifierType Type { get; init; }

    /// <summary>
    /// Start position in the text.
    /// </summary>
    public required int StartPosition { get; init; }

    /// <summary>
    /// End position in the text.
    /// </summary>
    public required int EndPosition { get; init; }

    /// <summary>
    /// Text content of the detected PHI (may be redacted).
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Confidence level that this is actually PHI.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Surrounding context for better understanding.
    /// </summary>
    public string? Context { get; init; }
}

/// <summary>
/// Mapping of HL7 field to PHI characteristics.
/// </summary>
public record PhiFieldMapping
{
    /// <summary>
    /// HIPAA Safe Harbor category (1-18).
    /// </summary>
    public required int HipaaCategory { get; init; }

    /// <summary>
    /// Type of identifier expected in this field.
    /// </summary>
    public required IdentifierType IdentifierType { get; init; }

    /// <summary>
    /// Whether this field is required to be removed under Safe Harbor.
    /// </summary>
    public required bool RequiresRemoval { get; init; }

    /// <summary>
    /// Description of the PHI type in this field.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Regular expression pattern for detecting this PHI type.
    /// </summary>
    public string? DetectionPattern { get; init; }
}

/// <summary>
/// Custom PHI pattern for organization-specific identifiers.
/// </summary>
public record CustomPhiPattern
{
    /// <summary>
    /// Name of the custom PHI pattern.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Regular expression pattern for detection.
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// Identifier type for this pattern.
    /// </summary>
    public required IdentifierType IdentifierType { get; init; }

    /// <summary>
    /// Confidence level when this pattern matches (0-1).
    /// </summary>
    public double Confidence { get; init; } = 0.9;

    /// <summary>
    /// Description of what this pattern detects.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this pattern should be case-sensitive.
    /// </summary>
    public bool CaseSensitive { get; init; } = false;
}

/// <summary>
/// Clinical context to improve free text PHI detection.
/// </summary>
public record ClinicalContext
{
    /// <summary>
    /// Type of clinical document or message.
    /// </summary>
    public string? DocumentType { get; init; }

    /// <summary>
    /// Clinical specialty or department.
    /// </summary>
    public string? Specialty { get; init; }

    /// <summary>
    /// Healthcare facility type.
    /// </summary>
    public string? FacilityType { get; init; }

    /// <summary>
    /// Additional context keywords.
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Named entity identified during text analysis.
/// </summary>
public record NamedEntity
{
    /// <summary>
    /// Text content of the entity.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Entity type (PERSON, ORG, GPE, etc.).
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Start position in the text.
    /// </summary>
    public required int StartPosition { get; init; }

    /// <summary>
    /// End position in the text.
    /// </summary>
    public required int EndPosition { get; init; }

    /// <summary>
    /// Confidence in the entity classification.
    /// </summary>
    public double Confidence { get; init; }
}

/// <summary>
/// Clinical concept identified in text.
/// </summary>
public record ClinicalConcept
{
    /// <summary>
    /// Concept text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Clinical concept type (DIAGNOSIS, MEDICATION, PROCEDURE, etc.).
    /// </summary>
    public required string ConceptType { get; init; }

    /// <summary>
    /// Medical coding system (ICD-10, SNOMED, etc.).
    /// </summary>
    public string? CodingSystem { get; init; }

    /// <summary>
    /// Medical code if identified.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Confidence in the concept identification.
    /// </summary>
    public double Confidence { get; init; }
}