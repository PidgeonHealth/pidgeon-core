// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Messaging;

namespace Pidgeon.Core.Application.Services.Generation;

/// <summary>
/// CLI-facing presentation helpers for the generate command: smart, context-aware
/// error suggestions when a message type isn't recognized, plus shell-safe message-type
/// normalization for generation. The message-type → standard map itself is domain
/// knowledge and lives in <see cref="MessageTypeVocabulary"/> (Domain/Messaging).
/// </summary>
public class MessageTypeRegistry
{
    /// <summary>
    /// Generates smart error suggestions based on attempted message type.
    /// Provides context-aware suggestions for each healthcare standard.
    /// </summary>
    /// <param name="attemptedMessageType">The message type that failed</param>
    /// <returns>Helpful error message with suggestions</returns>
    public static string GetSmartErrorSuggestion(string attemptedMessageType)
    {
        if (string.IsNullOrWhiteSpace(attemptedMessageType))
            return GetGenericSuggestions();

        var attempted = attemptedMessageType.Trim();

        // HL7 prefix matching (shell ate the suffix)
        if (IsHL7Prefix(attempted))
            return GetHL7Suggestions(attempted);

        // FHIR typo detection
        if (IsFHIRLikeTypo(attempted))
            return GetFHIRSuggestions(attempted);

        // NCPDP variant detection
        if (IsNCPDPLike(attempted))
            return GetNCPDPSuggestions(attempted);

        // Generic fallback
        return GetGenericSuggestions();
    }

    private static bool IsHL7Prefix(string attempted)
    {
        // Common HL7 prefixes that might have shell-eaten suffixes
        var hl7Prefixes = new[] { "ADT", "ORU", "ORM", "OML", "RDE", "SIU", "MDM", "DFT", "QRY", "ACK" };
        return hl7Prefixes.Any(prefix => attempted.Equals(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFHIRLikeTypo(string attempted)
    {
        // Check if it looks like a FHIR resource with typos
        var fhirResources = new[] { "Patient", "Observation", "Encounter", "MedicationRequest", "Bundle", "Appointment" };

        // Simple Levenshtein-like check - if it's close to a FHIR resource name
        return fhirResources.Any(resource =>
            Math.Abs(resource.Length - attempted.Length) <= 2 &&
            resource.ToLowerInvariant().Contains(attempted.ToLowerInvariant().Substring(0, Math.Min(3, attempted.Length))));
    }

    private static bool IsNCPDPLike(string attempted)
    {
        // NCPDP patterns: Rx-prefixed or pharmacy terms
        return attempted.StartsWith("Rx", StringComparison.OrdinalIgnoreCase) ||
               attempted.Contains("formulary", StringComparison.OrdinalIgnoreCase) ||
               attempted.Contains("prior", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetHL7Suggestions(string prefix)
    {
        var suggestions = prefix.ToUpperInvariant() switch
        {
            "ADT" => new[] { ("ADT^A01", "Patient admission"), ("ADT^A03", "Patient discharge"), ("ADT^A08", "Update patient info") },
            "ORU" => new[] { ("ORU^R01", "Lab results"), ("ORU^R03", "Display oriented results") },
            "ORM" => new[] { ("ORM^O01", "General order"), ("ORM^O02", "Order response") },
            "RDE" => new[] { ("RDE^O11", "Pharmacy order"), ("RDE^O25", "Refill authorization") },
            "SIU" => new[] { ("SIU^S12", "New appointment"), ("SIU^S13", "Reschedule appointment") },
            "ACK" => new[] { ("ACK", "General acknowledgment") },
            _ => new[] { ("ADT^A01", "Patient admission"), ("ORU^R01", "Lab results") }
        };

        var suggestionText = string.Join("\n", suggestions.Select(s => $"  pidgeon generate \"{s.Item1}\"    # {s.Item2}"));

        return $"Message type '{prefix}' incomplete.\n\nCommon HL7 {prefix} messages:\n{suggestionText}";
    }

    private static string GetFHIRSuggestions(string attempted)
    {
        // Find closest FHIR resources
        var suggestions = new[] { "Patient", "Observation", "Encounter", "MedicationRequest", "Bundle", "Appointment" };

        var closest = suggestions
            .OrderBy(s => LevenshteinDistance(s.ToLowerInvariant(), attempted.ToLowerInvariant()))
            .Take(3)
            .ToArray();

        var suggestionText = string.Join("\n", closest.Select(s => $"  pidgeon generate {s}"));

        return $"Message type '{attempted}' not recognized.\n\nDid you mean:\n{suggestionText}";
    }

    private static string GetNCPDPSuggestions(string attempted)
    {
        var suggestions = new[] { ("NewRx", "New prescription"), ("RxFill", "Fill notification"), ("CancelRx", "Cancel prescription") };

        var suggestionText = string.Join("\n", suggestions.Select(s => $"  pidgeon generate {s.Item1}      # {s.Item2}"));

        return $"Message type '{attempted}' not recognized.\n\nCommon NCPDP messages:\n{suggestionText}";
    }

    private static string GetGenericSuggestions()
    {
        return "Message type not recognized.\n\n" +
               "Examples:\n" +
               "  pidgeon generate \"ADT^A01\"    # HL7 admission\n" +
               "  pidgeon generate Patient      # FHIR patient\n" +
               "  pidgeon generate NewRx        # NCPDP prescription\n\n" +
               "Run 'pidgeon generate --help' for more options.";
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        var matrix = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++) matrix[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++) matrix[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1), matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[s1.Length, s2.Length];
    }

    /// <summary>
    /// Normalizes message type for generation service consumption.
    /// Converts shell-safe alternatives to canonical format.
    /// </summary>
    /// <param name="messageType">Message type to normalize (e.g., ADT-A01, ADT_A01, ADT.A01)</param>
    /// <returns>Normalized message type (e.g., ADT^A01)</returns>
    public static string NormalizeForGeneration(string messageType)
    {
        return MessageTypeVocabulary.Normalize(messageType);
    }
}
