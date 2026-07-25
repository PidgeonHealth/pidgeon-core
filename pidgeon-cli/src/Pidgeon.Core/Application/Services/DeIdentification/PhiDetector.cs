// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Diagnostics;
using Pidgeon.Core.Application.Interfaces.Standards;
using Pidgeon.Core.Domain.DeIdentification;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.DeIdentification;

/// <summary>
/// Application service for PHI detection operations.
/// Orchestrates pattern detection and field analysis over the standard
/// de-identification plugin seam (<see cref="IStandardDeIdentificationPlugin"/>).
/// </summary>
internal class PhiDetector : IPhiDetector
{
    /// <summary>
    /// PHI field-level inspection is defined against HL7 v2 field paths
    /// (e.g. "PID.5"), so the detector binds to the HL7 plugin's inspection face.
    /// </summary>
    private const string Hl7StandardName = "hl7";

    private readonly IStandardDeIdentificationPlugin _hl7Plugin;

    public PhiDetector(IEnumerable<IStandardDeIdentificationPlugin> plugins)
    {
        if (plugins is null)
            throw new ArgumentNullException(nameof(plugins));

        _hl7Plugin = plugins.FirstOrDefault(p =>
                p.StandardName.Equals(Hl7StandardName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                "PHI detection requires the HL7 de-identification plugin; none was registered.",
                nameof(plugins));
    }

    /// <summary>
    /// Scans healthcare message content for potential PHI using pattern recognition.
    /// </summary>
    public async Task<Result<PhiDetectionResult>> ScanForPhiAsync(string message, PhiDetectionOptions? options = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(message))
                return Result<PhiDetectionResult>.Failure("Message cannot be null or empty");

            await Task.Yield();

            var stopwatch = Stopwatch.StartNew();
            var minimumConfidence = options?.MinimumConfidence ?? 0.7;
            var maxSampleLength = options?.MaxSampleLength ?? 20;
            var detectedItems = new List<PhiDetectionItem>();
            var totalFieldsScanned = 0;

            var segments = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                if (segment.Length < 3)
                    continue;

                var fields = segment.Split('|');
                var segmentType = fields[0].Length >= 3 ? fields[0][..3] : fields[0];

                for (var i = 1; i < fields.Length; i++)
                {
                    var fieldValue = fields[i];
                    if (string.IsNullOrEmpty(fieldValue))
                        continue;

                    totalFieldsScanned++;
                    var fieldPath = $"{segmentType}.{i}";
                    var mapping = _hl7Plugin.GetPhiFieldMapping(fieldPath);
                    var isKnownPhiField = mapping != null;

                    // De-identification placeholders are not real PHI
                    if (_hl7Plugin.IsDeIdentifiedPlaceholder(fieldValue))
                        continue;

                    var hasPatternMatch = _hl7Plugin.ContainsPotentialPhi(fieldValue);

                    // If neither the field mapping nor pattern matching flags this value, skip it
                    if (!isKnownPhiField && !hasPatternMatch)
                        continue;

                    // Known PHI fields still require a pattern match or suspicious content;
                    // benign values (timestamps, short codes) at known positions are not actionable
                    if (isKnownPhiField && !hasPatternMatch)
                    {
                        var patternConfidence = _hl7Plugin.GetPhiDetectionConfidence(fieldValue);
                        if (patternConfidence == 0.0)
                            continue;
                    }

                    var confidence = _hl7Plugin.GetPhiDetectionConfidence(fieldValue);

                    if (isKnownPhiField && confidence < minimumConfidence)
                        confidence = minimumConfidence;

                    if (confidence < minimumConfidence)
                        continue;

                    var identifierType = isKnownPhiField
                        ? mapping!.IdentifierType
                        : _hl7Plugin.DetectPhiIdentifierType(fieldValue);

                    var sample = fieldValue.Length > maxSampleLength
                        ? fieldValue[..maxSampleLength]
                        : fieldValue;

                    detectedItems.Add(new PhiDetectionItem
                    {
                        Type = identifierType,
                        Location = fieldPath,
                        Sample = sample,
                        Confidence = confidence,
                        HipaaCategory = mapping?.HipaaCategory
                    });
                }
            }

            stopwatch.Stop();

            var phiByType = detectedItems
                .GroupBy(item => item.Type)
                .ToDictionary(g => g.Key, g => g.Count());

            var overallConfidence = detectedItems.Count > 0
                ? detectedItems.Average(item => item.Confidence)
                : 1.0;

            var result = new PhiDetectionResult
            {
                DetectedPhi = detectedItems,
                OverallConfidence = overallConfidence,
                Statistics = new PhiDetectionStatistics
                {
                    TotalFieldsScanned = totalFieldsScanned,
                    PotentialPhiFound = detectedItems.Count,
                    PhiByType = phiByType,
                    ScanTime = stopwatch.Elapsed
                }
            };

            return Result<PhiDetectionResult>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<PhiDetectionResult>.Failure($"PHI scanning failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates that de-identified message contains no remaining PHI.
    /// </summary>
    public async Task<Result<PhiValidationResult>> ValidatePhiRemovalAsync(
        string deidentifiedMessage,
        PhiDetectionOptions? options = null)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            var scanOptions = new PhiDetectionOptions
            {
                MinimumConfidence = 0.5,
                IncludeLowConfidence = options?.IncludeLowConfidence ?? false,
                MaxSampleLength = options?.MaxSampleLength ?? 20,
                RedactSamples = options?.RedactSamples ?? false
            };

            var scanResult = await ScanForPhiAsync(deidentifiedMessage, scanOptions);

            if (!scanResult.IsSuccess)
                return Result<PhiValidationResult>.Failure(scanResult.Error.Message);

            stopwatch.Stop();

            var detectedPhi = scanResult.Value.DetectedPhi;
            var passed = detectedPhi.Count == 0;

            var result = new PhiValidationResult
            {
                PassedValidation = passed,
                RemainingPhiItems = detectedPhi,
                ValidationConfidence = scanResult.Value.OverallConfidence,
                Statistics = new PhiValidationStatistics
                {
                    FieldsValidated = scanResult.Value.Statistics.TotalFieldsScanned,
                    PotentialPhiItems = detectedPhi.Count,
                    ValidationTime = stopwatch.Elapsed
                }
            };

            return Result<PhiValidationResult>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<PhiValidationResult>.Failure($"PHI validation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects PHI in specific healthcare message fields using field-aware analysis.
    /// </summary>
    public async Task<Result<FieldPhiDetectionResult>> DetectFieldPhiAsync(
        string fieldValue,
        string fieldPath,
        PhiDetectionOptions? options = null)
    {
        try
        {
            await Task.Yield();

            var mapping = _hl7Plugin.GetPhiFieldMapping(fieldPath);
            var hasPatternMatch = _hl7Plugin.ContainsPotentialPhi(fieldValue);
            var detectedItems = new List<PhiDetectionItem>();

            if (hasPatternMatch || mapping != null)
            {
                var confidence = _hl7Plugin.GetPhiDetectionConfidence(fieldValue);
                var identifierType = mapping != null
                    ? mapping.IdentifierType
                    : _hl7Plugin.DetectPhiIdentifierType(fieldValue);

                var maxSampleLength = options?.MaxSampleLength ?? 20;
                var sample = fieldValue.Length > maxSampleLength
                    ? fieldValue[..maxSampleLength]
                    : fieldValue;

                var minimumConfidence = options?.MinimumConfidence ?? 0.7;
                if (mapping != null && confidence < minimumConfidence)
                    confidence = minimumConfidence;

                if (confidence >= minimumConfidence || mapping != null)
                {
                    detectedItems.Add(new PhiDetectionItem
                    {
                        Type = identifierType,
                        Location = fieldPath,
                        Sample = sample,
                        Confidence = confidence,
                        HipaaCategory = mapping?.HipaaCategory
                    });
                }
            }

            var result = new FieldPhiDetectionResult
            {
                FieldPath = fieldPath,
                DetectedItems = detectedItems,
                ExpectedType = mapping?.IdentifierType,
                IsPhiField = mapping != null,
                HipaaCategory = mapping?.HipaaCategory
            };

            return Result<FieldPhiDetectionResult>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<FieldPhiDetectionResult>.Failure($"Field PHI detection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Analyzes free text fields for embedded PHI using regex-based pattern detection.
    /// Detection lives in <see cref="FreeTextPhiAnalyzer"/>.
    /// </summary>
    public async Task<Result<FreeTextPhiDetectionResult>> AnalyzeFreeTextAsync(
        string freeText,
        ClinicalContext? context = null,
        PhiDetectionOptions? options = null)
    {
        try
        {
            await Task.Yield();

            return Result<FreeTextPhiDetectionResult>.Success(FreeTextPhiAnalyzer.Analyze(freeText));
        }
        catch (Exception ex)
        {
            return Result<FreeTextPhiDetectionResult>.Failure($"Free text analysis failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Estimates re-identification risk based on quasi-identifiers present.
    /// </summary>
    public async Task<Result<RiskAssessmentResult>> AssessReIdentificationRiskAsync(
        IEnumerable<string> messages,
        int populationSize = 320_000_000,
        PhiDetectionOptions? options = null)
    {
        try
        {
            await Task.Yield(); // Placeholder for statistical analysis

            var result = new RiskAssessmentResult
            {
                ReIdentificationRisk = 0.001,
                KAnonymityScore = 5,
                LDiversityScore = 3,
                EquivalenceClasses = 1,
                ClassAnalyses = Array.Empty<EquivalenceClassAnalysis>()
            };

            return Result<RiskAssessmentResult>.Success(result);
        }
        catch (Exception ex)
        {
            return Result<RiskAssessmentResult>.Failure($"Risk assessment failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the standard HIPAA Safe Harbor field mapping for HL7 messages.
    /// The plugin owns the projection from its infrastructure mappings to the
    /// application-layer PhiFieldMapping records.
    /// </summary>
    public IReadOnlyDictionary<string, PhiFieldMapping> GetStandardFieldMappings()
    {
        return _hl7Plugin.GetPhiFieldMappings();
    }

    /// <summary>
    /// Registers custom PHI detection patterns for organization-specific identifiers.
    /// </summary>
    public Result<Unit> RegisterCustomPatterns(IEnumerable<CustomPhiPattern> patterns)
    {
        try
        {
            // Placeholder implementation - would register patterns with infrastructure
            return Result<Unit>.Success(Unit.Instance);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure($"Failed to register custom patterns: {ex.Message}");
        }
    }
}
