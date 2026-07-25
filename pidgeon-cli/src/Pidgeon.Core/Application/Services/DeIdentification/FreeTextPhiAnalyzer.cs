// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.RegularExpressions;
using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Application.Services.DeIdentification;

/// <summary>
/// Detects and scrubs PHI embedded in free-text narrative. Detects SSNs, dates, phone numbers, emails, URLs,
/// IPv4 addresses, street addresses, and person names by offset, and replaces each detected span with a typed,
/// HL7-delimiter-safe synthetic while retaining the surrounding clinical prose. Pure functions over the input
/// text — no state, no I/O. Deterministic and on-device; a model recall-booster is optional and never a
/// dependency.
///
/// Consumed today by the HL7 narrative fields (OBX-5 / NTE-3 / TXA-12) via <c>HL7DeIdentifier</c>. Wiring the
/// same scrub into FHIR <c>Annotation.text</c> and NCPDP Sig/notes is a downstream follow-up (§10 of the
/// de-identification defensibility standard).
/// </summary>
internal static class FreeTextPhiAnalyzer
{
    // Separators optional (dash, space, or none), matching the residual-scan detector (PhiPatternDetector's
    // "SSN in text" pattern). If the scrub detector were narrower than the verdict detector, a form the scrub
    // misses but the verdict catches (e.g. "123 45 6789") would survive and then be laundered as "compliant".
    private static readonly Regex SsnPattern = new(@"\b\d{3}[-\s]?\d{2}[-\s]?\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex DatePattern1 = new(@"\b\d{1,2}/\d{1,2}/\d{2,4}\b", RegexOptions.Compiled);
    private static readonly Regex DatePattern2 = new(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex PhonePattern = new(@"\b\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"\b[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b", RegexOptions.Compiled);
    private static readonly Regex UrlPattern = new(@"\bhttps?://[^\s|^~\\&]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex IpV4Pattern = new(
        @"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled);
    // Digit and word counts unbounded to stay a superset of the verdict's "in text" address pattern
    // (PhiPatternDetector: \b\d+\s+[A-Za-z\s]+(Street|Avenue|...)\b) — a bounded scrub pattern would miss a
    // long address the verdict catches, and the whole-field emitted record would then launder it as compliant.
    private static readonly Regex StreetAddressPattern = new(
        @"\b\d+\s+[A-Za-z\s]+(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Drive|Dr|Lane|Ln|Way|Court|Ct|Place|Pl|Terrace|Ter|Circle|Cir)\b\.?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Fail-closed trigger: a narrative field longer than this, or one carrying control characters, is redacted
    // whole rather than partially scanned. Structural uncertainty gets the safe failure (over-redaction);
    // pass-through is never the failure mode for a narrative.
    private const int MaxScannableLength = 10_000;

    private static readonly Regex Hl7DelimiterPattern = new(@"[|^~\\&]", RegexOptions.Compiled);

    public static FreeTextPhiDetectionResult Analyze(string freeText)
    {
        if (string.IsNullOrWhiteSpace(freeText))
        {
            return new FreeTextPhiDetectionResult
            {
                DetectedItems = Array.Empty<FreeTextPhiItem>(),
                OverallConfidence = 1.0,
                NamedEntities = Array.Empty<NamedEntity>(),
                ClinicalConcepts = Array.Empty<ClinicalConcept>()
            };
        }

        var detectedItems = new List<FreeTextPhiItem>();

        AddMatches(detectedItems, freeText, SsnPattern, IdentifierType.SocialSecurityNumber, 0.95);
        AddMatches(detectedItems, freeText, DatePattern1, IdentifierType.Other, 0.80);
        AddMatches(detectedItems, freeText, DatePattern2, IdentifierType.Other, 0.85);
        AddMatches(detectedItems, freeText, PhonePattern, IdentifierType.PhoneNumber, 0.85);
        AddMatches(detectedItems, freeText, EmailPattern, IdentifierType.Email, 0.90);
        AddMatches(detectedItems, freeText, UrlPattern, IdentifierType.Url, 0.90);
        AddMatches(detectedItems, freeText, IpV4Pattern, IdentifierType.IpAddress, 0.85);
        AddMatches(detectedItems, freeText, StreetAddressPattern, IdentifierType.Address, 0.75);
        detectedItems.AddRange(NarrativeNameDetector.DetectNameSpans(freeText));

        var overallConfidence = detectedItems.Count > 0
            ? detectedItems.Average(item => item.Confidence)
            : 1.0;

        return new FreeTextPhiDetectionResult
        {
            DetectedItems = detectedItems,
            OverallConfidence = overallConfidence,
            NamedEntities = Array.Empty<NamedEntity>(),
            ClinicalConcepts = Array.Empty<ClinicalConcept>()
        };
    }

    /// <summary>
    /// Scans a narrative field and replaces each detected PHI span with a typed, HL7-delimiter-safe synthetic
    /// via <paramref name="context"/>, retaining the surrounding prose. Fails closed (whole-field synthetic)
    /// when the text is structurally unsuitable for a partial scan (over length or control characters).
    /// Every emitted replacement is recorded so the residual-scan compliance verdict treats it as a synthetic,
    /// not a leak.
    /// </summary>
    public static string Scrub(string freeText, DeIdentificationContext context)
    {
        if (string.IsNullOrEmpty(freeText))
            return freeText;

        // A free-text field is best-effort scrubbed and cannot be certified by a whole-field residual scanner,
        // so record that this message carried free-text: the compliance verdict will report it Unknown, never
        // Safe-Harbor-certified, regardless of what the scan does or does not detect in it.
        context.MarkFreeTextProcessed();

        // Fail-closed on structural uncertainty: over-length or control-character content is redacted whole
        // rather than partially scanned.
        if (freeText.Length > MaxScannableLength || ContainsControlCharacters(freeText))
            return RedactWhole(freeText, context);

        var spans = ResolveOverlaps(Analyze(freeText).DetectedItems);
        if (spans.Count == 0)
            return freeText;

        // Detect as broadly as the residual-scan detector (PhiPatternDetector's "in text" SSN/phone/email/
        // address patterns) so the scrubbed OUTPUT removes what a reviewer would flag — a narrower scan would
        // leave real PHI in the field. The compliance verdict reports free-text fields as
        // not-independently-verifiable regardless, so this is about output completeness, not certification.
        var builder = new StringBuilder(freeText);
        foreach (var span in spans.OrderByDescending(s => s.StartPosition))
        {
            var synthetic = MakeHl7Safe(context.GetOrCreateSyntheticId(span.Content, span.Type));
            builder.Remove(span.StartPosition, span.EndPosition - span.StartPosition);
            builder.Insert(span.StartPosition, synthetic);
        }

        return builder.ToString();
    }

    private static string RedactWhole(string original, DeIdentificationContext context)
        => MakeHl7Safe(context.GetOrCreateSyntheticId(original, IdentifierType.FreeText));

    private static void AddMatches(
        List<FreeTextPhiItem> items, string text, Regex pattern, IdentifierType type, double confidence)
    {
        foreach (Match match in pattern.Matches(text))
        {
            items.Add(new FreeTextPhiItem
            {
                Type = type,
                StartPosition = match.Index,
                EndPosition = match.Index + match.Length,
                Content = match.Value,
                Confidence = confidence,
                Context = GetSurroundingContext(text, match.Index, match.Length)
            });
        }
    }

    // Prefer the longest span at the earliest start; drop spans that overlap an already-selected one, so a
    // name inside an email (or any nested detection) does not produce colliding replacements.
    private static List<FreeTextPhiItem> ResolveOverlaps(IReadOnlyList<FreeTextPhiItem> items)
    {
        var selected = new List<FreeTextPhiItem>();
        var coveredUntil = 0;
        foreach (var item in items
            .Where(i => i.EndPosition > i.StartPosition)
            .OrderBy(i => i.StartPosition)
            .ThenByDescending(i => i.EndPosition - i.StartPosition))
        {
            if (item.StartPosition >= coveredUntil)
            {
                selected.Add(item);
                coveredUntil = item.EndPosition;
            }
        }
        return selected;
    }

    private static bool ContainsControlCharacters(string text)
        => text.Any(c => char.IsControl(c) && c != '\t');

    // Strip HL7 delimiters from a synthetic so a replacement can never corrupt the field structure — the
    // PatientName synthetic is SURNAME^GIVEN, and an injected ^ would split the narrative into components.
    private static string MakeHl7Safe(string value)
        => Hl7DelimiterPattern.Replace(value, " ").Trim();

    private static string GetSurroundingContext(string text, int position, int length, int contextChars = 20)
    {
        var start = Math.Max(0, position - contextChars);
        var end = Math.Min(text.Length, position + length + contextChars);
        return text[start..end];
    }
}
