// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Clinical;

/// <summary>
/// Represents a clinically coherent scenario linking diagnoses, medications, and lab tests.
/// Used to generate realistic clinical data where all components align with a specific condition.
/// </summary>
public class ClinicalScenario
{
    /// <summary>
    /// Unique identifier for this scenario (e.g., "CHF", "diabetes_type2", "copd_moderate")
    /// </summary>
    public string ScenarioId { get; set; } = string.Empty;

    /// <summary>
    /// Display name for this scenario
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Primary diagnosis codes (ICD-10) associated with this scenario
    /// </summary>
    public List<DiagnosisCode> PrimaryDiagnoses { get; set; } = new();

    /// <summary>
    /// Common secondary/comorbid diagnosis codes for this scenario
    /// </summary>
    public List<DiagnosisCode> SecondaryDiagnoses { get; set; } = new();

    /// <summary>
    /// Typical medications for this condition (with probability of prescription)
    /// </summary>
    public List<MedicationOption> TypicalMedications { get; set; } = new();

    /// <summary>
    /// Relevant lab tests for monitoring this condition
    /// </summary>
    public List<LabTestOption> RelevantLabTests { get; set; } = new();

    /// <summary>
    /// Probability weight for scenario selection (higher = more common)
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// Demographic and physiological constraints this scenario imposes on entity and
    /// observation generation. Defaults to unconstrained. The entity sampler and observation
    /// generator read this to keep the patient and results coherent with the scenario.
    /// </summary>
    public ScenarioConstraints Constraints { get; set; } = new();
}

/// <summary>
/// Diagnosis code with metadata
/// </summary>
public class DiagnosisCode
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CodingSystem { get; set; } = "ICD10";
}

/// <summary>
/// Medication option with probability of prescription
/// </summary>
public class MedicationOption
{
    /// <summary>
    /// Generic medication name
    /// </summary>
    public string GenericName { get; set; } = string.Empty;

    /// <summary>
    /// Drug class (e.g., "ACE Inhibitor", "Beta Blocker")
    /// </summary>
    public string DrugClass { get; set; } = string.Empty;

    /// <summary>
    /// Typical dosage range
    /// </summary>
    public string TypicalDosage { get; set; } = string.Empty;

    /// <summary>
    /// Probability this medication is prescribed (0.0-1.0)
    /// </summary>
    public double PrescriptionProbability { get; set; } = 0.5;
}

/// <summary>
/// Lab test option with typical result ranges
/// </summary>
public class LabTestOption
{
    /// <summary>
    /// LOINC code for this test
    /// </summary>
    public string LoincCode { get; set; } = string.Empty;

    /// <summary>
    /// Test name/description
    /// </summary>
    public string TestName { get; set; } = string.Empty;

    /// <summary>
    /// Expected result range for this condition (min-max)
    /// </summary>
    public ResultRange ExpectedRange { get; set; } = new();

    /// <summary>
    /// Probability this test is ordered (0.0-1.0)
    /// </summary>
    public double OrderProbability { get; set; } = 0.5;
}

/// <summary>
/// Numeric result range
/// </summary>
public class ResultRange
{
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public string Units { get; set; } = string.Empty;
}
