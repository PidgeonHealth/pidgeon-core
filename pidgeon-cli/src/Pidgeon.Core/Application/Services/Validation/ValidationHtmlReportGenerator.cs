// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Services.Reporting;
using Pidgeon.Core.Domain.Validation;
using System.Text;
using System.Web;

namespace Pidgeon.Core.Application.Services.Validation;

/// <summary>One validated source (a file path or stdin) and its validation result, for report rendering.</summary>
public sealed record ValidationReportEntry(string Source, ValidationResult Result);

/// <summary>
/// Generates the self-contained HTML report for <c>pidgeon validate --report</c>:
/// a per-source pass/fail summary, the issue table with locations and suggested
/// fixes, and the shared attribution footer.
/// </summary>
public static class ValidationHtmlReportGenerator
{
    public static string GenerateReport(IReadOnlyList<ValidationReportEntry> entries, string? reproduceCommand = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<title>Pidgeon Validation Report</title>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<style>");
        sb.AppendLine(GetCss());
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        var passed = entries.Count(e => e.Result.IsValid);
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine("<h1>Pidgeon Validation Report</h1>");
        sb.AppendLine($"<p class=\"meta\">Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");
        sb.AppendLine($"<p class=\"meta\">Sources validated: {entries.Count} · Passed: {passed} · Failed: {entries.Count - passed}</p>");
        sb.AppendLine("</div>");

        foreach (var entry in entries)
            AppendSourceSection(sb, entry);

        sb.AppendLine(ReportAttribution.FooterHtml("report-validate", reproduceCommand));
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static void AppendSourceSection(StringBuilder sb, ValidationReportEntry entry)
    {
        var result = entry.Result;
        var verdictClass = result.IsValid ? "badge-pass" : "badge-fail";
        var verdict = result.IsValid ? "PASSED" : "FAILED";

        sb.AppendLine("<div class=\"source-section\">");
        sb.AppendLine($"<h2>{Encode(entry.Source)} <span class=\"badge {verdictClass}\">{verdict}</span></h2>");
        sb.AppendLine($"<p class=\"meta\">Standard: {Encode(result.Standard)} · Mode: {result.Mode}" +
                      (string.IsNullOrEmpty(result.Profile) ? "" : $" · Profile: {Encode(result.Profile)}") + "</p>");

        if (result.Issues.Count > 0)
        {
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>Severity</th><th>Rule</th><th>Location</th><th>Message</th><th>Suggested fix</th></tr></thead>");
            sb.AppendLine("<tbody>");
            foreach (var issue in result.Issues)
            {
                var severityClass = issue.Severity == ValidationSeverity.Error ? "severity-error" : "severity-warning";
                sb.AppendLine($"<tr class=\"{severityClass}\">");
                sb.AppendLine($"<td><span class=\"badge {severityClass}\">{issue.Severity}</span></td>");
                sb.AppendLine($"<td><code>{Encode(issue.RuleId)}</code></td>");
                sb.AppendLine($"<td><code>{Encode(issue.Location)}</code></td>");
                sb.AppendLine($"<td>{Encode(issue.Message)}</td>");
                sb.AppendLine($"<td>{Encode(issue.Suggestion ?? "")}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody>");
            sb.AppendLine("</table>");
        }
        else
        {
            sb.AppendLine("<p>No issues found.</p>");
        }

        var stats = result.Statistics;
        if (stats.FieldsValidated > 0)
        {
            sb.AppendLine($"<p class=\"meta\">{stats.FieldsValidated} fields checked, " +
                          $"{stats.RulesPassed} of {stats.TotalRulesChecked} rules passed</p>");
        }

        sb.AppendLine("</div>");
    }

    private static string Encode(string? value) => HttpUtility.HtmlEncode(value ?? string.Empty);

    private static string GetCss()
    {
        return @"
* { margin: 0; padding: 0; box-sizing: border-box; }
body {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
    line-height: 1.6;
    color: #333;
    max-width: 1200px;
    margin: 0 auto;
    padding: 20px;
    background: #f8f9fa;
}
.header {
    background: #1a365d;
    color: white;
    padding: 24px;
    border-radius: 8px;
    margin-bottom: 20px;
}
.header h1 { font-size: 24px; margin-bottom: 8px; }
.meta { font-size: 14px; opacity: 0.85; }
.source-section {
    background: white;
    padding: 20px;
    border-radius: 8px;
    margin-bottom: 16px;
    box-shadow: 0 1px 3px rgba(0,0,0,0.1);
    overflow-x: auto;
}
.source-section h2 { font-size: 18px; margin-bottom: 8px; }
.source-section .meta { color: #666; margin-bottom: 12px; }
table { width: 100%; border-collapse: collapse; font-size: 14px; }
th {
    background: #edf2f7;
    padding: 10px 12px;
    text-align: left;
    font-weight: 600;
    border-bottom: 2px solid #cbd5e0;
}
td { padding: 8px 12px; border-bottom: 1px solid #e2e8f0; }
tr:nth-child(even) td { background: #f7fafc; }
tr.severity-error td { border-left: 3px solid #e53e3e; }
tr.severity-warning td { border-left: 3px solid #d69e2e; }
code { background: #edf2f7; padding: 2px 6px; border-radius: 3px; font-size: 13px; }
.badge {
    display: inline-block;
    padding: 2px 8px;
    border-radius: 12px;
    font-size: 12px;
    font-weight: 600;
    text-transform: uppercase;
}
.badge-pass { background: #c6f6d5; color: #276749; }
.badge-fail { background: #fed7d7; color: #c53030; }
.badge.severity-error { background: #fed7d7; color: #c53030; }
.badge.severity-warning { background: #fefcbf; color: #b7791f; }";
    }
}
