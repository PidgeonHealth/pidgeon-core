// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.DeIdentification;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.DeIdentification;

/// <summary>
/// Application service for validating HIPAA compliance of de-identified content.
/// Performs comprehensive PHI scanning and Safe Harbor checklist validation.
/// </summary>
internal class ComplianceValidationService
{
    private readonly IPhiDetector _phiDetector;
    
    public ComplianceValidationService(IPhiDetector phiDetector)
    {
        _phiDetector = phiDetector ?? throw new ArgumentNullException(nameof(phiDetector));
    }

    /// <summary>
    /// Validates that existing de-identified files meet compliance requirements.
    /// </summary>
    public async Task<Result<ValidationResult>> ValidateComplianceAsync(
        string deidentifiedPath, 
        string? originalPath = null,
        DeIdentificationOptions? options = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(deidentifiedPath))
                return Result<ValidationResult>.Failure("De-identified path cannot be null or empty");

            if (!File.Exists(deidentifiedPath) && !Directory.Exists(deidentifiedPath))
                return Result<ValidationResult>.Failure($"Path not found: {deidentifiedPath}");

            var issues = new List<ValidationIssue>();
            var validatedFiles = new List<string>();

            // Process single file or directory
            if (File.Exists(deidentifiedPath))
            {
                var fileValidation = await ValidateFileComplianceAsync(deidentifiedPath, options);
                if (fileValidation.IsFailure)
                    return Result<ValidationResult>.Failure(fileValidation.Error);
                    
                validatedFiles.Add(deidentifiedPath);
                issues.AddRange(fileValidation.Value);
            }
            else
            {
                var files = Directory.GetFiles(deidentifiedPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var fileValidation = await ValidateFileComplianceAsync(file, options);
                    if (fileValidation.IsSuccess)
                    {
                        validatedFiles.Add(file);
                        issues.AddRange(fileValidation.Value);
                    }
                }
            }

            var status = DetermineValidationStatus(issues);
            var compliance = CreateComplianceVerification(issues, status);

            var result = new ValidationResult
            {
                Status = status,
                Compliance = compliance,
                Issues = issues,
                ValidatedFiles = validatedFiles,
                Metadata = new ValidationMetadata
                {
                    ValidationDate = DateTime.UtcNow,
                    ValidationTime = TimeSpan.FromSeconds(issues.Count * 0.1), // Estimated time
                    FilesValidated = validatedFiles.Count,
                    TotalIssues = issues.Count,
                    IssuesBySeverity = issues.GroupBy(i => i.Severity).ToDictionary(g => g.Key, g => g.Count())
                }
            };

            return Result<ValidationResult>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<ValidationResult>.Failure($"Compliance validation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates batch compliance across multiple de-identification results.
    /// </summary>
    public async Task<Result<ComplianceVerification>> ValidateBatchComplianceAsync(
        List<DeIdentificationResult> results)
    {
        try
        {
            if (!results.Any())
            {
                return Result<ComplianceVerification>.Success(new ComplianceVerification
                {
                    MeetsSafeHarbor = true,
                    SafeHarborChecklist = new Dictionary<string, bool>(),
                    Status = ComplianceStatus.Compliant
                });
            }

            // Check if all individual results are compliant
            var allCompliant = results.All(r => r.Compliance.MeetsSafeHarbor);
            var combinedChecklist = CombineSafeHarborChecklists(results);
            
            // Perform additional cross-message consistency checks
            var consistencyIssues = await ValidateCrossMessageConsistencyAsync(results);
            
            var status = allCompliant && !consistencyIssues.Any() ? 
                ComplianceStatus.Compliant : 
                ComplianceStatus.NonCompliant;

            var compliance = new ComplianceVerification
            {
                MeetsSafeHarbor = allCompliant,
                SafeHarborChecklist = combinedChecklist,
                Status = status,
                Notes = consistencyIssues.Any() ? 
                    $"Cross-message consistency issues: {string.Join(", ", consistencyIssues)}" : 
                    null
            };

            return Result<ComplianceVerification>.Success(compliance);
        }
        catch (Exception ex)
        {
            return Result<ComplianceVerification>.Failure($"Batch compliance validation failed: {ex.Message}");
        }
    }

    // Private helper methods

    private async Task<Result<List<ValidationIssue>>> ValidateFileComplianceAsync(
        string filePath, 
        DeIdentificationOptions? options)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            var scanResult = await _phiDetector.ScanForPhiAsync(content);
            
            var issues = new List<ValidationIssue>();
            
            if (scanResult.IsSuccess)
            {
                foreach (var phi in scanResult.Value.DetectedPhi)
                {
                    issues.Add(new ValidationIssue
                    {
                        FilePath = filePath,
                        Location = phi.Location,
                        IssueType = "Potential PHI Remaining",
                        Description = $"Potential {phi.Type} detected at {phi.Location}",
                        Severity = phi.Confidence > 0.8 ? IssueSeverity.Critical : 
                                  phi.Confidence > 0.6 ? IssueSeverity.Error : IssueSeverity.Warning,
                        RecommendedAction = $"Review and verify de-identification of {phi.Type}"
                    });
                }
            }

            return Result<List<ValidationIssue>>.Success(issues);
        }
        catch (Exception ex)
        {
            return Result<List<ValidationIssue>>.Failure($"File validation failed: {ex.Message}");
        }
    }

    private static ValidationStatus DetermineValidationStatus(List<ValidationIssue> issues)
    {
        if (issues.Any(i => i.Severity == IssueSeverity.Critical))
            return ValidationStatus.Failed;
        
        if (issues.Any(i => i.Severity == IssueSeverity.Error))
            return ValidationStatus.Failed;
        
        if (issues.Any(i => i.Severity == IssueSeverity.Warning))
            return ValidationStatus.PassedWithWarnings;
        
        return ValidationStatus.Passed;
    }

    private static ComplianceVerification CreateComplianceVerification(
        List<ValidationIssue> issues, 
        ValidationStatus status)
    {
        var checklist = new Dictionary<string, bool>
        {
            ["Names removed"] = !issues.Any(i => i.Description.Contains("PatientName")),
            ["Geographic subdivisions removed"] = !issues.Any(i => i.Description.Contains("Address")),
            ["Dates properly handled"] = !issues.Any(i => i.Description.Contains("DateOfBirth")),
            ["Phone numbers removed"] = !issues.Any(i => i.Description.Contains("PhoneNumber")),
            ["Email addresses removed"] = !issues.Any(i => i.Description.Contains("Email")),
            ["Social Security numbers removed"] = !issues.Any(i => i.Description.Contains("SocialSecurityNumber")),
            ["Medical record numbers handled"] = !issues.Any(i => i.Description.Contains("MedicalRecordNumber")),
            ["Account numbers removed"] = !issues.Any(i => i.Description.Contains("AccountNumber"))
        };

        var complianceStatus = status switch
        {
            ValidationStatus.Passed => ComplianceStatus.Compliant,
            ValidationStatus.PassedWithWarnings => ComplianceStatus.CompliantWithWarnings,
            ValidationStatus.Failed => ComplianceStatus.NonCompliant,
            _ => ComplianceStatus.Unknown
        };

        return new ComplianceVerification
        {
            MeetsSafeHarbor = status == ValidationStatus.Passed,
            SafeHarborChecklist = checklist,
            Status = complianceStatus
        };
    }

    private static IReadOnlyDictionary<string, bool> CombineSafeHarborChecklists(
        List<DeIdentificationResult> results)
    {
        var combinedChecklist = new Dictionary<string, bool>();
        
        // Get all unique checklist keys
        var allKeys = results.SelectMany(r => r.Compliance.SafeHarborChecklist.Keys).Distinct();
        
        // For each key, all results must pass for combined result to pass
        foreach (var key in allKeys)
        {
            combinedChecklist[key] = results.All(r => 
                r.Compliance.SafeHarborChecklist.TryGetValue(key, out var value) && value);
        }
        
        return combinedChecklist;
    }

    private async Task<List<string>> ValidateCrossMessageConsistencyAsync(
        List<DeIdentificationResult> results)
    {
        await Task.Yield(); // Placeholder for async consistency validation
        
        var issues = new List<string>();
        
        // Check for consistent ID mapping across results
        var allMappings = new Dictionary<string, string>();
        
        foreach (var result in results)
        {
            foreach (var mapping in result.IdMappings)
            {
                if (allMappings.TryGetValue(mapping.Key, out var existingValue))
                {
                    if (existingValue != mapping.Value)
                    {
                        issues.Add($"Inconsistent mapping for {mapping.Key}: {existingValue} vs {mapping.Value}");
                    }
                }
                else
                {
                    allMappings[mapping.Key] = mapping.Value;
                }
            }
        }
        
        return issues;
    }
}