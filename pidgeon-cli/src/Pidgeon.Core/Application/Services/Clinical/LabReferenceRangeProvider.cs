// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical;

namespace Pidgeon.Core.Application.Services.Clinical;

/// <summary>
/// Provides clinically realistic reference ranges for common LOINC codes.
/// Used as a fallback when graph-resolved labs don't have range data from
/// the scenario repository. Ranges sourced from curated relationship YAML.
/// </summary>
public class LabReferenceRangeProvider
{
    private static readonly Dictionary<string, ResultRange> Ranges = new(StringComparer.OrdinalIgnoreCase)
    {
        // Cardiac
        ["30934-4"] = new ResultRange { MinValue = 100, MaxValue = 900, Units = "pg/mL" },     // BNP
        ["10839-9"] = new ResultRange { MinValue = 0.0, MaxValue = 0.4, Units = "ng/mL" },     // Troponin I.cardiac
        ["2823-3"] = new ResultRange { MinValue = 3.5, MaxValue = 5.5, Units = "mmol/L" },     // Potassium
        ["2160-0"] = new ResultRange { MinValue = 0.7, MaxValue = 1.3, Units = "mg/dL" },      // Creatinine
        ["2093-3"] = new ResultRange { MinValue = 100, MaxValue = 200, Units = "mg/dL" },      // Cholesterol

        // Diabetes
        ["4548-4"] = new ResultRange { MinValue = 6.5, MaxValue = 12.0, Units = "%" },         // HbA1c
        ["2345-7"] = new ResultRange { MinValue = 70, MaxValue = 400, Units = "mg/dL" },       // Glucose

        // Respiratory
        ["59408-5"] = new ResultRange { MinValue = 88, MaxValue = 98, Units = "%" },            // O2 Saturation
        ["20150-9"] = new ResultRange { MinValue = 30, MaxValue = 80, Units = "%" },            // FEV1
        ["718-7"] = new ResultRange { MinValue = 8.0, MaxValue = 18.0, Units = "g/dL" },       // Hemoglobin

        // Infectious
        ["6690-2"] = new ResultRange { MinValue = 4.0, MaxValue = 30.0, Units = "10*3/uL" },   // WBC
        ["1988-5"] = new ResultRange { MinValue = 5, MaxValue = 300, Units = "mg/L" },          // CRP
        ["600-7"] = new ResultRange { MinValue = 0, MaxValue = 1, Units = "" },                 // Blood culture (qualitative)

        // Common panels
        ["2951-2"] = new ResultRange { MinValue = 136, MaxValue = 145, Units = "mmol/L" },     // Sodium
        ["2075-0"] = new ResultRange { MinValue = 98, MaxValue = 106, Units = "mmol/L" },      // Chloride
        ["3094-0"] = new ResultRange { MinValue = 7, MaxValue = 20, Units = "mg/dL" },         // BUN
        ["17861-6"] = new ResultRange { MinValue = 0.5, MaxValue = 5.0, Units = "ng/mL" },     // Calcium
        ["2532-0"] = new ResultRange { MinValue = 3.4, MaxValue = 5.4, Units = "g/dL" },       // LDH
        ["1920-8"] = new ResultRange { MinValue = 10, MaxValue = 40, Units = "U/L" },          // AST
        ["1742-6"] = new ResultRange { MinValue = 7, MaxValue = 56, Units = "U/L" },           // ALT
        ["787-2"] = new ResultRange { MinValue = 150, MaxValue = 400, Units = "10*3/uL" },     // Platelets
        ["789-8"] = new ResultRange { MinValue = 4.0, MaxValue = 5.5, Units = "10*6/uL" },     // RBC
        ["4544-3"] = new ResultRange { MinValue = 36, MaxValue = 46, Units = "%" },             // Hematocrit
        ["14749-6"] = new ResultRange { MinValue = 70, MaxValue = 100, Units = "mg/dL" },      // Fasting glucose
        ["2571-8"] = new ResultRange { MinValue = 0, MaxValue = 150, Units = "mg/dL" },        // Triglycerides
        ["13457-7"] = new ResultRange { MinValue = 0, MaxValue = 100, Units = "mg/dL" },       // LDL Cholesterol
        ["2085-9"] = new ResultRange { MinValue = 40, MaxValue = 60, Units = "mg/dL" },        // HDL Cholesterol
        ["33762-6"] = new ResultRange { MinValue = 0.0, MaxValue = 0.5, Units = "ng/mL" },     // NT-proBNP
        ["48065-7"] = new ResultRange { MinValue = 0.0, MaxValue = 3.0, Units = "ng/mL" },     // D-dimer
        ["5902-2"] = new ResultRange { MinValue = 11.0, MaxValue = 13.5, Units = "seconds" },  // Prothrombin time
        ["6301-6"] = new ResultRange { MinValue = 0.8, MaxValue = 1.1, Units = "" },           // INR
    };

    /// <summary>
    /// Gets a clinically realistic reference range for the given LOINC code.
    /// Returns null if the code is not in the registry.
    /// </summary>
    public ResultRange? GetRange(string loincCode)
    {
        return Ranges.TryGetValue(loincCode, out var range) ? range : null;
    }
}
