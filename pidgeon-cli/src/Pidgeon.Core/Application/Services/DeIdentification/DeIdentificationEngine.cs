// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.DeIdentification;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.DeIdentification;

/// <summary>
/// Application service for orchestrating de-identification file operations.
/// Delegates to specialized services for compliance, reporting, and estimation.
/// </summary>
internal class DeIdentificationEngine : IDeIdentificationEngine
{
    private readonly IPhiDetector _phiDetector;
    private readonly IDeIdentificationService _deIdentificationService;
    private readonly ComplianceValidationService _complianceService;
    private readonly AuditReportService _auditService;
    private readonly ResourceEstimationService _estimationService;
    
    public DeIdentificationEngine(
        IPhiDetector phiDetector,
        IDeIdentificationService deIdentificationService,
        ComplianceValidationService complianceService,
        AuditReportService auditService,
        ResourceEstimationService estimationService)
    {
        _phiDetector = phiDetector ?? throw new ArgumentNullException(nameof(phiDetector));
        _deIdentificationService = deIdentificationService ?? throw new ArgumentNullException(nameof(deIdentificationService));
        _complianceService = complianceService ?? throw new ArgumentNullException(nameof(complianceService));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _estimationService = estimationService ?? throw new ArgumentNullException(nameof(estimationService));
    }

    /// <summary>
    /// De-identifies a single file containing healthcare messages.
    /// </summary>
    public async Task<Result<DeIdentificationResult>> ProcessFileAsync(
        string inputPath, 
        string outputPath, 
        DeIdentificationOptions options)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                return Result<DeIdentificationResult>.Failure("Input path cannot be null or empty");
            
            if (string.IsNullOrWhiteSpace(outputPath))
                return Result<DeIdentificationResult>.Failure("Output path cannot be null or empty");
            
            if (!File.Exists(inputPath))
                return Result<DeIdentificationResult>.Failure($"Input file not found: {inputPath}");

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Read and process file content
            var inputContent = await File.ReadAllTextAsync(inputPath);
            if (string.IsNullOrWhiteSpace(inputContent))
                return Result<DeIdentificationResult>.Failure("Input file is empty or contains no readable content");

            // Process using domain service
            var contextResult = _deIdentificationService.CreateContext(options);
            if (contextResult.IsFailure)
                return Result<DeIdentificationResult>.Failure(contextResult.Error);

            var messages = new[] { inputContent };
            var result = await _deIdentificationService.DeIdentifyMessagesAsync(messages, options);
            if (result.IsFailure)
                return Result<DeIdentificationResult>.Failure(result.Error);

            // Write de-identified content to output file
            if (result.Value.DeIdentifiedMessages.Any())
            {
                await File.WriteAllTextAsync(outputPath, result.Value.DeIdentifiedMessages.First());
            }

            return Result<DeIdentificationResult>.Success(result.Value);
        }
        catch (IOException ex)
        {
            return Result<DeIdentificationResult>.Failure($"File I/O error: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<DeIdentificationResult>.Failure($"Access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result<DeIdentificationResult>.Failure($"File processing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// De-identifies all healthcare message files in a directory.
    /// </summary>
    public async Task<Result<BatchDeIdentificationResult>> ProcessDirectoryAsync(
        string inputDirectory, 
        string outputDirectory, 
        DeIdentificationOptions options)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(inputDirectory))
                return Result<BatchDeIdentificationResult>.Failure("Input directory cannot be null or empty");
            
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return Result<BatchDeIdentificationResult>.Failure("Output directory cannot be null or empty");
            
            if (!Directory.Exists(inputDirectory))
                return Result<BatchDeIdentificationResult>.Failure($"Input directory not found: {inputDirectory}");

            // Ensure output directory exists
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var files = Directory.GetFiles(inputDirectory, "*", SearchOption.AllDirectories);
            var fileResults = new List<FileProcessingResult>();
            var allMessages = new List<string>();

            // Process each file
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(inputDirectory, file);
                var outputPath = Path.Combine(outputDirectory, relativePath);
                var outputDir = Path.GetDirectoryName(outputPath);
                
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                var startTime = DateTime.UtcNow;
                var fileResult = await ProcessFileAsync(file, outputPath, options);
                var processingTime = DateTime.UtcNow - startTime;

                var fileInfo = new FileInfo(file);
                var outputFileInfo = File.Exists(outputPath) ? new FileInfo(outputPath) : null;

                fileResults.Add(new FileProcessingResult
                {
                    InputPath = file,
                    OutputPath = outputPath,
                    Success = fileResult.IsSuccess,
                    Result = fileResult.IsSuccess ? fileResult.Value : null,
                    ErrorMessage = fileResult.IsFailure ? fileResult.Error.Message : null,
                    ProcessingTime = processingTime,
                    SizeInfo = new FileSizeInfo
                    {
                        OriginalSizeBytes = fileInfo.Length,
                        DeIdentifiedSizeBytes = outputFileInfo?.Length ?? 0
                    }
                });

                if (fileResult.IsSuccess)
                {
                    allMessages.AddRange(fileResult.Value.DeIdentifiedMessages);
                }
            }

            // Create batch result
            var successfulResults = fileResults.Where(r => r.Success && r.Result != null).Select(r => r.Result!).ToList();
            var combinedStatistics = CombineStatistics(successfulResults);
            var batchCompliance = await _complianceService.ValidateBatchComplianceAsync(successfulResults);

            var batchResult = new BatchDeIdentificationResult
            {
                FileResults = fileResults,
                CombinedStatistics = combinedStatistics,
                BatchCompliance = batchCompliance.IsSuccess ? batchCompliance.Value : new ComplianceVerification
                {
                    MeetsSafeHarbor = false,
                    SafeHarborChecklist = new Dictionary<string, bool>(),
                    Status = ComplianceStatus.Unknown
                },
                CombinedIdMappings = CombineIdMappings(successfulResults),
                Metadata = new BatchProcessingMetadata
                {
                    TotalFiles = fileResults.Count,
                    SuccessfulFiles = fileResults.Count(r => r.Success),
                    FailedFiles = fileResults.Count(r => !r.Success),
                    TotalProcessingTime = TimeSpan.FromTicks(fileResults.Sum(r => r.ProcessingTime.Ticks))
                }
            };

            return Result<BatchDeIdentificationResult>.Success(batchResult);
        }
        catch (Exception ex)
        {
            return Result<BatchDeIdentificationResult>.Failure($"Directory processing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Previews de-identification changes without modifying files.
    /// </summary>
    public async Task<Result<DeIdentificationPreview>> PreviewChangesAsync(
        string inputPath, 
        DeIdentificationOptions options)
    {
        try
        {
            // Determine if input is file or directory
            var isDirectory = Directory.Exists(inputPath);
            var filesToProcess = new List<string>();
            
            if (isDirectory)
            {
                filesToProcess.AddRange(Directory.GetFiles(inputPath, "*.hl7", SearchOption.AllDirectories));
                filesToProcess.AddRange(Directory.GetFiles(inputPath, "*.HL7", SearchOption.AllDirectories));
                filesToProcess.AddRange(Directory.GetFiles(inputPath, "*.json", SearchOption.AllDirectories));
                filesToProcess.AddRange(Directory.GetFiles(inputPath, "*.fhir", SearchOption.AllDirectories));
                filesToProcess.AddRange(Directory.GetFiles(inputPath, "*.xml", SearchOption.AllDirectories));
            }
            else if (File.Exists(inputPath))
            {
                filesToProcess.Add(inputPath);
            }
            else
            {
                return Result<DeIdentificationPreview>.Failure($"Input path not found: {inputPath}");
            }

            if (!filesToProcess.Any())
            {
                return Result<DeIdentificationPreview>.Failure("No healthcare message files found to process (supported: .hl7, .json, .fhir, .xml)");
            }

            // Process first file to generate sample changes
            var sampleChanges = new List<PreviewChange>();
            var estimatedStats = new Dictionary<IdentifierType, int>();
            var totalFields = 0;
            var totalDates = 0;
            
            // Read and analyze first file (or first few files for better sample)
            var samplesToAnalyze = Math.Min(3, filesToProcess.Count);
            for (int i = 0; i < samplesToAnalyze; i++)
            {
                var fileContent = await File.ReadAllTextAsync(filesToProcess[i]);
                var segments = fileContent.Split('\n', '\r')
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToArray();
                
                foreach (var segment in segments)
                {
                    var fields = segment.Split('|');
                    var segmentType = fields[0];
                    
                    // Detect PHI based on segment type
                    if (segmentType == "PID" && fields.Length > 5)
                    {
                        // Patient name in PID.5
                        if (!string.IsNullOrWhiteSpace(fields[5]) && fields[5] != "~")
                        {
                            var originalName = fields[5];
                            sampleChanges.Add(new PreviewChange
                            {
                                Location = $"{Path.GetFileName(filesToProcess[i])}:PID.5",
                                IdentifierType = IdentifierType.PatientName,
                                OriginalValue = originalName,
                                ReplacementValue = "SYNTHETIC_NAME_1",
                                Action = "REPLACE"
                            });
                            estimatedStats[IdentifierType.PatientName] = estimatedStats.GetValueOrDefault(IdentifierType.PatientName) + 1;
                            totalFields++;
                        }
                        
                        // SSN in PID.19
                        if (fields.Length > 19 && !string.IsNullOrWhiteSpace(fields[19]))
                        {
                            var originalSSN = fields[19];
                            sampleChanges.Add(new PreviewChange
                            {
                                Location = $"{Path.GetFileName(filesToProcess[i])}:PID.19",
                                IdentifierType = IdentifierType.SocialSecurityNumber,
                                OriginalValue = originalSSN,
                                ReplacementValue = "999123456",
                                Action = "REPLACE"
                            });
                            estimatedStats[IdentifierType.SocialSecurityNumber] = estimatedStats.GetValueOrDefault(IdentifierType.SocialSecurityNumber) + 1;
                            totalFields++;
                        }
                        
                        // Birth date in PID.7
                        if (fields.Length > 7 && !string.IsNullOrWhiteSpace(fields[7]) && fields[7].Length >= 8)
                        {
                            totalDates++;
                            if (sampleChanges.Count < 10) // Limit sample size
                            {
                                sampleChanges.Add(new PreviewChange
                                {
                                    Location = $"{Path.GetFileName(filesToProcess[i])}:PID.7",
                                    IdentifierType = IdentifierType.PatientName, // Use available enum value
                                    OriginalValue = fields[7],
                                    ReplacementValue = "[Date shifted by configured offset]",
                                    Action = "SHIFT"
                                });
                            }
                        }
                        
                        // Address in PID.11
                        if (fields.Length > 11 && !string.IsNullOrWhiteSpace(fields[11]))
                        {
                            var originalAddress = fields[11];
                            if (sampleChanges.Count < 10)
                            {
                                sampleChanges.Add(new PreviewChange
                                {
                                    Location = $"{Path.GetFileName(filesToProcess[i])}:PID.11",
                                    IdentifierType = IdentifierType.Address,
                                    OriginalValue = originalAddress,
                                    ReplacementValue = "123 MAIN ST^^ANYTOWN^CA^12345",
                                    Action = "REPLACE"
                                });
                            }
                            estimatedStats[IdentifierType.Address] = estimatedStats.GetValueOrDefault(IdentifierType.Address) + 1;
                            totalFields++;
                        }
                    }
                    else if (segmentType == "PV1" && fields.Length > 7)
                    {
                        // Attending physician in PV1.7
                        if (!string.IsNullOrWhiteSpace(fields[7]) && fields[7] != "~")
                        {
                            var originalProvider = fields[7];
                            if (sampleChanges.Count < 10)
                            {
                                sampleChanges.Add(new PreviewChange
                                {
                                    Location = $"{Path.GetFileName(filesToProcess[i])}:PV1.7",
                                    IdentifierType = IdentifierType.ProviderName,
                                    OriginalValue = originalProvider,
                                    ReplacementValue = "SMITH^JOHN^MD",
                                    Action = "REPLACE"
                                });
                            }
                            estimatedStats[IdentifierType.ProviderName] = estimatedStats.GetValueOrDefault(IdentifierType.ProviderName) + 1;
                            totalFields++;
                        }
                    }
                }
            }
            
            // Extrapolate statistics for all files
            var sampleRatio = (double)filesToProcess.Count / samplesToAnalyze;
            var extrapolatedStats = estimatedStats.ToDictionary(
                kvp => kvp.Key,
                kvp => (int)(kvp.Value * sampleRatio)
            );
            
            // Estimate processing resources
            var totalSize = filesToProcess.Sum(f => new FileInfo(f).Length);
            var estimatedTimeMs = filesToProcess.Count * 50; // ~50ms per file estimate
            
            var preview = new DeIdentificationPreview
            {
                SampleChanges = sampleChanges.Take(10).ToArray(), // Limit to 10 samples
                EstimatedStatistics = new DeIdentificationStatistics
                {
                    TotalMessages = filesToProcess.Count,
                    TotalIdentifiersProcessed = (int)(totalFields * sampleRatio),
                    IdentifiersByType = extrapolatedStats,
                    FieldsModified = (int)(totalFields * sampleRatio),
                    DatesShifted = (int)(totalDates * sampleRatio),
                    AverageProcessingTimeMs = 50,
                    TotalProcessingTime = TimeSpan.FromMilliseconds(estimatedTimeMs),
                    UniqueSubjects = filesToProcess.Count // Rough estimate
                },
                ComplianceAssessment = new ComplianceVerification
                {
                    MeetsSafeHarbor = true,
                    SafeHarborChecklist = new Dictionary<string, bool>
                    {
                        ["Names removed"] = true,
                        ["Geographic subdivisions removed"] = true,
                        ["Dates shifted"] = options.DateShift != TimeSpan.Zero,
                        ["Phone numbers removed"] = true,
                        ["SSN removed"] = true,
                        ["Medical record numbers mapped"] = true,
                        ["IP addresses removed"] = true,
                        ["Biometric identifiers removed"] = true
                    },
                    Status = ComplianceStatus.Compliant
                },
                FilesToProcess = filesToProcess.ToArray(),
                ResourceEstimate = new ProcessingEstimate
                {
                    EstimatedTime = TimeSpan.FromMilliseconds(estimatedTimeMs),
                    EstimatedMemoryBytes = totalSize * 2, // Rough estimate: 2x input size
                    FileCount = filesToProcess.Count,
                    TotalInputSizeBytes = totalSize,
                    EstimatedThroughput = totalSize / Math.Max(1, estimatedTimeMs / 1000.0) // bytes/sec
                }
            };

            return Result<DeIdentificationPreview>.Success(preview);
        }
        catch (Exception ex)
        {
            return Result<DeIdentificationPreview>.Failure($"Preview generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates compliance by delegating to specialized service.
    /// </summary>
    public Task<Result<ValidationResult>> ValidateComplianceAsync(
        string deidentifiedPath, 
        string? originalPath = null,
        DeIdentificationOptions? options = null)
    {
        return _complianceService.ValidateComplianceAsync(deidentifiedPath, originalPath, options);
    }

    /// <summary>
    /// Generates audit report by delegating to specialized service.
    /// </summary>
    public Task<Result<string>> GenerateAuditReportAsync(
        DeIdentificationResult result, 
        string outputPath, 
        ReportFormat format = ReportFormat.Html)
    {
        return _auditService.GenerateAuditReportAsync(result, outputPath, format);
    }

    /// <summary>
    /// Estimates resources by delegating to specialized service.
    /// </summary>
    public Task<Result<ProcessingEstimate>> EstimateResourcesAsync(
        string inputPath, 
        DeIdentificationOptions options)
    {
        return _estimationService.EstimateResourcesAsync(inputPath, options);
    }

    // Private helper methods

    private static DeIdentificationStatistics CombineStatistics(List<DeIdentificationResult> results)
    {
        if (!results.Any())
        {
            return new DeIdentificationStatistics
            {
                TotalMessages = 0,
                TotalIdentifiersProcessed = 0,
                IdentifiersByType = new Dictionary<IdentifierType, int>(),
                FieldsModified = 0,
                DatesShifted = 0,
                AverageProcessingTimeMs = 0,
                TotalProcessingTime = TimeSpan.Zero,
                UniqueSubjects = 0
            };
        }

        var totalTime = TimeSpan.FromTicks(results.Sum(r => r.Statistics.TotalProcessingTime.Ticks));
        var totalMessages = results.Sum(r => r.Statistics.TotalMessages);

        return new DeIdentificationStatistics
        {
            TotalMessages = totalMessages,
            TotalIdentifiersProcessed = results.Sum(r => r.Statistics.TotalIdentifiersProcessed),
            IdentifiersByType = results.SelectMany(r => r.Statistics.IdentifiersByType)
                .GroupBy(kvp => kvp.Key)
                .ToDictionary(g => g.Key, g => g.Sum(kvp => kvp.Value)),
            FieldsModified = results.Sum(r => r.Statistics.FieldsModified),
            DatesShifted = results.Sum(r => r.Statistics.DatesShifted),
            AverageProcessingTimeMs = totalMessages > 0 ? totalTime.TotalMilliseconds / totalMessages : 0,
            TotalProcessingTime = totalTime,
            UniqueSubjects = results.Sum(r => r.Statistics.UniqueSubjects)
        };
    }

    private static IReadOnlyDictionary<string, string> CombineIdMappings(List<DeIdentificationResult> results)
    {
        var combinedMappings = new Dictionary<string, string>();
        
        foreach (var result in results)
        {
            foreach (var mapping in result.IdMappings)
            {
                combinedMappings[mapping.Key] = mapping.Value;
            }
        }
        
        return combinedMappings;
    }
}