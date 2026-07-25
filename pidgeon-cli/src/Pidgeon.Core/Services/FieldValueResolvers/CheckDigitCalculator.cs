// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Services.FieldValueResolvers;

/// <summary>
/// Pure check-digit algorithms for HL7 identifier composites (CX.2, XCN.11, XON.4).
/// Deterministic by construction — the same id and scheme always yield the same digit.
/// Extracted from <see cref="IdentifierCoherenceResolver"/> so the identifier logic and
/// the algorithm live in separate, independently testable units.
/// </summary>
public static class CheckDigitCalculator
{
    /// <summary>
    /// Calculates the check digit for an id under the named HL7 table 0061 scheme
    /// ("M10" = Mod10/Luhn, "M11" = Mod11, "ISO" = ISO 7064 Mod 11,10).
    /// Non-numeric characters are stripped first; an id with no digits yields "0".
    /// </summary>
    public static string Calculate(string id, string scheme)
    {
        var numericId = new string(id.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(numericId))
            return "0";

        return scheme switch
        {
            "M10" => Mod10(numericId),
            "M11" => Mod11(numericId),
            "ISO" => Iso7064(numericId),
            _ => "0"
        };
    }

    /// <summary>Mod10 (Luhn) check digit — credit cards and many healthcare identifiers.</summary>
    private static string Mod10(string id)
    {
        var sum = 0;
        var alternate = false;

        for (int i = id.Length - 1; i >= 0; i--)
        {
            var digit = id[i] - '0';

            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
            alternate = !alternate;
        }

        var checkDigit = (10 - (sum % 10)) % 10;
        return checkDigit.ToString();
    }

    /// <summary>Mod11 check digit — common in ISBN and some healthcare identifiers.</summary>
    private static string Mod11(string id)
    {
        var sum = 0;
        var weight = 2;

        for (int i = id.Length - 1; i >= 0; i--)
        {
            sum += (id[i] - '0') * weight;
            weight++;
            if (weight > 7)
                weight = 2;
        }

        var remainder = sum % 11;
        var checkDigit = (11 - remainder) % 11;

        // Mod11 can produce 10 as a check digit, conventionally written 'X'.
        return checkDigit == 10 ? "X" : checkDigit.ToString();
    }

    /// <summary>ISO 7064 Mod 11,10 check digit.</summary>
    private static string Iso7064(string id)
    {
        var check = 10;

        foreach (var c in id)
        {
            if (!char.IsDigit(c))
                continue;

            var digit = c - '0';
            check = ((check + digit) % 10 == 0 ? 10 : (check + digit) % 10) * 2 % 11;
        }

        var checkDigit = (11 - check) % 10;
        return checkDigit.ToString();
    }
}
