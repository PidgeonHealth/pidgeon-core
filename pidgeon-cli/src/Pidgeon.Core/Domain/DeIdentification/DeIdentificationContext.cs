// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.DeIdentification;

/// <summary>
/// Context maintaining state across message de-identification operations.
/// Ensures consistency and referential integrity for batch processing.
/// </summary>
public record DeIdentificationContext
{
    /// <summary>
    /// Team-specific salt for deterministic hashing.
    /// Same salt produces same synthetic identifiers for reproducible test scenarios.
    /// </summary>
    public required string Salt { get; init; }

    /// <summary>
    /// Date offset to apply to all date fields.
    /// Positive values shift dates forward, negative values shift backward.
    /// </summary>
    public required TimeSpan DateShift { get; init; }

    /// <summary>
    /// Mapping of original identifiers to synthetic replacements.
    /// Maintains consistency across messages (same patient ID → same synthetic ID).
    /// </summary>
    public Dictionary<string, string> IdMap { get; init; } = new();

    /// <summary>
    /// Set of identifiers already processed to detect duplicates and ensure consistency.
    /// </summary>
    public HashSet<string> ProcessedIdentifiers { get; init; } = new();

    /// <summary>
    /// Counter for generating sequential synthetic identifiers when needed.
    /// </summary>
    public int SequenceCounter { get; set; } = 1;

    /// <summary>
    /// Audit trail of de-identification actions performed.
    /// Maps original field values to synthetic replacements for compliance reporting.
    /// </summary>
    public Dictionary<string, DeIdentificationAction> AuditTrail { get; init; } = new();

    /// <summary>
    /// Exact output values the de-identifier emitted (whole HL7 field strings). A scanned value counts as
    /// residual PHI only if it is NOT in this set, which is how a realistic synthetic replacement is told
    /// apart from real PHI that survived.
    /// </summary>
    public HashSet<string> EmittedValues { get; init; } = new();

    /// <summary>Records a value the de-identifier wrote into an output field, for the compliance verdict.</summary>
    public void RecordEmitted(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            EmittedValues.Add(value);
    }

    /// <summary>
    /// Set when a free-text (narrative) field was processed. Free-text de-identification is best-effort — a
    /// whole-field residual scanner cannot certify that no name-in-prose survived — so a message that contained
    /// free-text is reported as not-independently-verifiable (Unknown), never Safe-Harbor-certified.
    /// </summary>
    public bool ContainsFreeText { get; private set; }

    /// <summary>Marks that a free-text field was processed, so the compliance verdict never certifies it.</summary>
    public void MarkFreeTextProcessed() => ContainsFreeText = true;

    /// <summary>
    /// Timestamp when this context was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or creates a consistent synthetic identifier for the given original value.
    /// Uses deterministic hashing based on salt to ensure reproducibility.
    /// </summary>
    /// <param name="original">Original identifier to replace</param>
    /// <param name="identifierType">Type of identifier (MRN, Name, etc.) for proper formatting</param>
    /// <returns>Consistent synthetic identifier</returns>
    public string GetOrCreateSyntheticId(string original, IdentifierType identifierType)
    {
        if (string.IsNullOrWhiteSpace(original))
            return string.Empty;

        // Check if we've already mapped this identifier
        if (IdMap.TryGetValue(original, out var existingId))
            return existingId;

        // Generate new synthetic identifier
        var syntheticId = GenerateDeterministicId(original, identifierType);
        IdMap[original] = syntheticId;
        ProcessedIdentifiers.Add(original);

        // Audit trail references the original by a salted, non-reversible hash — never the cleartext
        // value — so the compliance report and any serialized result carry no recoverable PHI.
        var reference = ComputeAuditReference(original);
        AuditTrail[reference] = new DeIdentificationAction
        {
            OriginalValueHash = reference,
            SyntheticValue = syntheticId,
            IdentifierType = identifierType,
            Action = "REPLACE",
            Timestamp = DateTime.UtcNow
        };

        return syntheticId;
    }

    /// <summary>
    /// Shifts a date by the configured offset while preserving relationships.
    /// </summary>
    /// <param name="originalDate">Original date to shift</param>
    /// <param name="preserveTime">Whether to preserve time component</param>
    /// <returns>Shifted date</returns>
    public DateTime ShiftDate(DateTime originalDate, bool preserveTime = true)
    {
        if (preserveTime)
            return originalDate.Add(DateShift);
        else
            return originalDate.Date.Add(DateShift);
    }

    /// <summary>
    /// Generates a deterministic synthetic identifier based on the original value and salt.
    /// Same input + salt always produces the same output.
    /// </summary>
    /// <summary>
    /// Applies the HIPAA Safe Harbor ZIP rule (45 CFR 164.514(b)(2)(i)(B)): retain the first three digits,
    /// except that the initial three digits of a ZIP whose population is 20,000 or fewer must be changed to
    /// 000. A null, empty, or under-three-digit ZIP is fully suppressed to 00000.
    /// </summary>
    public static string RedactZip(string? zip)
    {
        if (string.IsNullOrWhiteSpace(zip) || zip.Length < 3)
            return "00000";

        var prefix = zip[..3];
        return RestrictedZipPrefixes.Contains(prefix) ? "00000" : prefix + "00";
    }

    // The three-digit ZIP prefixes whose population is 20,000 or fewer (HHS Safe Harbor list); these must
    // be changed to 000 rather than retained.
    private static readonly HashSet<string> RestrictedZipPrefixes = new()
    {
        "036", "059", "063", "102", "203", "556", "692", "790",
        "821", "823", "830", "831", "878", "879", "884", "890", "893"
    };

    private string GenerateDeterministicId(string original, IdentifierType identifierType)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var input = $"{Salt}:{identifierType}:{original}";
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        var base64 = Convert.ToBase64String(hash);

        return identifierType switch
        {
            IdentifierType.MedicalRecordNumber => $"MRN{base64.Substring(0, 8).ToUpper().Replace("/", "").Replace("+", "")}",
            IdentifierType.PatientName => GenerateSyntheticName(base64),
            IdentifierType.SocialSecurityNumber => GenerateSyntheticSSN(base64),
            IdentifierType.PhoneNumber => GenerateSyntheticPhone(base64),
            IdentifierType.Address => GenerateSyntheticAddress(base64),
            IdentifierType.Email => GenerateSyntheticEmail(base64),
            IdentifierType.AccountNumber => $"ACCT{base64.Substring(0, 10).ToUpper().Replace("/", "").Replace("+", "")}",
            IdentifierType.InsuranceId => $"INS{base64.Substring(0, 9).ToUpper().Replace("/", "").Replace("+", "")}",
            _ => $"ID{base64.Substring(0, 8).ToUpper().Replace("/", "").Replace("+", "")}"
        };
    }

    private static string GenerateSyntheticName(string base64)
    {
        // Use deterministic hash to select from predefined name lists for consistent results
        var deterministicHash = GetDeterministicHash(base64);
        var surnames = new[] { "SMITH", "JOHNSON", "WILLIAMS", "BROWN", "JONES", "GARCIA", "MILLER", "DAVIS", "RODRIGUEZ", "MARTINEZ" };
        var givenNames = new[] { "JAMES", "MARY", "JOHN", "PATRICIA", "ROBERT", "JENNIFER", "MICHAEL", "LINDA", "WILLIAM", "ELIZABETH" };
        
        var surname = surnames[deterministicHash % (uint)surnames.Length];
        var givenName = givenNames[(deterministicHash >> 8) % (uint)givenNames.Length];
        
        return $"{surname}^{givenName}";
    }

    private static string GenerateSyntheticSSN(string base64)
    {
        // Generate valid SSN format but with invalid area numbers to avoid real SSN conflicts
        var hash = GetDeterministicHash(base64);
        var area = 900 + (hash % 99);  // 900-999 are invalid SSN area numbers
        var group = 1 + ((hash >> 8) % 99);
        var serial = 1 + ((hash >> 16) % 9999);
        return $"{area:D3}{group:D2}{serial:D4}";
    }

    private static string GenerateSyntheticPhone(string base64)
    {
        // Generate phone with 555 area code (reserved for fictional use)
        var hash = GetDeterministicHash(base64);
        var exchange = 100 + (hash % 800);  // Valid exchange codes
        var number = 1000 + ((hash >> 10) % 9000);
        return $"555{exchange:D3}{number:D4}";
    }

    private static string GenerateSyntheticAddress(string base64)
    {
        var hash = GetDeterministicHash(base64);
        var streetNumbers = new[] { "123", "456", "789", "101", "202", "303", "404", "505" };
        var streetNames = new[] { "MAIN ST", "ELM AVE", "OAK RD", "PINE BLVD", "MAPLE DR", "CEDAR LN", "PARK WAY", "FIRST ST" };
        
        var streetNumber = streetNumbers[hash % (uint)streetNumbers.Length];
        var streetName = streetNames[(hash >> 8) % (uint)streetNames.Length];
        
        return $"{streetNumber} {streetName}";
    }

    private static string GenerateSyntheticEmail(string base64)
    {
        var hash = GetDeterministicHash(base64);
        var domains = new[] { "example.com", "test.org", "sample.net", "demo.edu", "fake.gov" };
        var domain = domains[hash % (uint)domains.Length];
        var user = base64.Substring(0, 8).ToLower().Replace("/", "").Replace("+", "");
        
        return $"{user}@{domain}";
    }

    /// <summary>
    /// Generates a deterministic hash from string input using FNV-1a algorithm.
    /// Unlike .NET's GetHashCode(), this is guaranteed to be consistent across runs.
    /// </summary>
    private static uint GetDeterministicHash(string input)
    {
        // FNV-1a hash algorithm for consistent hashing
        uint hash = 2166136261u; // FNV offset basis
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(input))
        {
            hash ^= b;
            hash *= 16777619u; // FNV prime
        }
        return hash;
    }

    /// <summary>
    /// Produces a salted, non-reversible reference to an original value for the audit trail.
    /// The salt makes low-entropy identifiers (SSN, ZIP, date of birth) infeasible to brute-force,
    /// so the audit report never carries recoverable PHI.
    /// </summary>
    private string ComputeAuditReference(string original)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{Salt}:audit:{original}"));
        return Convert.ToHexString(hash)[..16];
    }

    // Helper methods for statistics tracking

    /// <summary>
    /// Gets the ID mappings for reporting, keyed by the salted hash reference rather than the
    /// cleartext original, so the exposed map preserves consistency and combination without being
    /// a re-identification crosswalk. The internal <see cref="IdMap"/> stays cleartext-keyed for
    /// within-run lookup.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetIdMappings()
    {
        var redacted = new Dictionary<string, string>();
        foreach (var kvp in IdMap)
            redacted[ComputeAuditReference(kvp.Key)] = kvp.Value;
        return redacted;
    }

    /// <summary>
    /// Gets the count of processed identifiers.
    /// </summary>
    public int GetProcessedIdentifierCount() => ProcessedIdentifiers.Count;

    /// <summary>
    /// Gets the count of identifiers by type from audit trail.
    /// </summary>
    public IReadOnlyDictionary<IdentifierType, int> GetIdentifiersByType()
    {
        return AuditTrail.Values
            .GroupBy(action => action.IdentifierType)
            .ToDictionary(group => group.Key, group => group.Count());
    }

    /// <summary>
    /// Gets the count of modified fields (all audit trail entries).
    /// </summary>
    public int GetModifiedFieldCount() => AuditTrail.Count;

    /// <summary>
    /// Gets the count of shifted dates from audit trail.
    /// </summary>
    public int GetShiftedDateCount()
    {
        return AuditTrail.Values.Count(action => action.Action == "SHIFT");
    }

    /// <summary>
    /// Gets the count of unique subjects processed.
    /// For now, approximated by counting unique patient-related identifiers.
    /// </summary>
    public int GetUniqueSubjectCount()
    {
        return AuditTrail.Values
            .Where(action => action.IdentifierType == IdentifierType.PatientName ||
                           action.IdentifierType == IdentifierType.MedicalRecordNumber)
            .Select(action => action.OriginalValueHash)
            .Distinct()
            .Count();
    }

    /// <summary>
    /// Gets the audit trail as a read-only dictionary for compliance reporting.
    /// </summary>
    public IReadOnlyDictionary<string, DeIdentificationAction> GetAuditTrail() => AuditTrail;

    /// <summary>
    /// Records a date shift operation in the audit trail.
    /// </summary>
    public void RecordDateShift(string originalDate, string shiftedDate)
    {
        var reference = ComputeAuditReference(originalDate);
        AuditTrail[$"DATE:{reference}"] = new DeIdentificationAction
        {
            OriginalValueHash = reference,
            SyntheticValue = shiftedDate,
            IdentifierType = IdentifierType.Other,
            Action = "SHIFT",
            Timestamp = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Types of identifiers for proper synthetic replacement formatting.
/// </summary>
public enum IdentifierType
{
    MedicalRecordNumber,
    PatientName,
    SocialSecurityNumber,
    PhoneNumber,
    Address,
    Email,
    AccountNumber,
    InsuranceId,
    ProviderName,
    LicenseNumber,
    DeviceId,
    Date,
    FreeText,
    Url,
    IpAddress,
    Other
}

/// <summary>
/// Record of a de-identification action for audit trail and compliance reporting.
/// The original value is retained only as a salted, non-reversible hash reference
/// (<see cref="OriginalValueHash"/>); the cleartext original never enters the audit trail,
/// so reports and serialized results carry no recoverable PHI.
/// </summary>
public record DeIdentificationAction
{
    public required string OriginalValueHash { get; init; }
    public required string SyntheticValue { get; init; }
    public required IdentifierType IdentifierType { get; init; }
    public required string Action { get; init; }  // "REPLACE", "REMOVE", "SHIFT"
    public required DateTime Timestamp { get; init; }
}