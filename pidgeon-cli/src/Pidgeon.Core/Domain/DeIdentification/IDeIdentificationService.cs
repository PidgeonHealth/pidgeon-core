// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Domain.DeIdentification;

/// <summary>
/// Domain service for de-identifying healthcare messages.
/// Provides standards-agnostic de-identification capabilities following HIPAA requirements.
/// </summary>
public interface IDeIdentificationService
{
    /// <summary>
    /// De-identifies a single healthcare message using the provided context.
    /// Removes or replaces PHI while maintaining message structure and clinical relationships.
    /// </summary>
    /// <param name="message">Healthcare message containing PHI</param>
    /// <param name="context">De-identification context with mappings and configuration</param>
    /// <returns>De-identified message with PHI removed/replaced</returns>
    Result<string> DeIdentifyMessage(string message, DeIdentificationContext context);

    /// <summary>
    /// De-identifies multiple related healthcare messages maintaining consistency.
    /// Ensures same patient identifiers map to same synthetic values across all messages.
    /// </summary>
    /// <param name="messages">Collection of related healthcare messages</param>
    /// <param name="options">De-identification configuration options</param>
    /// <returns>Complete de-identification result with all messages and metadata</returns>
    Task<Result<DeIdentificationResult>> DeIdentifyMessagesAsync(
        IEnumerable<string> messages, 
        DeIdentificationOptions options);

    /// <summary>
    /// Creates a new de-identification context from the provided options.
    /// Initializes mappings, salt, date shift, and other configuration.
    /// </summary>
    /// <param name="options">Configuration options for de-identification</param>
    /// <returns>Initialized context ready for message processing</returns>
    Result<DeIdentificationContext> CreateContext(DeIdentificationOptions options);

    /// <summary>
    /// Validates that a de-identified message meets compliance requirements.
    /// Scans for remaining PHI and verifies HIPAA Safe Harbor compliance.
    /// </summary>
    /// <param name="original">Original message with PHI</param>
    /// <param name="deidentified">De-identified message to validate</param>
    /// <param name="options">De-identification options used</param>
    /// <returns>Compliance verification results</returns>
    Task<Result<ComplianceVerification>> ValidateDeIdentificationAsync(
        string original, 
        string deidentified, 
        DeIdentificationOptions options);

    /// <summary>
    /// Detects potential PHI in a message without modifying it.
    /// Useful for analysis and compliance assessment before de-identification.
    /// </summary>
    /// <param name="message">Message to scan for PHI</param>
    /// <returns>List of potential PHI locations and types</returns>
    Task<Result<PhiDetectionResult>> DetectPhiAsync(string message);

    /// <summary>
    /// Estimates re-identification risk for statistical de-identification methods.
    /// Supports Expert Determination method compliance assessment.
    /// </summary>
    /// <param name="messages">De-identified messages to assess</param>
    /// <param name="quasiIdentifiers">Quasi-identifier fields to analyze</param>
    /// <returns>Re-identification risk assessment</returns>
    Task<Result<RiskAssessmentResult>> AssessReIdentificationRiskAsync(
        IEnumerable<string> messages, 
        IEnumerable<string> quasiIdentifiers);

    /// <summary>
    /// Generates a comprehensive de-identification report for audit and compliance.
    /// Includes statistics, compliance verification, and audit trail.
    /// </summary>
    /// <param name="result">De-identification result to report on</param>
    /// <param name="format">Output format (JSON, HTML, PDF, etc.)</param>
    /// <returns>Formatted compliance report</returns>
    Task<Result<string>> GenerateComplianceReportAsync(
        DeIdentificationResult result, 
        ReportFormat format = ReportFormat.Json);

    /// <summary>
    /// Loads ID mappings from a previous de-identification session.
    /// Enables consistent processing of related messages across time.
    /// </summary>
    /// <param name="mappingFilePath">Path to saved ID mappings</param>
    /// <returns>Dictionary of original to synthetic ID mappings</returns>
    Task<Result<Dictionary<string, string>>> LoadIdMappingsAsync(string mappingFilePath);

    /// <summary>
    /// Saves ID mappings for reuse in future de-identification sessions.
    /// Maintains consistency when processing related messages separately.
    /// </summary>
    /// <param name="mappings">ID mappings to save</param>
    /// <param name="mappingFilePath">Path where mappings should be saved</param>
    /// <returns>Success/failure result</returns>
    Task<Result<Unit>> SaveIdMappingsAsync(
        IReadOnlyDictionary<string, string> mappings, 
        string mappingFilePath);
}

/// <summary>
/// Result of PHI detection scan showing potential identifiers found.
/// </summary>
public record PhiDetectionResult
{
    /// <summary>
    /// List of potential PHI items detected in the message.
    /// </summary>
    public required IReadOnlyList<PhiDetectionItem> DetectedPhi { get; init; }

    /// <summary>
    /// Overall confidence that PHI was correctly identified.
    /// </summary>
    public double OverallConfidence { get; init; }

    /// <summary>
    /// Summary statistics about PHI detection.
    /// </summary>
    public required PhiDetectionStatistics Statistics { get; init; }
}

/// <summary>
/// Individual PHI item detected during scanning.
/// </summary>
public record PhiDetectionItem
{
    /// <summary>
    /// Type of PHI identifier detected.
    /// </summary>
    public required IdentifierType Type { get; init; }

    /// <summary>
    /// Location in the message (field path, line number, etc.).
    /// </summary>
    public required string Location { get; init; }

    /// <summary>
    /// Sample of the detected content (may be redacted for security).
    /// </summary>
    public required string Sample { get; init; }

    /// <summary>
    /// Confidence level that this is actually PHI (0-1).
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// HIPAA Safe Harbor identifier category (1-18).
    /// </summary>
    public int? HipaaCategory { get; init; }
}

/// <summary>
/// Statistics about PHI detection operation.
/// </summary>
public record PhiDetectionStatistics
{
    /// <summary>
    /// Total number of fields scanned.
    /// </summary>
    public required int TotalFieldsScanned { get; init; }

    /// <summary>
    /// Number of potential PHI items found.
    /// </summary>
    public required int PotentialPhiFound { get; init; }

    /// <summary>
    /// Breakdown by identifier type.
    /// </summary>
    public required IReadOnlyDictionary<IdentifierType, int> PhiByType { get; init; }

    /// <summary>
    /// Time taken for PHI detection.
    /// </summary>
    public required TimeSpan ScanTime { get; init; }
}

/// <summary>
/// Result of re-identification risk assessment for statistical methods.
/// </summary>
public record RiskAssessmentResult
{
    /// <summary>
    /// Estimated re-identification risk (0-1).
    /// Must be "very small" (<0.04) for HIPAA Expert Determination.
    /// </summary>
    public required double ReIdentificationRisk { get; init; }

    /// <summary>
    /// K-anonymity score (minimum across all equivalence classes).
    /// </summary>
    public required int KAnonymityScore { get; init; }

    /// <summary>
    /// L-diversity score (minimum across all sensitive attributes).
    /// </summary>
    public required int LDiversityScore { get; init; }

    /// <summary>
    /// Number of equivalence classes found.
    /// </summary>
    public required int EquivalenceClasses { get; init; }

    /// <summary>
    /// Whether the risk level meets HIPAA Expert Determination requirements.
    /// </summary>
    public bool MeetsExpertDetermination => ReIdentificationRisk < 0.04;

    /// <summary>
    /// Detailed risk analysis by quasi-identifier combination.
    /// </summary>
    public required IReadOnlyList<EquivalenceClassAnalysis> ClassAnalyses { get; init; }
}

/// <summary>
/// Analysis of a specific equivalence class for risk assessment.
/// </summary>
public record EquivalenceClassAnalysis
{
    /// <summary>
    /// Quasi-identifier values that define this class.
    /// </summary>
    public required IReadOnlyDictionary<string, string> QuasiIdentifiers { get; init; }

    /// <summary>
    /// Number of records in this equivalence class.
    /// </summary>
    public required int RecordCount { get; init; }

    /// <summary>
    /// Number of distinct sensitive attribute values.
    /// </summary>
    public required int SensitiveValueCount { get; init; }

    /// <summary>
    /// Re-identification risk for this specific class.
    /// </summary>
    public required double ClassRisk { get; init; }
}

/// <summary>
/// Supported formats for compliance reports.
/// </summary>
public enum ReportFormat
{
    Json,
    Html,
    Pdf,
    Xml,
    Csv
}

/// <summary>
/// Unit type for operations that don't return a value.
/// </summary>
public record Unit
{
    public static readonly Unit Instance = new();
    private Unit() { }
}