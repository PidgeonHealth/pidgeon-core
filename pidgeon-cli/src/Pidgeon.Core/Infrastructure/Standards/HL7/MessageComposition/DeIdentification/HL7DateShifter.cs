// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.DeIdentification;

/// <summary>
/// Applies the configured de-identification date shift to HL7 v2 date/time fields, preserving the original
/// precision (YYYY through YYYYMMDDHHMMSS). The shift is interval-preserving: every date in a message moves by
/// the same offset, so relative timing (age, length of stay, order-to-result gaps) survives de-identification.
/// </summary>
internal static class HL7DateShifter
{
    /// <summary>
    /// Shifts an HL7 date/time field by the context's configured offset, keeping the original precision.
    /// Returns the field unchanged when it is not a recognized HL7 date format.
    /// </summary>
    public static Result<string> Shift(string dateField, DeIdentificationContext context)
    {
        if (string.IsNullOrWhiteSpace(dateField))
            return Result<string>.Success(dateField);

        try
        {
            // HL7 date formats: YYYY, YYYYMM, YYYYMMDD, YYYYMMDDHHMM, YYYYMMDDHHMMSS, etc.
            if (TryParseHL7Date(dateField, out var parsedDate))
            {
                var shiftedDate = context.ShiftDate(parsedDate, preserveTime: true);
                var shiftedDateString = FormatHL7Date(shiftedDate, dateField.Length);
                return Result<string>.Success(shiftedDateString);
            }

            return Result<string>.Success(dateField); // Return unchanged if not a recognized date format
        }
        catch (Exception ex)
        {
            return Result<string>.Failure($"Error shifting date field: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to parse HL7 date/time strings into DateTime objects.
    /// </summary>
    private static bool TryParseHL7Date(string dateString, out DateTime date)
    {
        date = default;

        if (string.IsNullOrWhiteSpace(dateString))
            return false;

        // Remove any timezone information for parsing
        var cleanDateString = dateString.Split('+', '-')[0];

        return cleanDateString.Length switch
        {
            4 when int.TryParse(cleanDateString, out var year) => // YYYY
                TryCreateDate(year, 1, 1, out date),
            6 when DateTime.TryParseExact(cleanDateString, "yyyyMM", null, System.Globalization.DateTimeStyles.None, out date) => true,
            8 when DateTime.TryParseExact(cleanDateString, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out date) => true,
            10 when DateTime.TryParseExact(cleanDateString, "yyyyMMddHH", null, System.Globalization.DateTimeStyles.None, out date) => true,
            12 when DateTime.TryParseExact(cleanDateString, "yyyyMMddHHmm", null, System.Globalization.DateTimeStyles.None, out date) => true,
            14 when DateTime.TryParseExact(cleanDateString, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out date) => true,
            _ => false
        };
    }

    private static bool TryCreateDate(int year, int month, int day, out DateTime date)
    {
        try
        {
            date = new DateTime(year, month, day);
            return true;
        }
        catch
        {
            date = default;
            return false;
        }
    }

    /// <summary>
    /// Formats a DateTime back to HL7 format matching the original precision.
    /// </summary>
    private static string FormatHL7Date(DateTime date, int originalLength)
    {
        return originalLength switch
        {
            4 => date.ToString("yyyy"),
            6 => date.ToString("yyyyMM"),
            8 => date.ToString("yyyyMMdd"),
            10 => date.ToString("yyyyMMddHH"),
            12 => date.ToString("yyyyMMddHHmm"),
            14 => date.ToString("yyyyMMddHHmmss"),
            _ => date.ToString("yyyyMMddHHmmss") // Default to full precision
        };
    }
}
