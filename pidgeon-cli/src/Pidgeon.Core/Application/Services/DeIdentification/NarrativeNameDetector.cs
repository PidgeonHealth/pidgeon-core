// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Application.Services.DeIdentification;

/// <summary>
/// Detects person-name spans embedded in free-text narrative. This is the deterministic, on-device recall
/// FLOOR for names the field-position rules never inspect: a common-names gazetteer catches frequent names,
/// and a title/relationship cue-adjacency heuristic catches cue-anchored names the gazetteer misses
/// (e.g. "Mr Robert Chen", "Patient JOHN DOE"). A rare name with no cue is the acknowledged gap an optional
/// model recall-booster addresses — never closed by over-redacting every capitalized token, which would
/// destroy legitimate clinical terms.
///
/// ponytail: inline ~150-name gazetteer; broaden it or swap to the Pidgeon.Data name corpus if recall matters.
/// </summary>
internal static class NarrativeNameDetector
{
    // Words that immediately precede a person name in clinical prose. A capitalized token right after one of
    // these is treated as the start of a name span.
    private static readonly HashSet<string> Cues = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "miss", "dr", "doctor", "patient", "pt", "daughter", "son", "spouse",
        "wife", "husband", "mother", "father", "mom", "dad", "sister", "brother", "guardian", "parent",
        "nurse", "provider", "named", "aka"
        // Deliberately NOT generic prepositions ("with", "by", "seen"): "treated with Albuterol",
        // "followed by Neurology", "last seen Monday" would sweep a clinical/department term into a name span.
    };

    // Capitalized clinical/label tokens that are never a person name — they terminate a name run so a label
    // like "DOB" or "SSN" following a name is not swallowed into the redacted span.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "dob", "dod", "ssn", "mrn", "npi", "dea", "dx", "hx", "rx", "id", "tx", "po", "iv", "er", "ed", "icu"
    };

    // Common US given names and surnames. Case-insensitive; used only to seed detection, not to bound it —
    // the adjacency pass extends a span to capitalized neighbors so a surname absent from the list is caught.
    private static readonly HashSet<string> Gazetteer = new(StringComparer.OrdinalIgnoreCase)
    {
        // Given names
        "james", "mary", "john", "patricia", "robert", "jennifer", "michael", "linda", "william", "elizabeth",
        "david", "barbara", "richard", "susan", "joseph", "jessica", "thomas", "sarah", "charles", "karen",
        "christopher", "nancy", "daniel", "lisa", "matthew", "betty", "anthony", "margaret", "mark", "sandra",
        "donald", "ashley", "steven", "kimberly", "paul", "emily", "andrew", "donna", "joshua", "michelle",
        "kenneth", "carol", "kevin", "amanda", "brian", "dorothy", "george", "melissa", "edward", "deborah",
        "ronald", "stephanie", "timothy", "rebecca", "jason", "sharon", "jeffrey", "laura", "ryan", "cynthia",
        "jacob", "kathleen", "gary", "amy", "nicholas", "angela", "eric", "shirley", "jonathan", "anna",
        "stephen", "brenda", "larry", "pamela", "justin", "nicole", "scott", "ruth", "brandon", "katherine",
        // Surnames
        "smith", "johnson", "williams", "brown", "jones", "garcia", "miller", "davis", "rodriguez", "martinez",
        "hernandez", "lopez", "gonzalez", "wilson", "anderson", "taylor", "moore", "jackson", "martin", "lee",
        "perez", "thompson", "white", "harris", "sanchez", "clark", "ramirez", "lewis", "robinson", "walker",
        "young", "allen", "king", "wright", "scott", "torres", "nguyen", "hill", "flores", "green",
        "adams", "nelson", "baker", "hall", "rivera", "campbell", "mitchell", "carter", "roberts", "gomez",
        "phillips", "evans", "turner", "diaz", "parker", "cruz", "edwards", "collins", "reyes", "stewart",
        "morris", "morales", "murphy", "cook", "rogers", "gutierrez", "ortiz", "morgan", "cooper", "peterson",
        "bailey", "reed", "kelly", "howard", "ramos", "kim", "cox", "richardson", "watson",
        "chen", "wang", "patel", "singh", "kumar"
    };

    private static readonly Regex TokenPattern = new(@"[A-Za-z][A-Za-z'\-]*", RegexOptions.Compiled);

    // A neighbor that corroborates a gazetteer hit as a real name: a capitalized token that is not itself a
    // cue or a label stopword (so "DOB"/"SSN" beside a surname don't falsely corroborate).
    private static bool IsNameEligibleNeighbor(string token)
        => token.Length > 0 && char.IsUpper(token[0])
           && !Cues.Contains(token) && !StopWords.Contains(token);

    /// <summary>
    /// Returns the person-name spans found in <paramref name="text"/> as typed PHI items (PatientName).
    /// </summary>
    public static IEnumerable<FreeTextPhiItem> DetectNameSpans(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var tokens = TokenPattern.Matches(text);
        var isName = new bool[tokens.Count];

        // Seed: a capitalized token right after a cue, or a capitalized gazetteer hit in a name-shaped context.
        // Cue and stop words are never themselves part of a name span. All adjacency is whitespace-only, so a
        // sentence boundary or any punctuation between a name and the next word stops the span from crossing it.
        for (int i = 0; i < tokens.Count; i++)
        {
            var val = tokens[i].Value;
            if (Cues.Contains(val) || StopWords.Contains(val))
                continue;

            var capitalized = char.IsUpper(val[0]);
            var afterCue = i > 0 && capitalized
                && Cues.Contains(tokens[i - 1].Value)
                && WhitespaceBetween(text, tokens[i - 1], tokens[i]);

            // A capitalized gazetteer hit alone is not enough — common words double as surnames ("White" in
            // "White blood cell", "Cook", "Young"). Seed it from the gazetteer only when corroborated by a cue
            // or a whitespace-adjacent capitalized token. A lone capitalized surname amid lowercase prose is
            // left alone (an accepted recall gap; the model booster is the recall path).
            var gazetteerHit = capitalized && Gazetteer.Contains(val)
                && (afterCue
                    || (i > 0 && IsNameEligibleNeighbor(tokens[i - 1].Value)
                        && WhitespaceBetween(text, tokens[i - 1], tokens[i]))
                    || (i < tokens.Count - 1 && IsNameEligibleNeighbor(tokens[i + 1].Value)
                        && WhitespaceBetween(text, tokens[i], tokens[i + 1])));

            isName[i] = afterCue || gazetteerHit;
        }

        // Extend: a capitalized token whitespace-adjacent to a name candidate joins the same name (catches a
        // surname absent from the gazetteer, e.g. "DOE" beside "JOHN"). Iterate to a fixpoint for multi-token
        // names. Whitespace-only adjacency keeps "JOHN DOE. Chest CT" from swallowing "Chest CT".
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (isName[i]) continue;
                var val = tokens[i].Value;
                if (Cues.Contains(val) || StopWords.Contains(val) || !char.IsUpper(val[0]))
                    continue;

                var adjacentToName =
                    (i > 0 && isName[i - 1] && WhitespaceBetween(text, tokens[i - 1], tokens[i]))
                    || (i < tokens.Count - 1 && isName[i + 1] && WhitespaceBetween(text, tokens[i], tokens[i + 1]));
                if (adjacentToName)
                {
                    isName[i] = true;
                    changed = true;
                }
            }
        }

        // Merge consecutive, whitespace-separated name tokens into spans. A non-whitespace gap (punctuation,
        // sentence end) between two name tokens closes the current span rather than spanning across it.
        int spanStart = -1;
        int spanEnd = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (!isName[i])
            {
                if (spanStart >= 0)
                {
                    yield return NameSpan(text, spanStart, spanEnd);
                    spanStart = -1;
                }
                continue;
            }

            if (spanStart >= 0 && !WhitespaceBetween(text, tokens[i - 1], tokens[i]))
            {
                yield return NameSpan(text, spanStart, spanEnd);
                spanStart = -1;
            }

            if (spanStart < 0)
                spanStart = tokens[i].Index;
            spanEnd = tokens[i].Index + tokens[i].Length;
        }

        if (spanStart >= 0)
            yield return NameSpan(text, spanStart, spanEnd);
    }

    private static FreeTextPhiItem NameSpan(string text, int start, int end)
        => new()
        {
            Type = IdentifierType.PatientName,
            StartPosition = start,
            EndPosition = end,
            Content = text.Substring(start, end - start),
            Confidence = 0.75
        };

    // True if only whitespace lies between token a and the later token b — so a sentence boundary or any
    // punctuation between a name and the next word stops a name span from crossing it.
    private static bool WhitespaceBetween(string text, Match a, Match b)
    {
        for (int k = a.Index + a.Length; k < b.Index; k++)
            if (!char.IsWhiteSpace(text[k]))
                return false;
        return true;
    }
}
