// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Vendor MRN shapes. Real EHRs issue MRNs in vendor-specific widths; synthetic data that
/// matches the target schema's shape survives a QA engineer's first-inspection check.
/// </summary>
public enum MrnFormat
{
    /// <summary>6–10 digit numeric, no leading zero — matches the HL7 PID-3 <c>^\d{6,10}$</c> constraint.</summary>
    Generic,

    /// <summary>7–10 digit numeric (Epic MRN width).</summary>
    Epic,

    /// <summary>11 digit numeric (Cerner person-id width).</summary>
    Cerner
}

/// <summary>
/// Generates check-digit-valid healthcare reference identifiers (NPI, DEA) and vendor-shaped MRNs.
/// Pure and deterministic by construction — the same <see cref="Random"/> draw sequence yields the
/// same identifier. Lives beside <see cref="CheckDigitCalculator"/> so both the NCPDP generator and
/// Flock's population generator share one source of truth rather than each carrying inline algorithms.
/// </summary>
public static class HealthcareIdentifierFactory
{
    /// <summary>DEA registrant-type letters (table of business activity): A/B practitioners, F distributors, M mid-level.</summary>
    public static readonly char[] DeaRegistrantTypes = { 'A', 'B', 'F', 'M' };

    /// <summary>
    /// Generates a valid 10-digit NPI: 9 random digits plus a Luhn check digit computed over the
    /// sequence prefixed with the NPPES "80840" issuer prefix, per the official NPI check-digit
    /// specification. The "80840" prefix participates in the checksum but is not part of the returned
    /// identifier. Doubling begins at the rightmost payload digit (append-Luhn semantics) so the result
    /// validates against a standard NPI checker — verified against the canonical example 1234567893.
    /// </summary>
    public static string GenerateNpi(Random rng)
    {
        var nine = new char[9];
        for (var i = 0; i < 9; i++)
            nine[i] = (char)('0' + rng.Next(0, 10));

        var payload = "80840" + new string(nine);
        var sum = 0;
        var doubleDigit = true; // the rightmost payload digit is doubled; the check digit sits to its right
        for (var i = payload.Length - 1; i >= 0; i--)
        {
            var digit = payload[i] - '0';
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
            doubleDigit = !doubleDigit;
        }

        var checkDigit = (10 - (sum % 10)) % 10;
        return new string(nine) + checkDigit;
    }

    /// <summary>
    /// Generates a DEA registration number with a valid check digit. Format: registrant-type letter +
    /// registrant last-name initial + 6 digits + check digit, where the check digit is the units digit
    /// of (sum of digits 1,3,5) + 2 × (sum of digits 2,4,6).
    /// </summary>
    public static string GenerateDea(char registrantType, char lastNameInitial, Random rng)
    {
        var first = char.ToUpperInvariant(registrantType);
        var second = char.IsLetter(lastNameInitial) ? char.ToUpperInvariant(lastNameInitial) : 'A';

        var digits = new int[6];
        for (var i = 0; i < 6; i++)
            digits[i] = rng.Next(0, 10);

        var oddSum = digits[0] + digits[2] + digits[4];
        var evenSum = digits[1] + digits[3] + digits[5];
        var checkDigit = (oddSum + 2 * evenSum) % 10;

        return $"{first}{second}{string.Concat(digits.Select(d => d.ToString()))}{checkDigit}";
    }

    /// <summary>
    /// Generates a DEA number picking a random registrant type and deriving the second letter from the
    /// prescriber's last name (defaults to 'A' when the name is missing or non-alphabetic).
    /// </summary>
    public static string GenerateDea(string? lastName, Random rng)
    {
        var registrantType = DeaRegistrantTypes[rng.Next(DeaRegistrantTypes.Length)];
        var initial = !string.IsNullOrEmpty(lastName) && char.IsLetter(lastName[0]) ? lastName[0] : 'A';
        return GenerateDea(registrantType, initial, rng);
    }

    /// <summary>
    /// Generates a numeric MRN in the requested vendor width, with no leading zero.
    /// </summary>
    public static string GenerateMrn(MrnFormat format, Random rng)
    {
        var length = format switch
        {
            MrnFormat.Epic => rng.Next(7, 11),    // 7–10
            MrnFormat.Cerner => 11,
            _ => rng.Next(6, 11)                  // generic 6–10
        };

        return GenerateNumeric(rng, length);
    }

    private static string GenerateNumeric(Random rng, int length)
    {
        var chars = new char[length];
        chars[0] = (char)('1' + rng.Next(0, 9)); // 1–9, never a leading zero
        for (var i = 1; i < length; i++)
            chars[i] = (char)('0' + rng.Next(0, 10));

        return new string(chars);
    }
}
