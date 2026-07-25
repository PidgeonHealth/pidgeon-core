// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.DeIdentification;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.DeIdentification;

/// <summary>
/// Application service for orchestrating de-identification operations.
/// Handles file I/O, batch processing, and coordinates domain services.
/// </summary>
public interface IDeIdentificationEngine
{
    /// <summary>
    /// De-identifies a single file containing healthcare messages.
    /// Supports both single message files and multi-message files.
    /// </summary>
    /// <param name="inputPath">Path to input file containing healthcare messages</param>
    /// <param name="outputPath">Path where de-identified messages should be saved</param>
    /// <param name="options">De-identification configuration options</param>
    /// <returns>Complete de-identification result with statistics and compliance</returns>
    Task<Result<DeIdentificationResult>> ProcessFileAsync(
        string inputPath, 
        string outputPath, 
        DeIdentificationOptions options);

    /// <summary>
    /// De-identifies all healthcare message files in a directory.
    /// Maintains relationships between files and generates batch reports.
    /// </summary>
    /// <param name="inputDirectory">Directory containing healthcare message files</param>
    /// <param name="outputDirectory">Directory where de-identified files should be saved</param>
    /// <param name="options">De-identification configuration options</param>
    /// <returns>Batch processing result with combined statistics</returns>
    Task<Result<BatchDeIdentificationResult>> ProcessDirectoryAsync(
        string inputDirectory, 
        string outputDirectory, 
        DeIdentificationOptions options);

    /// <summary>
    /// Previews de-identification changes without modifying files.
    /// Shows what would be changed for user review and confirmation.
    /// </summary>
    /// <param name="inputPath">Path to input file or directory to preview</param>
    /// <param name="options">De-identification configuration options</param>
    /// <returns>Preview result showing proposed changes</returns>
    Task<Result<DeIdentificationPreview>> PreviewChangesAsync(
        string inputPath, 
        DeIdentificationOptions options);

    /// <summary>
    /// Validates that existing de-identified files meet compliance requirements.
    /// Useful for auditing previously processed files.
    /// </summary>
    /// <param name="deidentifiedPath">Path to de-identified files to validate</param>
    /// <param name="originalPath">Optional path to original files for comparison</param>
    /// <param name="options">Validation configuration options</param>
    /// <returns>Validation results with compliance assessment</returns>
    Task<Result<ValidationResult>> ValidateComplianceAsync(
        string deidentifiedPath, 
        string? originalPath = null,
        DeIdentificationOptions? options = null);

    /// <summary>
    /// Generates comprehensive audit reports for compliance documentation.
    /// Creates detailed reports showing all de-identification actions taken.
    /// </summary>
    /// <param name="result">De-identification result to generate report for</param>
    /// <param name="outputPath">Path where report should be saved</param>
    /// <param name="format">Report format (HTML, PDF, JSON, etc.)</param>
    /// <returns>Path to generated report file</returns>
    Task<Result<string>> GenerateAuditReportAsync(
        DeIdentificationResult result, 
        string outputPath, 
        ReportFormat format = ReportFormat.Html);

    /// <summary>
    /// Estimates processing time and resource requirements for a batch job.
    /// Helps with planning and resource allocation for large datasets.
    /// </summary>
    /// <param name="inputPath">Path to files that would be processed</param>
    /// <param name="options">De-identification options to be used</param>
    /// <returns>Resource estimation including time and memory requirements</returns>
    Task<Result<ProcessingEstimate>> EstimateResourcesAsync(
        string inputPath, 
        DeIdentificationOptions options);
}

/// <summary>
/// Result of batch de-identification processing across multiple files.
/// </summary>
public record BatchDeIdentificationResult
{
    /// <summary>
    /// Individual file processing results.
    /// </summary>
    public required IReadOnlyList<FileProcessingResult> FileResults { get; init; }

    /// <summary>
    /// Combined statistics across all processed files.
    /// </summary>
    public required DeIdentificationStatistics CombinedStatistics { get; init; }

    /// <summary>
    /// Overall compliance status for the entire batch.
    /// </summary>
    public required ComplianceVerification BatchCompliance { get; init; }

    /// <summary>
    /// Combined ID mappings from all files for consistency.
    /// </summary>
    public required IReadOnlyDictionary<string, string> CombinedIdMappings { get; init; }

    /// <summary>
    /// Processing metadata for the batch operation.
    /// </summary>
    public required BatchProcessingMetadata Metadata { get; init; }

    /// <summary>
    /// Files that failed processing with error details.
    /// </summary>
    public IReadOnlyList<ProcessingError> FailedFiles { get; init; } = Array.Empty<ProcessingError>();

    /// <summary>
    /// Overall success rate (successfully processed files / total files).
    /// </summary>
    public double SuccessRate => FileResults.Count == 0 ? 0.0 : 
        FileResults.Count(r => r.Success) / (double)FileResults.Count;
}

/// <summary>
/// Result of processing a single file within a batch.
/// </summary>
public record FileProcessingResult
{
    /// <summary>
    /// Path to the input file that was processed.
    /// </summary>
    public required string InputPath { get; init; }

    /// <summary>
    /// Path to the output file that was created.
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// Whether processing succeeded for this file.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// De-identification result if processing succeeded.
    /// </summary>
    public DeIdentificationResult? Result { get; init; }

    /// <summary>
    /// Error details if processing failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Processing time for this specific file.
    /// </summary>
    public required TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// File size information.
    /// </summary>
    public required FileSizeInfo SizeInfo { get; init; }
}

/// <summary>
/// Information about file sizes before and after processing.
/// </summary>
public record FileSizeInfo
{
    /// <summary>
    /// Original file size in bytes.
    /// </summary>
    public required long OriginalSizeBytes { get; init; }

    /// <summary>
    /// De-identified file size in bytes.
    /// </summary>
    public required long DeIdentifiedSizeBytes { get; init; }

    /// <summary>
    /// Size change ratio (negative means file got smaller).
    /// </summary>
    public double SizeChangeRatio => OriginalSizeBytes == 0 ? 0.0 : 
        (DeIdentifiedSizeBytes - OriginalSizeBytes) / (double)OriginalSizeBytes;
}

/// <summary>
/// Metadata for batch processing operations.
/// </summary>
public record BatchProcessingMetadata
{
    /// <summary>
    /// Total number of files processed.
    /// </summary>
    public required int TotalFiles { get; init; }

    /// <summary>
    /// Number of files successfully processed.
    /// </summary>
    public required int SuccessfulFiles { get; init; }

    /// <summary>
    /// Number of files that failed processing.
    /// </summary>
    public required int FailedFiles { get; init; }

    /// <summary>
    /// Total processing time for entire batch.
    /// </summary>
    public required TimeSpan TotalProcessingTime { get; init; }

    /// <summary>
    /// Average processing time per file.
    /// </summary>
    public TimeSpan AverageProcessingTime => TotalFiles == 0 ? TimeSpan.Zero : 
        TimeSpan.FromTicks(TotalProcessingTime.Ticks / TotalFiles);

    /// <summary>
    /// Peak memory usage during batch processing.
    /// </summary>
    public long? PeakMemoryUsageBytes { get; init; }

    /// <summary>
    /// Processing throughput (files per second).
    /// </summary>
    public double Throughput => TotalProcessingTime.TotalSeconds == 0 ? 0.0 : 
        TotalFiles / TotalProcessingTime.TotalSeconds;
}

/// <summary>
/// Preview of changes that would be made during de-identification.
/// </summary>
public record DeIdentificationPreview
{
    /// <summary>
    /// Sample of changes that would be made.
    /// Shows before/after for representative fields.
    /// </summary>
    public required IReadOnlyList<PreviewChange> SampleChanges { get; init; }

    /// <summary>
    /// Estimated statistics for the full processing operation.
    /// </summary>
    public required DeIdentificationStatistics EstimatedStatistics { get; init; }

    /// <summary>
    /// Compliance assessment for the proposed changes.
    /// </summary>
    public required ComplianceVerification ComplianceAssessment { get; init; }

    /// <summary>
    /// Files that would be processed.
    /// </summary>
    public required IReadOnlyList<string> FilesToProcess { get; init; }

    /// <summary>
    /// Estimated processing time and resources.
    /// </summary>
    public required ProcessingEstimate ResourceEstimate { get; init; }
}

/// <summary>
/// Individual change that would be made during de-identification.
/// </summary>
public record PreviewChange
{
    /// <summary>
    /// File and field location of the change.
    /// </summary>
    public required string Location { get; init; }

    /// <summary>
    /// Type of identifier being changed.
    /// </summary>
    public required IdentifierType IdentifierType { get; init; }

    /// <summary>
    /// Original value (may be redacted for security).
    /// </summary>
    public required string OriginalValue { get; init; }

    /// <summary>
    /// Proposed replacement value.
    /// </summary>
    public required string ReplacementValue { get; init; }

    /// <summary>
    /// Action to be taken (REPLACE, REMOVE, SHIFT).
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// HIPAA Safe Harbor category for this identifier.
    /// </summary>
    public int? HipaaCategory { get; init; }
}

/// <summary>
/// Result of compliance validation for existing de-identified files.
/// </summary>
public record ValidationResult
{
    /// <summary>
    /// Overall validation status.
    /// </summary>
    public required ValidationStatus Status { get; init; }

    /// <summary>
    /// Compliance verification results.
    /// </summary>
    public required ComplianceVerification Compliance { get; init; }

    /// <summary>
    /// Issues found during validation.
    /// </summary>
    public required IReadOnlyList<ValidationIssue> Issues { get; init; }

    /// <summary>
    /// Files that were validated.
    /// </summary>
    public required IReadOnlyList<string> ValidatedFiles { get; init; }

    /// <summary>
    /// Validation metadata.
    /// </summary>
    public required ValidationMetadata Metadata { get; init; }
}

/// <summary>
/// Individual validation issue found in de-identified files.
/// </summary>
public record ValidationIssue
{
    /// <summary>
    /// File where the issue was found.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Location within the file.
    /// </summary>
    public required string Location { get; init; }

    /// <summary>
    /// Type of validation issue.
    /// </summary>
    public required string IssueType { get; init; }

    /// <summary>
    /// Detailed description of the issue.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Severity of the issue.
    /// </summary>
    public required IssueSeverity Severity { get; init; }

    /// <summary>
    /// Recommended action to resolve the issue.
    /// </summary>
    public string? RecommendedAction { get; init; }
}

/// <summary>
/// Metadata for validation operations.
/// </summary>
public record ValidationMetadata
{
    /// <summary>
    /// When validation was performed.
    /// </summary>
    public required DateTime ValidationDate { get; init; }

    /// <summary>
    /// Time taken for validation.
    /// </summary>
    public required TimeSpan ValidationTime { get; init; }

    /// <summary>
    /// Number of files validated.
    /// </summary>
    public required int FilesValidated { get; init; }

    /// <summary>
    /// Total issues found.
    /// </summary>
    public required int TotalIssues { get; init; }

    /// <summary>
    /// Breakdown of issues by severity.
    /// </summary>
    public required IReadOnlyDictionary<IssueSeverity, int> IssuesBySeverity { get; init; }
}

/// <summary>
/// Estimate of processing resources required for a de-identification job.
/// </summary>
public record ProcessingEstimate
{
    /// <summary>
    /// Estimated total processing time.
    /// </summary>
    public required TimeSpan EstimatedTime { get; init; }

    /// <summary>
    /// Estimated peak memory usage in bytes.
    /// </summary>
    public required long EstimatedMemoryBytes { get; init; }

    /// <summary>
    /// Number of files to be processed.
    /// </summary>
    public required int FileCount { get; init; }

    /// <summary>
    /// Total size of input data in bytes.
    /// </summary>
    public required long TotalInputSizeBytes { get; init; }

    /// <summary>
    /// Estimated messages per second throughput.
    /// </summary>
    public required double EstimatedThroughput { get; init; }

    /// <summary>
    /// Confidence level of the estimate (0-1).
    /// </summary>
    public double EstimateConfidence { get; init; } = 0.8;

    /// <summary>
    /// Recommended batch size for optimal performance.
    /// </summary>
    public int? RecommendedBatchSize { get; init; }
}

/// <summary>
/// Error that occurred during file processing.
/// </summary>
public record ProcessingError
{
    /// <summary>
    /// File that failed to process.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Error message describing what went wrong.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// Exception details if available.
    /// </summary>
    public string? ExceptionDetails { get; init; }

    /// <summary>
    /// Error category for troubleshooting.
    /// </summary>
    public ProcessingErrorType ErrorType { get; init; } = ProcessingErrorType.Unknown;
}

/// <summary>
/// Types of processing errors.
/// </summary>
public enum ProcessingErrorType
{
    FileNotFound,
    AccessDenied,
    InvalidFormat,
    OutOfMemory,
    Timeout,
    ValidationFailure,
    Unknown
}

/// <summary>
/// Overall validation status.
/// </summary>
public enum ValidationStatus
{
    Passed,
    PassedWithWarnings,
    Failed,
    NotValidated
}

/// <summary>
/// Severity levels for validation issues.
/// </summary>
public enum IssueSeverity
{
    Info,
    Warning,
    Error,
    Critical
}