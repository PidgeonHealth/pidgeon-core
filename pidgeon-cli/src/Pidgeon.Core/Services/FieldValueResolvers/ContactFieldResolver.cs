// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Resolves contact information fields (phone numbers, emails, fax numbers, etc.).
/// Handles complex field names with dashes, parentheses, and type qualifiers.
/// Priority: 75 (medium - contact data before fallback)
/// </summary>
public class ContactFieldResolver : IFieldValueResolver
{
    private readonly ILogger<ContactFieldResolver> _logger;

    // Bound to the per-message deterministic RNG at the start of each ResolveAsync so
    // the contact-formatting helpers draw from the shared sequence (seeded → reproducible).
    private Random _random = Random.Shared;

    public int Priority => 75;

    public ContactFieldResolver(ILogger<ContactFieldResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> ResolveAsync(FieldResolutionContext context)
    {
        await Task.Yield();
        _random = context.FieldRng();

        var fieldName = context.Field.Name?.ToLowerInvariant() ?? "";
        var description = context.Field.Description?.ToLowerInvariant() ?? "";
        var combined = $"{fieldName} {description}";
        var dataType = context.Field.DataType?.ToUpperInvariant() ?? "";

        // Skip numeric fields - they need numeric values, not formatted strings
        // This prevents XTN.7 (Phone Number - NM) from getting "(999)999-9999" instead of just digits
        if (dataType == "NM")
        {
            return null;
        }

        try
        {
            // Phone numbers (handles "Phone Number - Home", "Telephone", etc.)
            if (combined.Contains("phone") || combined.Contains("telephone"))
            {
                if (combined.Contains("home"))
                    return GeneratePhoneNumber("home");
                if (combined.Contains("business") || combined.Contains("work") || combined.Contains("office"))
                    return GeneratePhoneNumber("business");
                if (combined.Contains("mobile") || combined.Contains("cell"))
                    return GeneratePhoneNumber("mobile");

                // Generic phone number
                return GeneratePhoneNumber("generic");
            }

            // Fax numbers
            if (combined.Contains("fax"))
                return GenerateFaxNumber();

            // Email addresses
            if (combined.Contains("email") || combined.Contains("e-mail"))
                return GenerateEmailAddress();

            // Pager numbers
            if (combined.Contains("pager") || combined.Contains("beeper"))
                return GeneratePagerNumber();

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resolving contact field {FieldName}", fieldName);
            return null;
        }
    }

    private string GeneratePhoneNumber(string type) =>
        type switch
        {
            "home" => $"({_random.Next(200, 999)}){_random.Next(200, 999)}-{_random.Next(1000, 9999)}",
            "business" => $"({_random.Next(200, 999)}){_random.Next(200, 999)}-{_random.Next(1000, 9999)}",
            "mobile" => $"({_random.Next(200, 999)}){_random.Next(200, 999)}-{_random.Next(1000, 9999)}",
            _ => $"({_random.Next(200, 999)}){_random.Next(200, 999)}-{_random.Next(1000, 9999)}"
        };

    private string GenerateFaxNumber() =>
        $"({_random.Next(200, 999)}){_random.Next(200, 999)}-{_random.Next(1000, 9999)}";

    private string GenerateEmailAddress()
    {
        var names = new[] { "patient", "user", "contact", "person", "individual" };
        var domains = new[] { "example.com", "healthcare.org", "patient.net", "mail.com" };
        var name = names[_random.Next(names.Length)];
        var domain = domains[_random.Next(domains.Length)];
        return $"{name}{_random.Next(100, 999)}@{domain}";
    }

    private string GeneratePagerNumber() =>
        $"{_random.Next(100, 999)}-{_random.Next(1000, 9999)}";
}
