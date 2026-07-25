// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.DeIdentification;

/// <summary>
/// Detects potential PHI using pattern matching and heuristics.
/// Used to identify PHI in fields not covered by standard HIPAA Safe Harbor mappings.
/// </summary>
public class PhiPatternDetector
{
    private readonly Dictionary<IdentifierType, List<Regex>> _patterns;
    private readonly HashSet<string> _commonWords;

    public PhiPatternDetector()
    {
        _patterns = InitializePatterns();
        _commonWords = InitializeCommonWords();
    }

    /// <summary>
    /// Checks if a field value contains potential PHI using pattern matching.
    /// </summary>
    /// <param name="fieldValue">Field value to analyze</param>
    /// <returns>True if potential PHI is detected</returns>
    public bool ContainsPotentialPhi(string? fieldValue)
    {
        if (string.IsNullOrWhiteSpace(fieldValue))
            return false;

        // Quick checks for obvious non-PHI content
        if (IsObviouslyNotPhi(fieldValue))
            return false;

        // Check each pattern type
        foreach (var patternGroup in _patterns.Values)
        {
            if (patternGroup.Any(pattern => pattern.IsMatch(fieldValue)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to determine the type of identifier based on pattern matching.
    /// </summary>
    /// <param name="fieldValue">Field value to analyze</param>
    /// <returns>Most likely identifier type</returns>
    public IdentifierType DetectIdentifierType(string fieldValue)
    {
        if (string.IsNullOrWhiteSpace(fieldValue))
            return IdentifierType.Other;

        // Score each identifier type based on pattern matches
        var scores = new Dictionary<IdentifierType, double>();

        foreach (var kvp in _patterns)
        {
            var identifierType = kvp.Key;
            var patterns = kvp.Value;
            
            double score = 0;
            foreach (var pattern in patterns)
            {
                if (pattern.IsMatch(fieldValue))
                {
                    // Weight patterns by specificity
                    score += GetPatternWeight(pattern);
                }
            }
            
            if (score > 0)
                scores[identifierType] = score;
        }

        // MaxBy is O(n) and zero-alloc, unlike OrderByDescending().First() which
        // allocates a sorted enumerable just to pick the top.
        return scores.Count > 0 ? scores.MaxBy(kvp => kvp.Value).Key : IdentifierType.Other;
    }

    /// <summary>
    /// Gets confidence level for PHI detection in the field.
    /// </summary>
    /// <param name="fieldValue">Field value to analyze</param>
    /// <returns>Confidence level (0-1)</returns>
    public double GetDetectionConfidence(string fieldValue)
    {
        if (string.IsNullOrWhiteSpace(fieldValue))
            return 0.0;

        if (IsObviouslyNotPhi(fieldValue))
            return 0.0;

        var maxConfidence = 0.0;

        foreach (var patternGroup in _patterns.Values)
        {
            foreach (var pattern in patternGroup)
            {
                if (pattern.IsMatch(fieldValue))
                {
                    var confidence = GetPatternConfidence(pattern, fieldValue);
                    maxConfidence = Math.Max(maxConfidence, confidence);
                }
            }
        }

        return maxConfidence;
    }

    /// <summary>
    /// Initializes regex patterns for different types of PHI.
    /// </summary>
    private static Dictionary<IdentifierType, List<Regex>> InitializePatterns()
    {
        var patterns = new Dictionary<IdentifierType, List<Regex>>();

        // Social Security Numbers
        patterns[IdentifierType.SocialSecurityNumber] = new List<Regex>
        {
            new(@"^\d{3}-?\d{2}-?\d{4}$", RegexOptions.Compiled), // SSN format
            new(@"\b\d{3}[-\s]?\d{2}[-\s]?\d{4}\b", RegexOptions.Compiled) // SSN in text
        };

        // Phone Numbers
        patterns[IdentifierType.PhoneNumber] = new List<Regex>
        {
            new(@"^\(?\d{3}\)?[-\s]?\d{3}[-\s]?\d{4}$", RegexOptions.Compiled), // US phone format
            new(@"^\+?1?[-\s]?\(?\d{3}\)?[-\s]?\d{3}[-\s]?\d{4}$", RegexOptions.Compiled), // US phone with country code
            new(@"\b\d{3}[-\.\s]\d{3}[-\.\s]\d{4}\b", RegexOptions.Compiled) // Phone in text
        };

        // Email Addresses
        patterns[IdentifierType.Email] = new List<Regex>
        {
            new(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"\b[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)
        };

        // Medical Record Numbers
        patterns[IdentifierType.MedicalRecordNumber] = new List<Regex>
        {
            new(@"^MRN\d{6,12}$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"^MR\d{6,12}$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"^\d{6,12}$", RegexOptions.Compiled), // Generic numeric ID
            new(@"^[A-Z]{2,4}\d{6,10}$", RegexOptions.Compiled) // Alpha-numeric ID
        };

        // Account Numbers
        patterns[IdentifierType.AccountNumber] = new List<Regex>
        {
            new(@"^ACCT\d{6,12}$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"^ACC\d{6,12}$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"^\d{8,15}$", RegexOptions.Compiled) // Long numeric strings
        };

        // Insurance IDs
        patterns[IdentifierType.InsuranceId] = new List<Regex>
        {
            new(@"^INS\d{6,12}$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"^[A-Z]{3}\d{6,9}[A-Z]?$", RegexOptions.Compiled), // Common insurance format
            new(@"^[A-Z]{2}\d{7,10}$", RegexOptions.Compiled) // Another common format
        };

        // Names (basic patterns - less reliable)
        patterns[IdentifierType.PatientName] = new List<Regex>
        {
            new(@"^[A-Z][a-z]+\^[A-Z][a-z]+", RegexOptions.Compiled), // Last^First format
            new(@"^[A-Z][a-z]+\s[A-Z][a-z]+$", RegexOptions.Compiled), // First Last format
            new(@"^[A-Z][A-Z]+\^[A-Z][A-Z]+", RegexOptions.Compiled) // LAST^FIRST format
        };

        // Addresses
        patterns[IdentifierType.Address] = new List<Regex>
        {
            new(@"^\d+\s+[A-Za-z\s]+(St|Ave|Rd|Blvd|Dr|Ln|Way|Ct|Pl)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"\b\d+\s+[A-Za-z\s]+(Street|Avenue|Road|Boulevard|Drive|Lane|Way|Court|Place)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)
        };

        // ZIP Codes
        patterns[IdentifierType.Other] = new List<Regex>
        {
            new(@"^\d{5}(-\d{4})?$", RegexOptions.Compiled), // ZIP code format
            new(@"^\d{5}\s*$", RegexOptions.Compiled) // Simple ZIP
        };

        // License Numbers
        patterns[IdentifierType.LicenseNumber] = new List<Regex>
        {
            new(@"^[A-Z]{1,2}\d{6,9}$", RegexOptions.Compiled), // Driver's license format
            new(@"^DL\d{6,10}$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"^LIC\d{6,10}$", RegexOptions.Compiled | RegexOptions.IgnoreCase)
        };

        // Device Identifiers
        patterns[IdentifierType.DeviceId] = new List<Regex>
        {
            new(@"^[A-Z0-9]{8,20}$", RegexOptions.Compiled), // Serial number format
            new(@"^DEV\d{6,12}$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new(@"^SN[A-Z0-9]{6,15}$", RegexOptions.Compiled | RegexOptions.IgnoreCase)
        };

        return patterns;
    }

    /// <summary>
    /// Initializes a set of common words that are obviously not PHI.
    /// </summary>
    private static HashSet<string> InitializeCommonWords()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Common medical terms
            "NORMAL", "ABNORMAL", "POSITIVE", "NEGATIVE", "PENDING", "COMPLETE",
            "ORDERED", "CANCELLED", "DISCONTINUED", "ACTIVE", "INACTIVE",
            
            // Common codes and values
            "M", "F", "U", "O", // Gender codes
            "A", "E", "I", "O", "P", "R", "N", // Patient class codes
            "Y", "N", // Yes/No flags
            
            // Common status values
            "NEW", "UPDATE", "DELETE", "ORIGINAL", "CORRECTED",
            "FINAL", "PRELIMINARY", "AMENDED",
            
            // Common units
            "MG", "ML", "KG", "LB", "CM", "IN", "MM", "L",
            
            // Days of week and months (abbreviated)
            "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN",
            "JAN", "FEB", "MAR", "APR", "MAY", "JUN",
            "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"
        };
    }

    /// <summary>
    /// Checks if a field value appears to be a de-identification placeholder.
    /// </summary>
    public bool IsDeIdentifiedPlaceholder(string? fieldValue)
    {
        if (string.IsNullOrWhiteSpace(fieldValue))
            return false;

        var trimmed = fieldValue.Trim();

        // Common replacement tokens
        if (string.Equals(trimmed, "REDACTED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "REMOVED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "ANONYMIZED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "DE-IDENTIFIED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "[REDACTED]", StringComparison.OrdinalIgnoreCase))
            return true;

        // XXXXX-style masks (e.g., XXXXX, XXXXX^XXXXX)
        if (Regex.IsMatch(trimmed, @"^[X]+(\^[X]+)*$", RegexOptions.IgnoreCase))
            return true;

        // All-zero numeric placeholders (e.g., 0000000000, 000000000, 00000)
        if (Regex.IsMatch(trimmed, @"^0{5,}$"))
            return true;

        // Synthetic ID prefixes (e.g., SYNTH001)
        if (trimmed.StartsWith("SYNTH", StringComparison.OrdinalIgnoreCase))
            return true;

        // Composite de-ID values with REDACTED components (e.g., REDACTED^^REDACTED^IL^00000)
        var components = trimmed.Split('^', '~');
        if (components.Length > 1 && components.All(c =>
            string.IsNullOrEmpty(c) ||
            string.Equals(c, "REDACTED", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(c, @"^0{3,}$") ||
            c.Length <= 2))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a field value is obviously not PHI.
    /// </summary>
    private bool IsObviouslyNotPhi(string fieldValue)
    {
        if (string.IsNullOrWhiteSpace(fieldValue))
            return true;

        // Single characters or very short strings are usually codes, not PHI
        if (fieldValue.Length <= 2)
            return true;

        // Common medical codes and values
        if (_commonWords.Contains(fieldValue.Trim()))
            return true;

        // De-identification placeholders are not real PHI
        if (IsDeIdentifiedPlaceholder(fieldValue))
            return true;

        // Numeric values that are clearly measurements, not identifiers
        if (IsNumericMeasurement(fieldValue))
            return true;

        // Date/time values in standard formats (not identifying by themselves in this context)
        if (IsStandardDateTime(fieldValue))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a value appears to be a numeric measurement rather than an identifier.
    /// </summary>
    private static bool IsNumericMeasurement(string value)
    {
        // Decimal numbers with common medical units
        var measurementPattern = new Regex(@"^\d+\.?\d*\s*(mg|ml|kg|lb|cm|in|mm|l|g|oz|ft|yd)$", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        
        if (measurementPattern.IsMatch(value))
            return true;

        // Simple decimal numbers (lab values)
        if (decimal.TryParse(value, out var numValue))
        {
            // Very large numbers are more likely to be identifiers
            if (numValue > 1_000_000)
                return false;
            
            // Numbers with decimals are usually measurements
            if (value.Contains('.'))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a value appears to be a standard date/time format.
    /// </summary>
    private static bool IsStandardDateTime(string value)
    {
        // HL7 date/time formats
        var dateTimePatterns = new[]
        {
            @"^\d{4}$", // Year
            @"^\d{6}$", // YYYYMM
            @"^\d{8}$", // YYYYMMDD
            @"^\d{10}$", // YYYYMMDDHH
            @"^\d{12}$", // YYYYMMDDHHMM
            @"^\d{14}$" // YYYYMMDDHHMMSS
        };

        return dateTimePatterns.Any(pattern => Regex.IsMatch(value, pattern));
    }

    /// <summary>
    /// Gets the weight of a pattern for scoring identifier types.
    /// </summary>
    private static double GetPatternWeight(Regex pattern)
    {
        var patternString = pattern.ToString();
        
        // More specific patterns get higher weights
        if (patternString.Contains("^") && patternString.Contains("$"))
            return 1.0; // Exact match patterns
        
        if (patternString.Contains(@"\b"))
            return 0.8; // Word boundary patterns
        
        return 0.6; // General patterns
    }

    /// <summary>
    /// Gets the confidence level for a specific pattern match.
    /// </summary>
    private static double GetPatternConfidence(Regex pattern, string value)
    {
        var patternString = pattern.ToString();
        
        // Higher confidence for more specific patterns
        if (patternString.Contains("SSN") || patternString.Contains("MRN"))
            return 0.95;
        
        if (patternString.Contains("@") && patternString.Contains("."))
            return 0.9; // Email patterns
        
        if (patternString.Contains("^") && patternString.Contains("$"))
            return 0.85; // Exact format matches
        
        if (value.Length > 20)
            return 0.4; // Very long strings are less likely to be simple identifiers
        
        return 0.7; // Default confidence
    }
}