// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Search;
using Pidgeon.Core.Domain.Search;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pidgeon.Core.Application.Services.Search;

/// <summary>
/// Service for searching values within healthcare message files.
/// </summary>
public class MessageSearchService : IMessageSearchService
{
    private readonly ILogger<MessageSearchService> _logger;

    // File extensions for healthcare messages
    private static readonly string[] SupportedExtensions = { ".hl7", ".adt", ".oru", ".rde", ".json", ".xml", ".txt" };

    public MessageSearchService(ILogger<MessageSearchService> logger)
    {
        _logger = logger;
    }

    public async Task<List<ValueLocation>> FindValueInFilesAsync(string value, string searchPath, CancellationToken cancellationToken = default)
    {
        var results = new List<ValueLocation>();

        try
        {
            if (File.Exists(searchPath))
            {
                // Search single file
                var fileResults = await SearchFileAsync(searchPath, value, cancellationToken);
                results.AddRange(fileResults);
            }
            else if (searchPath.Contains('*') || searchPath.Contains('?'))
            {
                // Handle glob pattern
                var directory = Path.GetDirectoryName(searchPath) ?? ".";
                var pattern = Path.GetFileName(searchPath);
                var files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);

                foreach (var file in files)
                {
                    if (IsSupportedFile(file))
                    {
                        var fileResults = await SearchFileAsync(file, value, cancellationToken);
                        results.AddRange(fileResults);
                    }
                }
            }
            else
            {
                _logger.LogWarning("Search path not found: {Path}", searchPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for value in files: {Path}", searchPath);
        }

        return results;
    }

    public async Task<List<ValueLocation>> FindValueInDirectoryAsync(string value, string directory, CancellationToken cancellationToken = default)
    {
        var results = new List<ValueLocation>();

        try
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("Directory not found: {Directory}", directory);
                return results;
            }

            // Search all supported files in directory
            var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(IsSupportedFile)
                .ToList();

            _logger.LogInformation("Searching {Count} files in {Directory} for value: {Value}",
                files.Count, directory, value);

            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var fileResults = await SearchFileAsync(file, value, cancellationToken);
                results.AddRange(fileResults);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching directory: {Directory}", directory);
        }

        return results;
    }

    private async Task<List<ValueLocation>> SearchFileAsync(string filePath, string searchValue, CancellationToken cancellationToken)
    {
        var results = new List<ValueLocation>();

        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            // Determine file type and search accordingly
            if (extension == ".hl7" || extension == ".adt" || extension == ".oru" || extension == ".rde" || extension == ".txt")
            {
                results = await SearchHL7FileAsync(filePath, searchValue, cancellationToken);
            }
            else if (extension == ".json")
            {
                results = await SearchJsonFileAsync(filePath, searchValue, cancellationToken);
            }
            else if (extension == ".xml")
            {
                results = await SearchXmlFileAsync(filePath, searchValue, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching file: {File}", filePath);
        }

        return results;
    }

    private async Task<List<ValueLocation>> SearchHL7FileAsync(string filePath, string searchValue, CancellationToken cancellationToken)
    {
        var results = new List<ValueLocation>();

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
            var lineNumber = 0;

            foreach (var line in lines)
            {
                lineNumber++;

                if (line.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                {
                    // Parse HL7 segment structure
                    var segments = line.Split('|');
                    if (segments.Length > 0)
                    {
                        var segmentName = segments[0];

                        // Find which field contains the value
                        for (int i = 1; i < segments.Length; i++)
                        {
                            if (segments[i].Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                            {
                                var location = new ValueLocation
                                {
                                    FileName = Path.GetFileName(filePath),
                                    FieldPath = $"{segmentName}.{i}",
                                    FieldDescription = GetHL7FieldDescription(segmentName, i),
                                    Standard = "HL7v2.3",
                                    MessageType = ExtractHL7MessageType(lines),
                                    DataType = "String", // Would need field metadata for exact type
                                    LineNumber = lineNumber
                                };

                                results.Add(location);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing HL7 file: {File}", filePath);
        }

        return results;
    }

    private async Task<List<ValueLocation>> SearchJsonFileAsync(string filePath, string searchValue, CancellationToken cancellationToken)
    {
        var results = new List<ValueLocation>();

        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);

            // Try to parse as FHIR resource
            if (json.Contains("resourceType", StringComparison.OrdinalIgnoreCase))
            {
                // Search FHIR JSON
                results = SearchJsonForValue(json, searchValue, filePath, "FHIR-R4");
            }
            else
            {
                // Generic JSON search
                results = SearchJsonForValue(json, searchValue, filePath, "JSON");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing JSON file: {File}", filePath);
        }

        return results;
    }

    private List<ValueLocation> SearchJsonForValue(string json, string searchValue, string filePath, string standard)
    {
        var results = new List<ValueLocation>();

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            SearchJsonElement(root, searchValue, "", filePath, standard, results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching JSON content");
        }

        return results;
    }

    private void SearchJsonElement(JsonElement element, string searchValue, string currentPath, string filePath, string standard, List<ValueLocation> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var newPath = string.IsNullOrEmpty(currentPath) ? property.Name : $"{currentPath}.{property.Name}";
                    SearchJsonElement(property.Value, searchValue, newPath, filePath, standard, results);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var newPath = $"{currentPath}[{index}]";
                    SearchJsonElement(item, searchValue, newPath, filePath, standard, results);
                    index++;
                }
                break;

            case JsonValueKind.String:
                var stringValue = element.GetString();
                if (stringValue != null && stringValue.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new ValueLocation
                    {
                        FileName = Path.GetFileName(filePath),
                        FieldPath = currentPath,
                        FieldDescription = ExtractFieldName(currentPath),
                        Standard = standard,
                        MessageType = ExtractResourceType(currentPath),
                        DataType = "String"
                    });
                }
                break;

            case JsonValueKind.Number:
                if (element.ToString() == searchValue)
                {
                    results.Add(new ValueLocation
                    {
                        FileName = Path.GetFileName(filePath),
                        FieldPath = currentPath,
                        FieldDescription = ExtractFieldName(currentPath),
                        Standard = standard,
                        MessageType = ExtractResourceType(currentPath),
                        DataType = "Number"
                    });
                }
                break;
        }
    }

    private async Task<List<ValueLocation>> SearchXmlFileAsync(string filePath, string searchValue, CancellationToken cancellationToken)
    {
        var results = new List<ValueLocation>();

        try
        {
            var xml = await File.ReadAllTextAsync(filePath, cancellationToken);

            // Simple regex-based XML search (would use XDocument for production)
            var pattern = $@"<([^>]+)>([^<]*{Regex.Escape(searchValue)}[^<]*)</\1>";
            var matches = Regex.Matches(xml, pattern, RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                var elementName = match.Groups[1].Value;
                var elementValue = match.Groups[2].Value;

                results.Add(new ValueLocation
                {
                    FileName = Path.GetFileName(filePath),
                    FieldPath = elementName,
                    FieldDescription = elementName,
                    Standard = "XML",
                    MessageType = "XML Document",
                    DataType = "String"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing XML file: {File}", filePath);
        }

        return results;
    }

    private bool IsSupportedFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    private string GetHL7FieldDescription(string segment, int fieldNumber)
    {
        // Common HL7 field descriptions (would use metadata service in production)
        return (segment, fieldNumber) switch
        {
            ("PID", 3) => "Patient Identifier List",
            ("PID", 5) => "Patient Name",
            ("PID", 7) => "Date/Time of Birth",
            ("PID", 8) => "Administrative Sex",
            ("PID", 11) => "Patient Address",
            ("PV1", 19) => "Visit Number",
            ("PV1", 3) => "Assigned Patient Location",
            ("OBX", 5) => "Observation Value",
            ("DG1", 3) => "Diagnosis Code",
            ("RXE", 2) => "Give Code",
            ("AL1", 3) => "Allergy Type",
            _ => $"{segment} Field {fieldNumber}"
        };
    }

    private string ExtractHL7MessageType(string[] lines)
    {
        // Find MSH segment and extract message type
        var mshLine = lines.FirstOrDefault(l => l.StartsWith("MSH"));
        if (mshLine != null)
        {
            var fields = mshLine.Split('|');
            if (fields.Length > 8)
            {
                return fields[8]; // MSH.9 contains message type
            }
        }
        return "HL7 Message";
    }

    private string ExtractFieldName(string path)
    {
        // Extract the last component of the path as field name
        var parts = path.Split('.');
        return parts.Length > 0 ? parts[^1].Trim('[', ']') : path;
    }

    private string ExtractResourceType(string path)
    {
        // For FHIR, the first component is usually the resource type
        var parts = path.Split('.');
        return parts.Length > 0 ? parts[0] : "Resource";
    }
}