// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.DeIdentification;

/// <summary>
/// Result of a de-identification operation containing the processed message(s) and metadata.
/// Provides comprehensive information about what was changed and compliance verification.
/// </summary>
public record DeIdentificationResult
{
    /// <summary>
    /// The de-identified message(s) with all PHI removed or replaced.
    /// </summary>
    public required IReadOnlyList<string> DeIdentifiedMessages { get; init; }

    /// <summary>
    /// Statistics about the de-identification operation.
    /// </summary>
    public required DeIdentificationStatistics Statistics { get; init; }

    /// <summary>
    /// Compliance verification results showing adherence to standards.
    /// </summary>
    public required ComplianceVerification Compliance { get; init; }

    /// <summary>
    /// Processing metadata including timing and options used.
    /// </summary>
    public required ProcessingMetadata Metadata { get; init; }

    /// <summary>
    /// Detailed audit trail if requested in options.
    /// Maps original values to their synthetic replacements.
    /// </summary>
    public IReadOnlyDictionary<string, DeIdentificationAction>? AuditTrail { get; init; }

    /// <summary>
    /// Validation warnings encountered during processing.
    /// Non-blocking issues that should be reviewed.
    /// </summary>
    public IReadOnlyList<ValidationWarning> Warnings { get; init; } = Array.Empty<ValidationWarning>();

    /// <summary>
    /// ID mapping for maintaining consistency across related messages.
    /// Can be saved and reused for processing additional messages.
    /// </summary>
    public IReadOnlyDictionary<string, string> IdMappings { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Creates a successful de-identification result.
    /// </summary>
    public static DeIdentificationResult Success(
        IReadOnlyList<string> deIdentifiedMessages,
        DeIdentificationStatistics statistics,
        ComplianceVerification compliance,
        ProcessingMetadata metadata,
        IReadOnlyDictionary<string, DeIdentificationAction>? auditTrail = null,
        IReadOnlyList<ValidationWarning>? warnings = null,
        IReadOnlyDictionary<string, string>? idMappings = null)
    {
        return new DeIdentificationResult
        {
            DeIdentifiedMessages = deIdentifiedMessages,
            Statistics = statistics,
            Compliance = compliance,
            Metadata = metadata,
            AuditTrail = auditTrail,
            Warnings = warnings ?? Array.Empty<ValidationWarning>(),
            IdMappings = idMappings ?? new Dictionary<string, string>()
        };
    }
}

/// <summary>
/// Statistics about identifiers found and processed during de-identification.
/// </summary>
public record DeIdentificationStatistics
{
    /// <summary>
    /// Total number of messages processed.
    /// </summary>
    public required int TotalMessages { get; init; }

    /// <summary>
    /// Total number of PHI identifiers found and processed.
    /// </summary>
    public required int TotalIdentifiersProcessed { get; init; }

    /// <summary>
    /// Breakdown of identifiers by type (Name, MRN, SSN, etc.).
    /// </summary>
    public required IReadOnlyDictionary<IdentifierType, int> IdentifiersByType { get; init; }

    /// <summary>
    /// Number of fields modified during de-identification.
    /// </summary>
    public required int FieldsModified { get; init; }

    /// <summary>
    /// Number of dates shifted.
    /// </summary>
    public required int DatesShifted { get; init; }

    /// <summary>
    /// Average processing time per message in milliseconds.
    /// </summary>
    public required double AverageProcessingTimeMs { get; init; }

    /// <summary>
    /// Total processing time for all messages.
    /// </summary>
    public required TimeSpan TotalProcessingTime { get; init; }

    /// <summary>
    /// Number of unique patients/subjects identified across messages.
    /// Based on consistent identifier mapping.
    /// </summary>
    public required int UniqueSubjects { get; init; }

    /// <summary>
    /// Data utility score (0-1) indicating how much original data structure was preserved.
    /// Higher scores indicate better preservation of statistical properties.
    /// </summary>
    public double DataUtilityScore { get; init; } = 1.0;
}

/// <summary>
/// Verification that de-identification meets compliance requirements.
/// </summary>
public record ComplianceVerification
{
    /// <summary>
    /// Whether the result meets HIPAA Safe Harbor requirements.
    /// All 18 identifiers must be removed or properly handled.
    /// </summary>
    public required bool MeetsSafeHarbor { get; init; }

    /// <summary>
    /// Checklist of HIPAA Safe Harbor requirements and their status.
    /// </summary>
    public required IReadOnlyDictionary<string, bool> SafeHarborChecklist { get; init; }

    /// <summary>
    /// Re-identification risk assessment for Expert Determination method.
    /// Must be "very small" (typically <0.04%) for compliance.
    /// </summary>
    public double? ReIdentificationRisk { get; init; }

    /// <summary>
    /// K-anonymity score if statistical methods were used.
    /// Minimum value across all equivalence classes.
    /// </summary>
    public int? KAnonymityScore { get; init; }

    /// <summary>
    /// L-diversity score if statistical methods were used.
    /// Minimum diversity across all sensitive attributes.
    /// </summary>
    public int? LDiversityScore { get; init; }

    /// <summary>
    /// Any remaining PHI detected after de-identification.
    /// Should be empty for successful de-identification.
    /// </summary>
    public IReadOnlyList<RemainingPhiWarning> RemainingPhi { get; init; } = Array.Empty<RemainingPhiWarning>();

    /// <summary>
    /// Overall compliance status.
    /// </summary>
    public ComplianceStatus Status { get; init; }

    /// <summary>
    /// Additional compliance notes or recommendations.
    /// </summary>
    public string? Notes { get; init; }
}

/// <summary>
/// Processing metadata for the de-identification operation.
/// </summary>
public record ProcessingMetadata
{
    /// <summary>
    /// Timestamp when processing started.
    /// </summary>
    public required DateTime StartedAt { get; init; }

    /// <summary>
    /// Timestamp when processing completed.
    /// </summary>
    public required DateTime CompletedAt { get; init; }

    /// <summary>
    /// De-identification options used for this operation.
    /// </summary>
    public required DeIdentificationOptions Options { get; init; }

    /// <summary>
    /// Version of the de-identification engine.
    /// </summary>
    public string EngineVersion { get; init; } = "1.0.0";

    /// <summary>
    /// Standards and versions processed (e.g., "HL7 v2.3").
    /// </summary>
    public IReadOnlyList<string> StandardsProcessed { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Salt used for deterministic generation (may be redacted for security).
    /// </summary>
    public string? SaltUsed { get; init; }

    /// <summary>
    /// Processing mode (single message, batch, preview, etc.).
    /// </summary>
    public string ProcessingMode { get; init; } = "Standard";
}

/// <summary>
/// Warning about validation issues encountered during processing.
/// </summary>
public record ValidationWarning
{
    /// <summary>
    /// Type of validation issue.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Human-readable warning message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Field or location where the warning occurred.
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Severity level of the warning.
    /// </summary>
    public WarningSeverity Severity { get; init; } = WarningSeverity.Medium;
}

/// <summary>
/// Warning about PHI that may still remain after de-identification.
/// </summary>
public record RemainingPhiWarning
{
    /// <summary>
    /// Type of PHI potentially remaining.
    /// </summary>
    public required string PhiType { get; init; }

    /// <summary>
    /// Location in the message where PHI was detected.
    /// </summary>
    public required string Location { get; init; }

    /// <summary>
    /// Sample of the potentially identifying content (may be redacted).
    /// </summary>
    public required string Sample { get; init; }

    /// <summary>
    /// Confidence level that this is actually PHI (0-1).
    /// </summary>
    public double Confidence { get; init; } = 0.5;
}

/// <summary>
/// Compliance status levels.
/// </summary>
public enum ComplianceStatus
{
    /// <summary>
    /// Fully compliant with all requirements.
    /// </summary>
    Compliant,

    /// <summary>
    /// Mostly compliant with minor warnings.
    /// </summary>
    CompliantWithWarnings,

    /// <summary>
    /// Not compliant - manual review required.
    /// </summary>
    NonCompliant,

    /// <summary>
    /// Unable to determine compliance status.
    /// </summary>
    Unknown
}

/// <summary>
/// Severity levels for validation warnings.
/// </summary>
public enum WarningSeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Result of analyzing content for PHI patterns and distribution.
/// </summary>
public record PhiAnalysisResult
{
    /// <summary>
    /// Detected message format (HL7, FHIR, etc.).
    /// </summary>
    public required string MessageFormat { get; init; }

    /// <summary>
    /// PHI items identified during analysis.
    /// </summary>
    public required IReadOnlyList<PhiDetectionItem> DetectedFields { get; init; }

    /// <summary>
    /// Total number of fields analyzed in the content.
    /// </summary>
    public required int TotalFieldsAnalyzed { get; init; }

    /// <summary>
    /// Number of fields containing potential PHI.
    /// </summary>
    public required int PhiFieldsFound { get; init; }

    /// <summary>
    /// Overall risk assessment based on PHI types found.
    /// </summary>
    public required string OverallRiskAssessment { get; init; }
}

/// <summary>
/// Result of batch consistency validation operations.
/// </summary>
public record BatchConsistencyResult
{
    /// <summary>
    /// Whether all messages in the batch maintain consistency.
    /// </summary>
    public required bool IsConsistent { get; init; }

    /// <summary>
    /// Number of messages processed in the batch.
    /// </summary>
    public required int TotalMessages { get; init; }

    /// <summary>
    /// Number of unique patient identifiers found.
    /// </summary>
    public required int UniquePatientIds { get; init; }

    /// <summary>
    /// Consistency violations found during validation.
    /// </summary>
    public required IReadOnlyList<ConsistencyViolation> Violations { get; init; }

    /// <summary>
    /// Time taken to complete the consistency check.
    /// </summary>
    public required TimeSpan ProcessingTime { get; init; }
}

/// <summary>
/// Individual consistency violation found during batch processing.
/// </summary>
public record ConsistencyViolation
{
    /// <summary>
    /// Type of consistency violation detected.
    /// </summary>
    public required string ViolationType { get; init; }

    /// <summary>
    /// Patient identifier involved in the violation.
    /// </summary>
    public required string PatientId { get; init; }

    /// <summary>
    /// Description of the consistency issue.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Message indices where the violation was found.
    /// </summary>
    public required IReadOnlyList<int> AffectedMessageIndices { get; init; }
}

/// <summary>
/// Result of consistency validation operations.
/// </summary>
public record ConsistencyValidationResult
{
    /// <summary>
    /// Whether the consistency validation passed.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Consistency violations found.
    /// </summary>
    public required IReadOnlyList<ConsistencyViolation> Violations { get; init; }

    /// <summary>
    /// Number of ID mappings validated.
    /// </summary>
    public required int ValidatedMappings { get; init; }

    /// <summary>
    /// Overall confidence in the consistency check.
    /// </summary>
    public required double ConfidenceScore { get; init; }
}

