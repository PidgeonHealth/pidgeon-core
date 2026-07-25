// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Resolves HL7-specific coded values (language codes, status codes, types, etc.).
/// Provides realistic values from HL7 standard tables.
/// Priority: 70 (medium-high - HL7 coded values before generic fallback)
/// </summary>
public class HL7CodedValueResolver : IFieldValueResolver
{
    private readonly ILogger<HL7CodedValueResolver> _logger;

    // Bound to the per-message deterministic RNG at the start of each ResolveAsync so
    // the coded-value helpers draw from the shared sequence (seeded → reproducible).
    private Random _random = Random.Shared;

    public int Priority => 70;

    public HL7CodedValueResolver(ILogger<HL7CodedValueResolver> _logger)
    {
        this._logger = _logger ?? throw new ArgumentNullException(nameof(_logger));
    }

    public async Task<string?> ResolveAsync(FieldResolutionContext context)
    {
        await Task.Yield();
        _random = context.FieldRng();

        var fieldName = context.Field.Name?.ToLowerInvariant() ?? "";
        var description = context.Field.Description?.ToLowerInvariant() ?? "";
        var combined = $"{fieldName} {description}";

        try
        {
            // Language codes (HL7 Table 0296)
            if (combined.Contains("language"))
                return GetRandomLanguageCode();

            // Accommodation codes (HL7 Table 0129)
            if (combined.Contains("accommodation"))
                return GetRandomAccommodationCode();

            // Transfer/Discharge reason (HL7 Table 0007)
            if (combined.Contains("transfer") || combined.Contains("discharge"))
                return GetRandomTransferReason();

            // Referral source (HL7 Table 0213)
            if (combined.Contains("referral") && combined.Contains("source"))
                return GetRandomReferralSource();

            // Allergy type/severity (HL7 Table 0128, 0127)
            if (combined.Contains("allergy"))
            {
                if (combined.Contains("severity"))
                    return GetRandomAllergySeverity();
                if (combined.Contains("type"))
                    return GetRandomAllergyType();
                // Generic allergy code
                return "DA"; // Drug allergy
            }

            // Diagnostic Related Group (DRG)
            if (combined.Contains("diagnostic") && combined.Contains("group"))
                return GetRandomDRG();

            // Major Diagnostic Category (MDC)
            if (combined.Contains("diagnostic") && combined.Contains("category"))
                return GetRandomMDC();

            // Outlier type (HL7 Table 0083)
            if (combined.Contains("outlier") && combined.Contains("type"))
                return GetRandomOutlierType();

            // Visit/Patient valuables
            if (combined.Contains("valuables"))
                return GetRandomValuablesCode();

            // Access checks/restrictions
            if (combined.Contains("access") || combined.Contains("restriction"))
                return GetRandomAccessRestriction();

            // Observation/Result status (HL7 Table 0085)
            if ((combined.Contains("observation") || combined.Contains("result")) && combined.Contains("status"))
                return GetRandomObservationStatus();

            // Unit codes (usually UCUM)
            if (combined.Contains("unit") && !combined.Contains("organizational"))
                return GetRandomUnitCode();

            // Reference range
            if (combined.Contains("reference") && combined.Contains("range"))
                return GetRandomReferenceRange();

            // Visit description
            if (combined.Contains("visit") && combined.Contains("description"))
                return "Routine Visit";

            // Location/bed status
            if (combined.Contains("bed") && combined.Contains("status"))
                return GetRandomBedStatus();

            // Duplicate patient indicator
            if (combined.Contains("duplicate") && combined.Contains("patient"))
                return "N"; // Not a duplicate

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving HL7 coded value field {FieldName}", fieldName);
            return null;
        }
    }

    private string GetRandomLanguageCode() =>
        new[] { "en", "es", "fr", "de", "zh", "ja", "ar" }[_random.Next(7)];

    private string GetRandomAccommodationCode() =>
        new[] { "P", "S", "SP", "W" }[_random.Next(4)]; // Private, Semi-private, Suite-Private, Ward

    private string GetRandomTransferReason() =>
        new[] { "01", "02", "03", "04", "05" }[_random.Next(5)];

    private string GetRandomReferralSource() =>
        new[] { "1", "2", "3", "4", "5" }[_random.Next(5)];

    private string GetRandomAllergySeverity() =>
        new[] { "SV", "MO", "MI" }[_random.Next(3)]; // Severe, Moderate, Mild

    private string GetRandomAllergyType() =>
        new[] { "DA", "FA", "MA", "EA" }[_random.Next(4)]; // Drug, Food, Misc, Environment

    private string GetRandomDRG() =>
        $"{_random.Next(1, 999):D3}";

    private string GetRandomMDC() =>
        $"{_random.Next(1, 25):D2}"; // MDC 01-25

    private string GetRandomOutlierType() =>
        new[] { "C", "D", "B" }[_random.Next(3)]; // Cost, Day, Both

    private string GetRandomValuablesCode() =>
        new[] { "Y", "N" }[_random.Next(2)];

    private string GetRandomAccessRestriction() =>
        new[] { "ALL", "DEM", "DRG", "LOC", "OBS", "PHY" }[_random.Next(6)];

    private string GetRandomObservationStatus() =>
        new[] { "F", "P", "C", "X" }[_random.Next(4)]; // Final, Preliminary, Corrected, Cannot obtain

    private string GetRandomUnitCode() =>
        new[] { "mg", "g", "mg/dL", "mmol/L", "mL", "L", "%" }[_random.Next(7)];

    private string GetRandomReferenceRange() =>
        $"{_random.Next(10, 100)}-{_random.Next(100, 200)}";

    private string GetRandomBedStatus() =>
        new[] { "O", "C", "H", "K", "I" }[_random.Next(5)]; // Occupied, Closed, Housekeeping, Contaminated, Isolated
}
