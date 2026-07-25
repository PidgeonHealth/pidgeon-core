// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.Json;
using Pidgeon.Core.Domain.DeIdentification;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.DeIdentification;

/// <summary>
/// Application service for generating compliance audit reports.
/// Supports multiple output formats for regulatory documentation.
/// </summary>
internal class AuditReportService
{
    /// <summary>
    /// Generates comprehensive audit reports for compliance documentation.
    /// </summary>
    public async Task<Result<string>> GenerateAuditReportAsync(
        DeIdentificationResult result, 
        string outputPath, 
        ReportFormat format = ReportFormat.Html)
    {
        try
        {
            if (result == null)
                return Result<string>.Failure("De-identification result cannot be null");

            if (string.IsNullOrWhiteSpace(outputPath))
                return Result<string>.Failure("Output path cannot be null or empty");

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var reportContent = format switch
            {
                ReportFormat.Html => await GenerateHtmlReportAsync(result),
                ReportFormat.Json => await GenerateJsonReportAsync(result),
                ReportFormat.Pdf => await GeneratePdfReportAsync(result),
                ReportFormat.Xml => await GenerateXmlReportAsync(result),
                ReportFormat.Csv => await GenerateCsvReportAsync(result),
                _ => await GenerateHtmlReportAsync(result) // Default to HTML
            };

            await File.WriteAllTextAsync(outputPath, reportContent);
            
            return Result<string>.Success(outputPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Report generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates batch audit reports for multiple de-identification results.
    /// </summary>
    public async Task<Result<string>> GenerateBatchAuditReportAsync(
        BatchDeIdentificationResult batchResult,
        string outputPath,
        ReportFormat format = ReportFormat.Html)
    {
        try
        {
            if (batchResult == null)
                return Result<string>.Failure("Batch result cannot be null");

            if (string.IsNullOrWhiteSpace(outputPath))
                return Result<string>.Failure("Output path cannot be null or empty");

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var reportContent = format switch
            {
                ReportFormat.Html => await GenerateBatchHtmlReportAsync(batchResult),
                ReportFormat.Json => await GenerateBatchJsonReportAsync(batchResult),
                ReportFormat.Csv => await GenerateBatchCsvReportAsync(batchResult),
                _ => await GenerateBatchHtmlReportAsync(batchResult)
            };

            await File.WriteAllTextAsync(outputPath, reportContent);
            
            return Result<string>.Success(outputPath);
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Batch report generation failed: {ex.Message}");
        }
    }

    // Private report generation methods

    private async Task<string> GenerateHtmlReportAsync(DeIdentificationResult result)
    {
        await Task.Yield();
        
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html><head>");
        html.AppendLine("<title>De-identification Audit Report</title>");
        html.AppendLine("<style>");
        html.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; }");
        html.AppendLine("table { border-collapse: collapse; width: 100%; }");
        html.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
        html.AppendLine("th { background-color: #f2f2f2; }");
        html.AppendLine(".compliant { color: green; font-weight: bold; }");
        html.AppendLine(".non-compliant { color: red; font-weight: bold; }");
        html.AppendLine("</style>");
        html.AppendLine("</head><body>");
        
        // Header
        html.AppendLine("<h1>HIPAA De-identification Audit Report</h1>");
        html.AppendLine($"<p><strong>Generated:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");
        html.AppendLine($"<p><strong>Processing Date:</strong> {result.Metadata.StartedAt:yyyy-MM-dd HH:mm:ss}</p>");
        
        // Summary Statistics
        html.AppendLine("<h2>Processing Summary</h2>");
        html.AppendLine("<table>");
        html.AppendLine($"<tr><td>Messages Processed</td><td>{result.Statistics.TotalMessages}</td></tr>");
        html.AppendLine($"<tr><td>Identifiers Processed</td><td>{result.Statistics.TotalIdentifiersProcessed}</td></tr>");
        html.AppendLine($"<tr><td>Fields Modified</td><td>{result.Statistics.FieldsModified}</td></tr>");
        html.AppendLine($"<tr><td>Dates Shifted</td><td>{result.Statistics.DatesShifted}</td></tr>");
        html.AppendLine($"<tr><td>Processing Time</td><td>{result.Statistics.TotalProcessingTime}</td></tr>");
        html.AppendLine("</table>");
        
        // HIPAA Compliance
        html.AppendLine("<h2>HIPAA Safe Harbor Compliance</h2>");
        var complianceClass = result.Compliance.MeetsSafeHarbor ? "compliant" : "non-compliant";
        html.AppendLine($"<p class=\"{complianceClass}\">Status: {result.Compliance.Status}</p>");
        
        html.AppendLine("<h3>Safe Harbor Checklist</h3>");
        html.AppendLine("<table>");
        html.AppendLine("<tr><th>Requirement</th><th>Status</th></tr>");
        foreach (var check in result.Compliance.SafeHarborChecklist)
        {
            var statusClass = check.Value ? "compliant" : "non-compliant";
            var statusText = check.Value ? "✓ Compliant" : "✗ Non-compliant";
            html.AppendLine($"<tr><td>{check.Key}</td><td class=\"{statusClass}\">{statusText}</td></tr>");
        }
        html.AppendLine("</table>");
        
        // Identifier Breakdown
        if (result.Statistics.IdentifiersByType.Any())
        {
            html.AppendLine("<h2>Identifiers by Type</h2>");
            html.AppendLine("<table>");
            html.AppendLine("<tr><th>Identifier Type</th><th>Count</th></tr>");
            foreach (var identifier in result.Statistics.IdentifiersByType.OrderByDescending(kvp => kvp.Value))
            {
                html.AppendLine($"<tr><td>{identifier.Key}</td><td>{identifier.Value}</td></tr>");
            }
            html.AppendLine("</table>");
        }
        
        // Warnings
        if (result.Warnings.Any())
        {
            html.AppendLine("<h2>Warnings</h2>");
            html.AppendLine("<table>");
            html.AppendLine("<tr><th>Type</th><th>Message</th><th>Severity</th></tr>");
            foreach (var warning in result.Warnings)
            {
                html.AppendLine($"<tr><td>{warning.Type}</td><td>{warning.Message}</td><td>{warning.Severity}</td></tr>");
            }
            html.AppendLine("</table>");
        }
        
        html.AppendLine("</body></html>");
        
        return html.ToString();
    }

    private async Task<string> GenerateJsonReportAsync(DeIdentificationResult result)
    {
        await Task.Yield();
        var options = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(result, options);
    }

    private async Task<string> GenerateCsvReportAsync(DeIdentificationResult result)
    {
        await Task.Yield();
        var csv = new StringBuilder();
        csv.AppendLine("Reference,Synthetic,Type,Action,Timestamp");

        if (result.AuditTrail != null)
        {
            foreach (var action in result.AuditTrail)
            {
                csv.AppendLine($"\"{action.Value.OriginalValueHash}\",\"{action.Value.SyntheticValue}\",\"{action.Value.IdentifierType}\",\"{action.Value.Action}\",\"{action.Value.Timestamp:yyyy-MM-dd HH:mm:ss}\"");
            }
        }
        
        return csv.ToString();
    }

    private async Task<string> GeneratePdfReportAsync(DeIdentificationResult result)
    {
        await Task.Yield();
        // Placeholder - would use PDF library like iTextSharp or similar in production
        return "PDF generation requires additional libraries. HTML report generated instead: " + 
               await GenerateHtmlReportAsync(result);
    }

    private async Task<string> GenerateXmlReportAsync(DeIdentificationResult result)
    {
        await Task.Yield();
        var xml = new StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<deidentification-audit-report>");
        xml.AppendLine($"  <generated-date>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}</generated-date>");
        xml.AppendLine("  <statistics>");
        xml.AppendLine($"    <messages-processed>{result.Statistics.TotalMessages}</messages-processed>");
        xml.AppendLine($"    <identifiers-processed>{result.Statistics.TotalIdentifiersProcessed}</identifiers-processed>");
        xml.AppendLine($"    <processing-time>{result.Statistics.TotalProcessingTime}</processing-time>");
        xml.AppendLine("  </statistics>");
        xml.AppendLine("  <compliance>");
        xml.AppendLine($"    <meets-safe-harbor>{result.Compliance.MeetsSafeHarbor}</meets-safe-harbor>");
        xml.AppendLine($"    <status>{result.Compliance.Status}</status>");
        xml.AppendLine("  </compliance>");
        xml.AppendLine("</deidentification-audit-report>");
        
        return xml.ToString();
    }

    private async Task<string> GenerateBatchHtmlReportAsync(BatchDeIdentificationResult batchResult)
    {
        await Task.Yield();
        
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html><head>");
        html.AppendLine("<title>Batch De-identification Audit Report</title>");
        html.AppendLine("<style>");
        html.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; }");
        html.AppendLine("table { border-collapse: collapse; width: 100%; margin-bottom: 20px; }");
        html.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
        html.AppendLine("th { background-color: #f2f2f2; }");
        html.AppendLine(".success { color: green; }");
        html.AppendLine(".error { color: red; }");
        html.AppendLine("</style>");
        html.AppendLine("</head><body>");
        
        html.AppendLine("<h1>Batch De-identification Audit Report</h1>");
        html.AppendLine($"<p><strong>Generated:</strong> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");
        
        // Batch Summary
        html.AppendLine("<h2>Batch Summary</h2>");
        html.AppendLine("<table>");
        html.AppendLine($"<tr><td>Total Files</td><td>{batchResult.Metadata.TotalFiles}</td></tr>");
        html.AppendLine($"<tr><td>Successful Files</td><td class=\"success\">{batchResult.Metadata.SuccessfulFiles}</td></tr>");
        html.AppendLine($"<tr><td>Failed Files</td><td class=\"error\">{batchResult.Metadata.FailedFiles}</td></tr>");
        html.AppendLine($"<tr><td>Success Rate</td><td>{batchResult.SuccessRate:P1}</td></tr>");
        html.AppendLine($"<tr><td>Total Processing Time</td><td>{batchResult.Metadata.TotalProcessingTime}</td></tr>");
        html.AppendLine("</table>");
        
        // File Results
        html.AppendLine("<h2>File Processing Results</h2>");
        html.AppendLine("<table>");
        html.AppendLine("<tr><th>Input File</th><th>Output File</th><th>Status</th><th>Processing Time</th><th>Size Change</th></tr>");
        foreach (var fileResult in batchResult.FileResults)
        {
            var statusClass = fileResult.Success ? "success" : "error";
            var statusText = fileResult.Success ? "Success" : "Failed";
            var sizeChange = $"{fileResult.SizeInfo.SizeChangeRatio:P1}";
            
            html.AppendLine($"<tr>");
            html.AppendLine($"  <td>{Path.GetFileName(fileResult.InputPath)}</td>");
            html.AppendLine($"  <td>{Path.GetFileName(fileResult.OutputPath)}</td>");
            html.AppendLine($"  <td class=\"{statusClass}\">{statusText}</td>");
            html.AppendLine($"  <td>{fileResult.ProcessingTime.TotalSeconds:F2}s</td>");
            html.AppendLine($"  <td>{sizeChange}</td>");
            html.AppendLine("</tr>");
        }
        html.AppendLine("</table>");
        
        html.AppendLine("</body></html>");
        
        return html.ToString();
    }

    private async Task<string> GenerateBatchJsonReportAsync(BatchDeIdentificationResult batchResult)
    {
        await Task.Yield();
        var options = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(batchResult, options);
    }

    private async Task<string> GenerateBatchCsvReportAsync(BatchDeIdentificationResult batchResult)
    {
        await Task.Yield();
        var csv = new StringBuilder();
        csv.AppendLine("InputFile,OutputFile,Success,ProcessingTimeMs,OriginalSizeBytes,DeIdentifiedSizeBytes,SizeChangeRatio");
        
        foreach (var fileResult in batchResult.FileResults)
        {
            csv.AppendLine($"\"{fileResult.InputPath}\",\"{fileResult.OutputPath}\",{fileResult.Success},{fileResult.ProcessingTime.TotalMilliseconds},{fileResult.SizeInfo.OriginalSizeBytes},{fileResult.SizeInfo.DeIdentifiedSizeBytes},{fileResult.SizeInfo.SizeChangeRatio}");
        }
        
        return csv.ToString();
    }
}