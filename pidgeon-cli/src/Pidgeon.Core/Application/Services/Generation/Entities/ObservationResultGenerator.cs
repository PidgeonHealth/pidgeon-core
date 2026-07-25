// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.Generation.Entities;

/// <summary>
/// Per-entity generator for <see cref="ObservationResult"/>. Fully synchronous.
/// </summary>
internal sealed class ObservationResultGenerator
{
    private static readonly (string Name, string Code, string Value, string Units, string Range)[] LabTests =
    {
        ("Complete Blood Count", "CBC", "WBC: 7.2, RBC: 4.5, Hgb: 14.1, Hct: 42.3", "K/uL", "4.0-11.0"),
        ("Basic Metabolic Panel", "BMP", "Na: 138, K: 4.2, Cl: 102, CO2: 24", "mmol/L", "135-145"),
        ("Lipid Panel", "LIPID", "Total: 180, HDL: 45, LDL: 110, Trig: 125", "mg/dL", "<200"),
        ("Hemoglobin A1c", "HBA1C", "6.8", "%", "<7.0"),
        ("Thyroid Stimulating Hormone", "TSH", "2.4", "mIU/L", "0.4-4.0"),
        ("Creatinine", "CREAT", "1.1", "mg/dL", "0.6-1.3"),
        ("Glucose", "GLUC", "95", "mg/dL", "70-99")
    };

    /// <summary>
    /// Generates a single observation result.
    /// </summary>
    /// <param name="ctx">Coordinate-addressed generation context (entropy + clock + options).</param>
    /// <param name="constraints">
    /// Scenario constraints the observation must stay coherent with, or <c>null</c> when
    /// unconstrained. The coupling seam for scenario-driven flags/ranges; not yet consulted
    /// by procedural sampling.
    /// </param>
    public ObservationResult Generate(EntityGenerationContext ctx, ScenarioConstraints? constraints = null)
    {
        var rng = ctx.Key.Stream();

        var selectedTest = LabTests[rng.Next(LabTests.Length)];
        var testId = $"LAB{rng.Next(10000, 99999)}";

        return new ObservationResult
        {
            Id = testId,
            ObservationDescription = selectedTest.Name,
            ObservationCode = selectedTest.Code,
            Value = selectedTest.Value,
            Units = selectedTest.Units,
            ReferenceRange = selectedTest.Range,
            ResultStatus = "F",
            ObservationDateTime = ctx.Clock.AddHours(-rng.Next(1, 48)),
            CodingSystem = "LN",
            Category = "LAB"
        };
    }
}
