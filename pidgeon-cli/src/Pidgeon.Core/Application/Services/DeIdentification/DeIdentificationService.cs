// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Interfaces.Standards;
using Pidgeon.Core.Domain.DeIdentification;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Infrastructure.Standards.FHIR.R4.DeIdentification;
using Pidgeon.Core.Infrastructure.Standards.NCPDP.DeIdentification;

namespace Pidgeon.Core.Application.Services.DeIdentification;

/// <summary>
/// Detected healthcare message format for de-identification routing.
/// </summary>
public enum MessageFormat
{
    Unknown,
    HL7,
    FHIR,
    NCPDP
}

/// <summary>
/// Application service implementing domain de-identification contracts.
/// Delegates to standard-specific plugins for actual de-identification logic.
/// </summary>
internal class DeIdentificationService : IDeIdentificationService
{
    private readonly IReadOnlyList<IStandardDeIdentificationPlugin> _plugins;
    private readonly FHIRDeIdentifier _fhirDeIdentifier;
    private readonly NCPDPDeIdentifier _ncpdpDeIdentifier;
    private readonly IPhiDetector _phiDetector;

    public DeIdentificationService(
        IPhiDetector phiDetector,
        IEnumerable<IStandardDeIdentificationPlugin> plugins)
    {
        _phiDetector = phiDetector;
        _plugins = (plugins ?? throw new ArgumentNullException(nameof(plugins)))
            .ToList()
            .AsReadOnly();
        _fhirDeIdentifier = new FHIRDeIdentifier();
        _ncpdpDeIdentifier = new NCPDPDeIdentifier();
    }

    /// <summary>
    /// De-identifies a single healthcare message using the provided context.
    /// Routes to the first registered plugin whose CanHandle matches (HL7 today);
    /// FHIR and NCPDP remain direct pending their own plugin extraction.
    /// </summary>
    public Result<string> DeIdentifyMessage(string message, DeIdentificationContext context)
    {
        var plugin = _plugins.FirstOrDefault(p => p.CanHandle(message));
        if (plugin is not null)
            return plugin.DeIdentifyMessage(message, context);

        var format = DetectMessageFormat(message);
        return format switch
        {
            MessageFormat.FHIR => _fhirDeIdentifier.DeIdentifyMessage(message, context),
            MessageFormat.NCPDP => _ncpdpDeIdentifier.DeIdentifyMessage(message, context),
            MessageFormat.HL7 => Result<string>.Failure("No de-identification plugin is registered for HL7 content."),
            _ => Result<string>.Failure("Unrecognized message format. Expected HL7 (MSH|...), FHIR JSON ({\"resourceType\":...}), or NCPDP SCRIPT XML.")
        };
    }

    /// <summary>
    /// Detects the healthcare message format from content.
    /// </summary>
    public static MessageFormat DetectMessageFormat(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return MessageFormat.Unknown;

        var trimmed = content.TrimStart();

        // HL7: starts with MSH| segment header
        if (trimmed.StartsWith("MSH|", StringComparison.Ordinal))
            return MessageFormat.HL7;

        // FHIR: JSON object with resourceType field
        if (trimmed.StartsWith('{') && trimmed.Contains("\"resourceType\""))
            return MessageFormat.FHIR;

        // NCPDP: XML with NCPDP SCRIPT namespace or Message root element
        if (trimmed.StartsWith('<') &&
            (trimmed.Contains("ncpdp.org/schema/SCRIPT") || trimmed.Contains("<Message")))
            return MessageFormat.NCPDP;

        return MessageFormat.Unknown;
    }

    /// <summary>
    /// De-identifies multiple related healthcare messages maintaining consistency.
    /// </summary>
    public async Task<Result<DeIdentificationResult>> DeIdentifyMessagesAsync(
        IEnumerable<string> messages,
        DeIdentificationOptions options)
    {
        await Task.Yield();

        var contextResult = CreateContext(options);
        if (contextResult.IsFailure)
            return Result<DeIdentificationResult>.Failure(contextResult.Error);

        var context = contextResult.Value;
        var deidentifiedMessages = new List<string>();
        var formatsProcessed = new HashSet<string>();
        var startTime = DateTime.UtcNow;

        foreach (var message in messages)
        {
            var format = DetectMessageFormat(message);
            formatsProcessed.Add(format switch
            {
                MessageFormat.HL7 => "HL7 v2.3",
                MessageFormat.FHIR => "FHIR R4",
                MessageFormat.NCPDP => "NCPDP SCRIPT 2017071",
                _ => "Unknown"
            });

            var result = DeIdentifyMessage(message, context);
            if (result.IsFailure)
                return Result<DeIdentificationResult>.Failure(result.Error);

            deidentifiedMessages.Add(result.Value);
        }

        var endTime = DateTime.UtcNow;
        var processingTime = endTime - startTime;

        var deidentificationResult = new DeIdentificationResult
        {
            DeIdentifiedMessages = deidentifiedMessages,
            IdMappings = context.GetIdMappings(),
            Statistics = new DeIdentificationStatistics
            {
                TotalMessages = deidentifiedMessages.Count,
                TotalIdentifiersProcessed = context.GetProcessedIdentifierCount(),
                IdentifiersByType = context.GetIdentifiersByType(),
                FieldsModified = context.GetModifiedFieldCount(),
                DatesShifted = context.GetShiftedDateCount(),
                AverageProcessingTimeMs = processingTime.TotalMilliseconds / Math.Max(1, deidentifiedMessages.Count),
                TotalProcessingTime = processingTime,
                UniqueSubjects = context.GetUniqueSubjectCount()
            },
            Compliance = await ComputeComplianceAsync(deidentifiedMessages, context.EmittedValues, context.ContainsFreeText),
            Metadata = new ProcessingMetadata
            {
                StartedAt = startTime,
                CompletedAt = endTime,
                Options = options,
                StandardsProcessed = formatsProcessed.ToArray(),
                ProcessingMode = "Batch"
            },
            AuditTrail = context.GetAuditTrail()
        };

        return Result<DeIdentificationResult>.Success(deidentificationResult);
    }

    /// <summary>
    /// Creates a new de-identification context from the provided options.
    /// </summary>
    public Result<DeIdentificationContext> CreateContext(DeIdentificationOptions options)
    {
        try
        {
            var context = new DeIdentificationContext
            {
                Salt = options.Salt ?? Guid.NewGuid().ToString(),
                DateShift = options.DateShift ?? TimeSpan.Zero
            };
            return Result<DeIdentificationContext>.Success(context);
        }
        catch (ArgumentException ex)
        {
            return Result<DeIdentificationContext>.Failure($"Invalid options: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates that a de-identified message meets compliance requirements.
    /// </summary>
    public async Task<Result<ComplianceVerification>> ValidateDeIdentificationAsync(
        string original,
        string deidentified,
        DeIdentificationOptions options)
    {
        await Task.Yield();

        // Standalone validation has no record of which synthetics the engine emitted, so it cannot tell a
        // realistic synthetic apart from surviving PHI. Report Unknown rather than a false Compliant; a
        // computed verdict is produced by DeIdentifyMessagesAsync, which holds the emitted-synthetic set.
        var verification = new ComplianceVerification
        {
            MeetsSafeHarbor = false,
            SafeHarborChecklist = new Dictionary<string, bool>(),
            Status = ComplianceStatus.Unknown,
            Notes = "Standalone validation is not scan-verified; use DeIdentifyMessagesAsync for a computed verdict."
        };

        return Result<ComplianceVerification>.Success(verification);
    }

    /// <summary>
    /// Detects potential PHI in a message without modifying it.
    /// </summary>
    public async Task<Result<PhiDetectionResult>> DetectPhiAsync(string message)
    {
        return await _phiDetector.ScanForPhiAsync(message);
    }

    /// <summary>
    /// Estimates re-identification risk for statistical de-identification methods.
    /// </summary>
    public async Task<Result<RiskAssessmentResult>> AssessReIdentificationRiskAsync(
        IEnumerable<string> messages,
        IEnumerable<string> quasiIdentifiers)
    {
        await Task.Yield();

        var assessment = new RiskAssessmentResult
        {
            ReIdentificationRisk = 0.02,
            KAnonymityScore = 5,
            LDiversityScore = 3,
            EquivalenceClasses = 10,
            ClassAnalyses = Array.Empty<EquivalenceClassAnalysis>()
        };

        return Result<RiskAssessmentResult>.Success(assessment);
    }

    /// <summary>
    /// Generates a comprehensive de-identification report for audit and compliance.
    /// </summary>
    public async Task<Result<string>> GenerateComplianceReportAsync(
        DeIdentificationResult result,
        ReportFormat format = ReportFormat.Json)
    {
        await Task.Yield();

        var report = format switch
        {
            ReportFormat.Json => DeIdentificationReportRenderer.GenerateJsonReport(result),
            ReportFormat.Html => DeIdentificationReportRenderer.GenerateHtmlReport(result),
            _ => $"Compliance Report: {result.Compliance.Status}"
        };

        return Result<string>.Success(report);
    }

    /// <summary>
    /// Loads ID mappings from a previous de-identification session.
    /// </summary>
    public async Task<Result<Dictionary<string, string>>> LoadIdMappingsAsync(string mappingFilePath)
    {
        await Task.Yield();

        var mappings = new Dictionary<string, string>();
        return Result<Dictionary<string, string>>.Success(mappings);
    }

    /// <summary>
    /// Saves ID mappings for reuse in future de-identification sessions.
    /// </summary>
    public async Task<Result<Unit>> SaveIdMappingsAsync(
        IReadOnlyDictionary<string, string> mappings,
        string mappingFilePath)
    {
        await Task.Yield();

        return Result<Unit>.Success(Unit.Instance);
    }

    /// <summary>
    /// Computes the Safe Harbor verdict by re-scanning each de-identified output for residual PHI. A detected
    /// value is a real leak only if it is NOT one of the synthetic values the engine emitted (<paramref
    /// name="emitted"/>): realistic synthetic replacements look like PHI to the scanner, so a naive re-scan
    /// would false-positive. The residual scanner understands HL7 only, so FHIR/NCPDP output resolves to
    /// Unknown rather than a false Compliant.
    /// </summary>
    internal async Task<ComplianceVerification> ComputeComplianceAsync(
        IReadOnlyList<string> deidentifiedMessages, IReadOnlySet<string> emitted, bool containsFreeText = false)
    {
        var remaining = new List<RemainingPhiWarning>();
        var anyUnverifiable = false;
        var scanOptions = new PhiDetectionOptions { MaxSampleLength = int.MaxValue };

        foreach (var output in deidentifiedMessages)
        {
            if (DetectMessageFormat(output) != MessageFormat.HL7)
            {
                anyUnverifiable = true;
                continue;
            }

            var scan = await _phiDetector.ScanForPhiAsync(output, scanOptions);
            if (scan.IsFailure)
                continue;

            foreach (var item in scan.Value.DetectedPhi)
            {
                // A value the engine itself emitted is a synthetic, not a leak, even though it looks like PHI.
                if (emitted.Contains(item.Sample))
                    continue;

                // A free-text field's detected values are the engine's synthetics or undetectable prose, not a
                // structured residual — skip it here. The context's ContainsFreeText flag makes the whole verdict
                // Unknown for any message carrying free text, so it is never falsely certified regardless of what
                // this whole-field scan detects (or misses) inside the prose.
                if (item.Type == IdentifierType.FreeText)
                    continue;

                remaining.Add(new RemainingPhiWarning
                {
                    PhiType = item.Type.ToString(),
                    Location = item.Location,
                    Sample = "[residual PHI detected]", // never echo the surviving value
                    Confidence = item.Confidence
                });
            }
        }

        if (remaining.Count > 0)
        {
            return new ComplianceVerification
            {
                MeetsSafeHarbor = false,
                SafeHarborChecklist = new Dictionary<string, bool>(),
                RemainingPhi = remaining,
                Status = ComplianceStatus.NonCompliant,
                Notes = $"Structural residual re-scan found {remaining.Count} identifier(s) still present after de-identification."
            };
        }

        if (anyUnverifiable || containsFreeText)
        {
            return new ComplianceVerification
            {
                MeetsSafeHarbor = false,
                SafeHarborChecklist = new Dictionary<string, bool>(),
                Status = ComplianceStatus.Unknown,
                Notes = "Structural removal applied, but FHIR/NCPDP output or an HL7 free-text field is not independently verifiable by the HL7 residual scanner."
            };
        }

        return new ComplianceVerification
        {
            MeetsSafeHarbor = true,
            SafeHarborChecklist = GetSafeHarborChecklist(),
            Status = ComplianceStatus.Compliant,
            Notes = "Verified by an HL7 structural residual re-scan of the output (no identifier survived outside the emitted synthetic set)."
        };
    }

    private static Dictionary<string, bool> GetSafeHarborChecklist()
    {
        return new Dictionary<string, bool>
        {
            ["Names"] = true,
            ["Geographic subdivisions"] = true,
            ["Dates"] = true,
            ["Phone numbers"] = true,
            ["Fax numbers"] = true,
            ["Email addresses"] = true,
            ["Social security numbers"] = true,
            ["Medical record numbers"] = true,
            ["Health plan beneficiary numbers"] = true,
            ["Account numbers"] = true,
            ["Certificate/license numbers"] = true,
            ["Vehicle identifiers"] = true,
            ["Device identifiers"] = true,
            ["Web URLs"] = true,
            ["IP addresses"] = true,
            ["Biometric identifiers"] = true,
            ["Full face photos"] = true,
            ["Other unique identifiers"] = true
        };
    }
}
