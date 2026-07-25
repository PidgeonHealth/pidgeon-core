// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.Json;
using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Application.Services.DeIdentification;

/// <summary>
/// Renders de-identification compliance reports as JSON or self-contained HTML.
/// Pure string formatting over a completed <see cref="DeIdentificationResult"/> —
/// no state, no I/O.
/// </summary>
internal static class DeIdentificationReportRenderer
{
    public static string GenerateJsonReport(DeIdentificationResult result)
    {
        var reportObject = new
        {
            statistics = new
            {
                result.Statistics.TotalMessages,
                result.Statistics.TotalIdentifiersProcessed,
                result.Statistics.FieldsModified,
                result.Statistics.DatesShifted,
                result.Statistics.UniqueSubjects,
                result.Statistics.AverageProcessingTimeMs,
                TotalProcessingTimeMs = result.Statistics.TotalProcessingTime.TotalMilliseconds
            },
            compliance = new
            {
                result.Compliance.MeetsSafeHarbor,
                Status = result.Compliance.Status.ToString(),
                result.Compliance.SafeHarborChecklist
            },
            metadata = new
            {
                result.Metadata.StartedAt,
                result.Metadata.CompletedAt,
                result.Metadata.ProcessingMode,
                result.Metadata.StandardsProcessed
            }
        };

        return JsonSerializer.Serialize(reportObject, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string GenerateHtmlReport(DeIdentificationResult result)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <title>De-identification Compliance Report</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { font-family: Arial, sans-serif; margin: 2rem; color: #333; }");
        sb.AppendLine("    h1 { color: #1a1a2e; border-bottom: 2px solid #1a1a2e; padding-bottom: 0.5rem; }");
        sb.AppendLine("    h2 { color: #16213e; margin-top: 2rem; }");
        sb.AppendLine("    table { border-collapse: collapse; width: 100%; margin-top: 1rem; }");
        sb.AppendLine("    th, td { border: 1px solid #ddd; padding: 0.5rem 1rem; text-align: left; }");
        sb.AppendLine("    th { background-color: #f4f4f4; font-weight: bold; }");
        sb.AppendLine("    .pass { color: #2e7d32; font-weight: bold; }");
        sb.AppendLine("    .fail { color: #c62828; font-weight: bold; }");
        sb.AppendLine("    .stat-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 1rem; margin-top: 1rem; }");
        sb.AppendLine("    .stat-card { background: #f9f9f9; border: 1px solid #ddd; border-radius: 4px; padding: 1rem; }");
        sb.AppendLine("    .stat-card .value { font-size: 1.5rem; font-weight: bold; color: #1a1a2e; }");
        sb.AppendLine("    .stat-card .label { font-size: 0.85rem; color: #666; margin-top: 0.25rem; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <h1>De-identification Compliance Report</h1>");

        // Summary statistics
        sb.AppendLine("  <h2>Summary</h2>");
        sb.AppendLine("  <div class=\"stat-grid\">");
        AppendStatCard(sb, result.Statistics.TotalMessages.ToString(), "Messages Processed");
        AppendStatCard(sb, result.Statistics.TotalIdentifiersProcessed.ToString(), "Identifiers Found");
        AppendStatCard(sb, result.Statistics.FieldsModified.ToString(), "Fields Modified");
        AppendStatCard(sb, result.Statistics.DatesShifted.ToString(), "Dates Shifted");
        AppendStatCard(sb, result.Statistics.UniqueSubjects.ToString(), "Unique Subjects");
        AppendStatCard(sb, $"{result.Statistics.AverageProcessingTimeMs:F1} ms", "Avg Processing Time");
        sb.AppendLine("  </div>");

        // Compliance checklist
        sb.AppendLine("  <h2>HIPAA Safe Harbor Checklist</h2>");
        sb.AppendLine("  <table>");
        sb.AppendLine("    <thead>");
        sb.AppendLine("      <tr><th>Identifier Category</th><th>Status</th></tr>");
        sb.AppendLine("    </thead>");
        sb.AppendLine("    <tbody>");
        foreach (var item in result.Compliance.SafeHarborChecklist)
        {
            var statusClass = item.Value ? "pass" : "fail";
            var statusText = item.Value ? "Removed" : "NOT Removed";
            sb.AppendLine($"      <tr><td>{item.Key}</td><td class=\"{statusClass}\">{statusText}</td></tr>");
        }
        sb.AppendLine("    </tbody>");
        sb.AppendLine("  </table>");

        // Processing metadata
        sb.AppendLine("  <h2>Processing Details</h2>");
        sb.AppendLine("  <table>");
        sb.AppendLine("    <tbody>");
        sb.AppendLine($"      <tr><td>Start Time</td><td>{result.Metadata.StartedAt:yyyy-MM-dd HH:mm:ss} UTC</td></tr>");
        sb.AppendLine($"      <tr><td>End Time</td><td>{result.Metadata.CompletedAt:yyyy-MM-dd HH:mm:ss} UTC</td></tr>");
        sb.AppendLine($"      <tr><td>Processing Mode</td><td>{result.Metadata.ProcessingMode}</td></tr>");
        sb.AppendLine($"      <tr><td>Standards Processed</td><td>{string.Join(", ", result.Metadata.StandardsProcessed)}</td></tr>");
        sb.AppendLine($"      <tr><td>Overall Status</td><td>{result.Compliance.Status}</td></tr>");
        sb.AppendLine("    </tbody>");
        sb.AppendLine("  </table>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static void AppendStatCard(StringBuilder sb, string value, string label)
    {
        sb.AppendLine("    <div class=\"stat-card\">");
        sb.AppendLine($"      <div class=\"value\">{value}</div>");
        sb.AppendLine($"      <div class=\"label\">{label}</div>");
        sb.AppendLine("    </div>");
    }
}
