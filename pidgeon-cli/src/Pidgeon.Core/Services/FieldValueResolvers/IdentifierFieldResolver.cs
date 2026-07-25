// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Resolves identifier field values (patient IDs, account numbers, license numbers, etc.).
/// Handles complex field names with parentheses, dashes, and special characters.
/// Priority: 75 (medium - identifier data before fallback)
/// </summary>
public class IdentifierFieldResolver : IFieldValueResolver
{
    private readonly ILogger<IdentifierFieldResolver> _logger;

    // Bound to the per-message deterministic RNG / clock at the start of each
    // ResolveAsync so the identifier-formatting helpers draw from the shared sequence.
    private Random _random = Random.Shared;
    private DateTime _clock = DateTime.Now;

    public int Priority => 75;

    public IdentifierFieldResolver(ILogger<IdentifierFieldResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> ResolveAsync(FieldResolutionContext context)
    {
        await Task.Yield();
        _random = context.FieldRng();
        _clock = context.Clock();

        var fieldName = context.Field.Name?.ToLowerInvariant() ?? "";
        var description = context.Field.Description?.ToLowerInvariant() ?? "";
        var combined = $"{fieldName} {description}";

        try
        {
            // Mother's identifier — the patient's MOTHER, a different person (audit D-07).
            // Checked before the patient branch so "mother's identifier" never resolves to
            // the patient's own ID.
            if (combined.Contains("mother"))
                return GenerateGenericId();

            // Patient identifiers - use consistent ID from context
            if (combined.Contains("patient") && (combined.Contains("id") || combined.Contains("identifier")))
                return GetPatientIdFromContext(context);

            // Account numbers
            if (combined.Contains("account") && (combined.Contains("number") || combined.Contains("id")))
                return GetAccountNumberFromContext(context);

            // Medical record numbers - use consistent MRN from context
            if (combined.Contains("medical record") || combined.Contains("mrn"))
                return GetMedicalRecordNumberFromContext(context);

            // Social Security Numbers - use consistent SSN from context
            if (combined.Contains("ssn") || combined.Contains("social security"))
                return GetSocialSecurityNumberFromContext(context);

            // Driver's license
            if (combined.Contains("driver") || combined.Contains("license number"))
                return GenerateDriversLicense();

            // Operator/User IDs
            if (combined.Contains("operator") && (combined.Contains("id") || combined.Contains("identifier")))
                return GenerateOperatorId();

            // Producer/Provider IDs
            if ((combined.Contains("producer") || combined.Contains("provider")) &&
                (combined.Contains("id") || combined.Contains("identifier")))
                return GenerateProviderId();

            // Visit/Encounter identifiers - use consistent ID from context
            if ((combined.Contains("visit") || combined.Contains("encounter")) &&
                (combined.Contains("number") || combined.Contains("id")))
                return GetVisitNumberFromContext(context);

            // Order/Placer/Filler identifiers
            if ((combined.Contains("order") || combined.Contains("placer") || combined.Contains("filler")) &&
                (combined.Contains("number") || combined.Contains("id")))
                return GenerateOrderNumber();

            // Message control IDs
            if (combined.Contains("message") && combined.Contains("control"))
                return GenerateMessageControlId();

            // Generic identifiers (catch remaining ID fields)
            if (combined.Contains("identifier") ||
                (combined.Contains("id") && !combined.Contains("void"))) // Avoid "void" as ID
                return GenerateGenericId();

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving identifier field {FieldName}", fieldName);
            return null;
        }
    }

    /// <summary>
    /// Gets patient ID from clinical context to ensure referential integrity within a message.
    /// Falls back to random generation only if context unavailable.
    /// </summary>
    private string GetPatientIdFromContext(FieldResolutionContext context)
    {
        var patient = context.GenerationContext?.Patient;
        if (patient?.Id != null)
        {
            // If ID is just numeric, add PAT prefix for consistency
            if (int.TryParse(patient.Id, out _))
                return $"PAT{patient.Id}";
            return patient.Id;
        }

        // Fallback only if no patient context
        return $"PAT{_random.Next(100000, 999999)}";
    }

    /// <summary>
    /// Gets account number from clinical context.
    /// </summary>
    private string GetAccountNumberFromContext(FieldResolutionContext context)
    {
        var patient = context.GenerationContext?.Patient;
        if (patient?.MedicalRecordNumber != null)
        {
            // Use MRN as account number with ACCT prefix
            if (int.TryParse(patient.MedicalRecordNumber, out _))
                return $"ACCT{patient.MedicalRecordNumber}";
            return patient.MedicalRecordNumber;
        }

        return $"ACCT{_random.Next(1000000, 9999999)}";
    }

    /// <summary>
    /// Gets medical record number from clinical context.
    /// </summary>
    private string GetMedicalRecordNumberFromContext(FieldResolutionContext context)
    {
        var patient = context.GenerationContext?.Patient;
        if (patient?.MedicalRecordNumber != null)
            return patient.MedicalRecordNumber;

        if (patient?.Id != null)
            return patient.Id;

        return $"MRN{_random.Next(100000, 999999)}";
    }

    /// <summary>
    /// Gets SSN from clinical context. The patient's SSN belongs ONLY to PID (PID-19);
    /// SSN fields on role segments (GT1-12 guarantor, NK1-37 contact person) describe
    /// OTHER people and draw a coordinate-keyed SSN instead (audit D-07).
    /// </summary>
    private string GetSocialSecurityNumberFromContext(FieldResolutionContext context)
    {
        var patient = context.GenerationContext?.Patient;
        if (context.SegmentCode == "PID" && !string.IsNullOrEmpty(patient?.SocialSecurityNumber))
            return patient.SocialSecurityNumber;

        var ssn = $"{_random.Next(100, 999)}-{_random.Next(10, 99)}-{_random.Next(1000, 9999)}";
        // A role SSN landing on the patient's own defeats the separation; redraw.
        if (ssn == patient?.SocialSecurityNumber)
            ssn = $"{_random.Next(100, 999)}-{_random.Next(10, 99)}-{_random.Next(1000, 9999)}";
        return ssn;
    }

    /// <summary>
    /// Gets visit/encounter number from clinical context.
    /// </summary>
    private string GetVisitNumberFromContext(FieldResolutionContext context)
    {
        var encounter = context.GenerationContext?.Encounter;
        if (encounter?.Id != null)
        {
            // If ID is just numeric, add V prefix for consistency
            if (int.TryParse(encounter.Id, out _))
                return $"V{encounter.Id}";
            return encounter.Id;
        }

        return $"V{_random.Next(1000000, 9999999)}";
    }

    // These remain random as they're not part of the core clinical context
    private string GenerateDriversLicense() => $"DL{_random.Next(1000000, 9999999)}";
    private string GenerateOperatorId() => $"OP{_random.Next(1000, 9999)}";
    private string GenerateProviderId() => $"PRV{_random.Next(10000, 99999)}";
    private string GenerateOrderNumber() => $"ORD{_random.Next(100000, 999999)}";
    private string GenerateMessageControlId() => $"MSG{_clock:yyyyMMddHHmmss}{_random.Next(1000, 9999)}";
    private string GenerateGenericId() => $"ID{_random.Next(100000, 999999)}";
}
