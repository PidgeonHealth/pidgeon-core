// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using YamlDotNet.Serialization;

namespace Pidgeon.Core.Domain.Clinical;

/// <summary>
/// Severity levels for coherence rules, with numeric weights for scoring.
/// </summary>
public enum RuleSeverity
{
    Low = 1,
    Medium = 2,
    High = 5,
    Critical = 10
}

/// <summary>
/// A set of coherence rules for a clinical category, loaded from a YAML file.
/// </summary>
public class CoherenceRuleSet
{
    [YamlMember(Alias = "category")]
    public string Category { get; set; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = string.Empty;

    [YamlMember(Alias = "rules")]
    public List<CoherenceRuleDefinition> Rules { get; set; } = new();
}

/// <summary>
/// A single coherence rule defining expected clinical relationships.
/// </summary>
public class CoherenceRuleDefinition
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// ICD-10 diagnosis pattern using glob-style wildcards (e.g., "I21*" or "I20*|I21*|I25*").
    /// Pipe-delimited for multiple patterns. '*' matches any suffix.
    /// </summary>
    [YamlMember(Alias = "diagnosis_pattern")]
    public string? DiagnosisPattern { get; set; }

    /// <summary>
    /// Optional medication pattern trigger (for rules triggered by medications rather than diagnoses).
    /// </summary>
    [YamlMember(Alias = "medication_pattern")]
    public MedicationPatternTrigger? MedicationPattern { get; set; }

    [YamlMember(Alias = "expected_observations")]
    public List<ExpectedObservation> ExpectedObservations { get; set; } = new();

    [YamlMember(Alias = "expected_medications")]
    public List<ExpectedMedication> ExpectedMedications { get; set; } = new();

    [YamlMember(Alias = "expected_procedures")]
    public List<ExpectedProcedure> ExpectedProcedures { get; set; } = new();
}

/// <summary>
/// An expected observation (lab test) that should be present for clinical coherence.
/// </summary>
public class ExpectedObservation
{
    [YamlMember(Alias = "code_system")]
    public string CodeSystem { get; set; } = "LOINC";

    [YamlMember(Alias = "code")]
    public string Code { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "severity")]
    public string SeverityText { get; set; } = "medium";

    public RuleSeverity Severity => Enum.TryParse<RuleSeverity>(SeverityText, true, out var s) ? s : RuleSeverity.Medium;
}

/// <summary>
/// An expected medication that should be present for clinical coherence.
/// </summary>
public class ExpectedMedication
{
    [YamlMember(Alias = "drug_class")]
    public string DrugClass { get; set; } = string.Empty;

    [YamlMember(Alias = "code")]
    public string? Code { get; set; }

    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "route")]
    public List<string> Route { get; set; } = new();

    [YamlMember(Alias = "severity")]
    public string SeverityText { get; set; } = "medium";

    public RuleSeverity Severity => Enum.TryParse<RuleSeverity>(SeverityText, true, out var s) ? s : RuleSeverity.Medium;
}

/// <summary>
/// An expected procedure that should be present for clinical coherence.
/// </summary>
public class ExpectedProcedure
{
    [YamlMember(Alias = "code_system")]
    public string CodeSystem { get; set; } = "CPT";

    [YamlMember(Alias = "code")]
    public string Code { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "severity")]
    public string SeverityText { get; set; } = "medium";

    public RuleSeverity Severity => Enum.TryParse<RuleSeverity>(SeverityText, true, out var s) ? s : RuleSeverity.Medium;
}

/// <summary>
/// Trigger based on medication presence rather than diagnosis.
/// </summary>
public class MedicationPatternTrigger
{
    [YamlMember(Alias = "drug_class")]
    public string? DrugClass { get; set; }

    [YamlMember(Alias = "schedule")]
    public List<string> Schedule { get; set; } = new();
}
