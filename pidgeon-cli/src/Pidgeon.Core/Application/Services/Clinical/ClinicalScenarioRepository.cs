// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Clinical;
using Pidgeon.Core.Domain.Clinical.Entities;

namespace Pidgeon.Core.Application.Services.Clinical;

/// <summary>
/// Repository of predefined clinical scenarios for realistic message generation.
/// Provides clinically coherent combinations of diagnoses, medications, and lab tests.
/// </summary>
public class ClinicalScenarioRepository
{
    private readonly List<ClinicalScenario> _scenarios;
    private readonly Random _random;

    public ClinicalScenarioRepository()
    {
        _random = new Random();
        _scenarios = InitializeScenarios();
    }

    /// <summary>
    /// Gets a random clinical scenario weighted by commonality, using the repository's
    /// own non-deterministic generator. Retained for callers outside the seeded
    /// generation path.
    /// </summary>
    public ClinicalScenario GetRandomScenario() => GetRandomScenario(_random);

    /// <summary>
    /// Gets a random clinical scenario weighted by commonality, drawing from the supplied
    /// generator. The seeded generation path passes the per-message RNG so scenario
    /// selection is reproducible.
    /// </summary>
    public ClinicalScenario GetRandomScenario(Random rng)
    {
        var totalWeight = _scenarios.Sum(s => s.Weight);
        var randomValue = rng.Next(totalWeight);

        var currentWeight = 0;
        foreach (var scenario in _scenarios)
        {
            currentWeight += scenario.Weight;
            if (randomValue < currentWeight)
                return scenario;
        }

        return _scenarios.First();
    }

    /// <summary>
    /// Gets a specific scenario by ID.
    /// </summary>
    public ClinicalScenario? GetScenarioById(string scenarioId)
    {
        return _scenarios.FirstOrDefault(s => s.ScenarioId == scenarioId);
    }

    /// <summary>
    /// Gets all available scenarios.
    /// </summary>
    public IReadOnlyList<ClinicalScenario> GetAllScenarios() => _scenarios.AsReadOnly();

    private List<ClinicalScenario> InitializeScenarios()
    {
        return new List<ClinicalScenario>
        {
            CreateCongestiveHeartFailureScenario(),
            CreateType2DiabetesScenario(),
            CreateCOPDScenario(),
            CreateHypertensionScenario(),
            CreatePneumoniaScenario(),
            CreateAcuteMyocardialInfarctionScenario(),
            CreateLungCancerScenario(),
            CreateStrokeScenario(),
            CreateHipFractureScenario(),
            CreatePregnancyScenario(),
            CreatePediatricURIScenario(),
            CreateDepressionScenario(),
            CreateCirrhosisScenario(),
            CreateHypothyroidScenario(),
            CreateChronicKidneyDiseaseScenario(),
            CreateSepsisScenario()
        };
    }

    /// <summary>
    /// CHF (Congestive Heart Failure) - Common inpatient condition
    /// </summary>
    private ClinicalScenario CreateCongestiveHeartFailureScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "chf",
            Name = "Congestive Heart Failure",
            Weight = 20, // Common condition
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "I50.9", Description = "Heart failure, unspecified", CodingSystem = "ICD10" },
                new() { Code = "I50.23", Description = "Acute on chronic systolic heart failure", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "I10", Description = "Essential hypertension", CodingSystem = "ICD10" },
                new() { Code = "E11.9", Description = "Type 2 diabetes mellitus", CodingSystem = "ICD10" },
                new() { Code = "N18.3", Description = "Chronic kidney disease, stage 3", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Furosemide", DrugClass = "Loop Diuretic", TypicalDosage = "40-80mg daily", PrescriptionProbability = 0.9 },
                new() { GenericName = "Lisinopril", DrugClass = "ACE Inhibitor", TypicalDosage = "10-40mg daily", PrescriptionProbability = 0.8 },
                new() { GenericName = "Carvedilol", DrugClass = "Beta Blocker", TypicalDosage = "6.25-25mg BID", PrescriptionProbability = 0.7 },
                new() { GenericName = "Spironolactone", DrugClass = "Potassium-Sparing Diuretic", TypicalDosage = "25-50mg daily", PrescriptionProbability = 0.6 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "30934-4", TestName = "BNP (Brain Natriuretic Peptide)", ExpectedRange = new ResultRange { MinValue = 200, MaxValue = 2000, Units = "pg/mL" }, OrderProbability = 0.9 },
                new() { LoincCode = "2160-0", TestName = "Creatinine", ExpectedRange = new ResultRange { MinValue = 1.2, MaxValue = 2.5, Units = "mg/dL" }, OrderProbability = 0.8 },
                new() { LoincCode = "6299-2", TestName = "BUN", ExpectedRange = new ResultRange { MinValue = 20, MaxValue = 60, Units = "mg/dL" }, OrderProbability = 0.7 },
                new() { LoincCode = "2823-3", TestName = "Potassium", ExpectedRange = new ResultRange { MinValue = 3.5, MaxValue = 5.5, Units = "mmol/L" }, OrderProbability = 0.8 }
            }
        };
    }

    /// <summary>
    /// Type 2 Diabetes - Very common chronic condition
    /// </summary>
    private ClinicalScenario CreateType2DiabetesScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "diabetes_type2",
            Name = "Type 2 Diabetes Mellitus",
            Weight = 25, // Very common
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "E11.9", Description = "Type 2 diabetes mellitus without complications", CodingSystem = "ICD10" },
                new() { Code = "E11.65", Description = "Type 2 diabetes with hyperglycemia", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "I10", Description = "Essential hypertension", CodingSystem = "ICD10" },
                new() { Code = "E78.5", Description = "Hyperlipidemia", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Metformin", DrugClass = "Biguanide", TypicalDosage = "500-2000mg daily", PrescriptionProbability = 0.9 },
                new() { GenericName = "Insulin Glargine", DrugClass = "Long-Acting Insulin", TypicalDosage = "10-100 units daily", PrescriptionProbability = 0.6 },
                new() { GenericName = "Glipizide", DrugClass = "Sulfonylurea", TypicalDosage = "5-20mg daily", PrescriptionProbability = 0.5 },
                new() { GenericName = "Atorvastatin", DrugClass = "Statin", TypicalDosage = "10-80mg daily", PrescriptionProbability = 0.7 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "4548-4", TestName = "Hemoglobin A1c", ExpectedRange = new ResultRange { MinValue = 7.0, MaxValue = 12.0, Units = "%" }, OrderProbability = 0.95 },
                new() { LoincCode = "2345-7", TestName = "Glucose", ExpectedRange = new ResultRange { MinValue = 140, MaxValue = 350, Units = "mg/dL" }, OrderProbability = 0.9 },
                new() { LoincCode = "2160-0", TestName = "Creatinine", ExpectedRange = new ResultRange { MinValue = 0.8, MaxValue = 1.5, Units = "mg/dL" }, OrderProbability = 0.7 },
                new() { LoincCode = "13457-7", TestName = "LDL Cholesterol", ExpectedRange = new ResultRange { MinValue = 100, MaxValue = 190, Units = "mg/dL" }, OrderProbability = 0.7 }
            }
        };
    }

    /// <summary>
    /// COPD (Chronic Obstructive Pulmonary Disease)
    /// </summary>
    private ClinicalScenario CreateCOPDScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "copd",
            Name = "Chronic Obstructive Pulmonary Disease",
            Weight = 15,
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "J44.1", Description = "COPD with acute exacerbation", CodingSystem = "ICD10" },
                new() { Code = "J44.0", Description = "COPD with acute lower respiratory infection", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "J96.01", Description = "Acute respiratory failure with hypoxia", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Albuterol", DrugClass = "Short-Acting Beta Agonist", TypicalDosage = "2.5mg nebulized q4-6h", PrescriptionProbability = 0.95 },
                new() { GenericName = "Ipratropium", DrugClass = "Anticholinergic", TypicalDosage = "0.5mg nebulized q6h", PrescriptionProbability = 0.8 },
                new() { GenericName = "Prednisone", DrugClass = "Corticosteroid", TypicalDosage = "40-60mg daily", PrescriptionProbability = 0.85 },
                new() { GenericName = "Azithromycin", DrugClass = "Macrolide Antibiotic", TypicalDosage = "500mg daily x5 days", PrescriptionProbability = 0.6 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "59408-5", TestName = "Oxygen Saturation", ExpectedRange = new ResultRange { MinValue = 85, MaxValue = 94, Units = "%" }, OrderProbability = 0.95 },
                new() { LoincCode = "2019-8", TestName = "pCO2 (Arterial)", ExpectedRange = new ResultRange { MinValue = 45, MaxValue = 65, Units = "mmHg" }, OrderProbability = 0.7 },
                new() { LoincCode = "6690-2", TestName = "WBC", ExpectedRange = new ResultRange { MinValue = 10, MaxValue = 18, Units = "K/uL" }, OrderProbability = 0.8 }
            }
        };
    }

    /// <summary>
    /// Hypertension - Most common chronic condition
    /// </summary>
    private ClinicalScenario CreateHypertensionScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "hypertension",
            Name = "Essential Hypertension",
            Weight = 30, // Most common
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "I10", Description = "Essential (primary) hypertension", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>(),
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Lisinopril", DrugClass = "ACE Inhibitor", TypicalDosage = "10-40mg daily", PrescriptionProbability = 0.7 },
                new() { GenericName = "Amlodipine", DrugClass = "Calcium Channel Blocker", TypicalDosage = "5-10mg daily", PrescriptionProbability = 0.6 },
                new() { GenericName = "Hydrochlorothiazide", DrugClass = "Thiazide Diuretic", TypicalDosage = "12.5-25mg daily", PrescriptionProbability = 0.5 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "2160-0", TestName = "Creatinine", ExpectedRange = new ResultRange { MinValue = 0.7, MaxValue = 1.3, Units = "mg/dL" }, OrderProbability = 0.6 },
                new() { LoincCode = "2823-3", TestName = "Potassium", ExpectedRange = new ResultRange { MinValue = 3.5, MaxValue = 5.0, Units = "mmol/L" }, OrderProbability = 0.5 }
            }
        };
    }

    /// <summary>
    /// Community-Acquired Pneumonia
    /// </summary>
    private ClinicalScenario CreatePneumoniaScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "pneumonia",
            Name = "Community-Acquired Pneumonia",
            Weight = 15,
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "J18.9", Description = "Pneumonia, unspecified organism", CodingSystem = "ICD10" },
                new() { Code = "J15.9", Description = "Bacterial pneumonia, unspecified", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "J96.01", Description = "Acute respiratory failure with hypoxia", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Ceftriaxone", DrugClass = "Cephalosporin", TypicalDosage = "1-2g IV daily", PrescriptionProbability = 0.9 },
                new() { GenericName = "Azithromycin", DrugClass = "Macrolide", TypicalDosage = "500mg IV/PO daily", PrescriptionProbability = 0.85 },
                new() { GenericName = "Acetaminophen", DrugClass = "Antipyretic", TypicalDosage = "650mg q6h PRN", PrescriptionProbability = 0.7 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "6690-2", TestName = "WBC", ExpectedRange = new ResultRange { MinValue = 12, MaxValue = 25, Units = "K/uL" }, OrderProbability = 0.95 },
                new() { LoincCode = "59408-5", TestName = "Oxygen Saturation", ExpectedRange = new ResultRange { MinValue = 88, MaxValue = 96, Units = "%" }, OrderProbability = 0.9 },
                new() { LoincCode = "1988-5", TestName = "CRP (C-Reactive Protein)", ExpectedRange = new ResultRange { MinValue = 50, MaxValue = 200, Units = "mg/L" }, OrderProbability = 0.7 }
            }
        };
    }

    /// <summary>
    /// Acute Myocardial Infarction (Heart Attack)
    /// </summary>
    private ClinicalScenario CreateAcuteMyocardialInfarctionScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "ami",
            Name = "Acute Myocardial Infarction",
            Weight = 10, // Less common but high acuity
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "I21.9", Description = "Acute myocardial infarction, unspecified", CodingSystem = "ICD10" },
                new() { Code = "I21.3", Description = "ST elevation myocardial infarction", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "I50.9", Description = "Heart failure, unspecified", CodingSystem = "ICD10" },
                new() { Code = "I10", Description = "Essential hypertension", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Aspirin", DrugClass = "Antiplatelet", TypicalDosage = "325mg loading, then 81mg daily", PrescriptionProbability = 0.98 },
                new() { GenericName = "Atorvastatin", DrugClass = "Statin", TypicalDosage = "80mg daily", PrescriptionProbability = 0.95 },
                new() { GenericName = "Metoprolol", DrugClass = "Beta Blocker", TypicalDosage = "25-100mg BID", PrescriptionProbability = 0.9 },
                new() { GenericName = "Lisinopril", DrugClass = "ACE Inhibitor", TypicalDosage = "5-40mg daily", PrescriptionProbability = 0.85 },
                new() { GenericName = "Clopidogrel", DrugClass = "Antiplatelet", TypicalDosage = "75mg daily", PrescriptionProbability = 0.8 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "10839-9", TestName = "Troponin I", ExpectedRange = new ResultRange { MinValue = 0.5, MaxValue = 50.0, Units = "ng/mL" }, OrderProbability = 0.99 },
                new() { LoincCode = "30934-4", TestName = "BNP", ExpectedRange = new ResultRange { MinValue = 100, MaxValue = 1000, Units = "pg/mL" }, OrderProbability = 0.8 },
                new() { LoincCode = "2823-3", TestName = "Potassium", ExpectedRange = new ResultRange { MinValue = 3.5, MaxValue = 5.5, Units = "mmol/L" }, OrderProbability = 0.7 }
            }
        };
    }

    private ClinicalScenario CreateLungCancerScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "lung_cancer",
            Name = "Lung Cancer",
            Weight = 8,
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "C34.90", Description = "Malignant neoplasm of unspecified part of bronchus or lung", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "J44.9", Description = "COPD, unspecified", CodingSystem = "ICD10" },
                new() { Code = "D64.9", Description = "Anemia, unspecified", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Carboplatin", DrugClass = "Platinum Agent", TypicalDosage = "AUC 5-6 IV", PrescriptionProbability = 0.7 },
                new() { GenericName = "Paclitaxel", DrugClass = "Taxane", TypicalDosage = "175mg/m2 IV", PrescriptionProbability = 0.65 },
                new() { GenericName = "Pembrolizumab", DrugClass = "PD-1 Inhibitor", TypicalDosage = "200mg IV q3w", PrescriptionProbability = 0.5 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "6690-2", TestName = "WBC", ExpectedRange = new ResultRange { MinValue = 2.0, MaxValue = 15.0, Units = "K/uL" }, OrderProbability = 0.9 },
                new() { LoincCode = "2039-6", TestName = "CEA", ExpectedRange = new ResultRange { MinValue = 0.0, MaxValue = 50.0, Units = "ng/mL" }, OrderProbability = 0.8 },
                new() { LoincCode = "2532-0", TestName = "LDH", ExpectedRange = new ResultRange { MinValue = 100, MaxValue = 500, Units = "U/L" }, OrderProbability = 0.7 }
            }
        };
    }

    private ClinicalScenario CreateStrokeScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "stroke",
            Name = "Cerebral Infarction (Stroke)",
            Weight = 10,
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "I63.9", Description = "Cerebral infarction, unspecified", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "I10", Description = "Essential hypertension", CodingSystem = "ICD10" },
                new() { Code = "I48.91", Description = "Unspecified atrial fibrillation", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Aspirin", DrugClass = "Antiplatelet", TypicalDosage = "325mg daily", PrescriptionProbability = 0.85 },
                new() { GenericName = "Alteplase", DrugClass = "Thrombolytic", TypicalDosage = "0.9mg/kg IV", PrescriptionProbability = 0.4 },
                new() { GenericName = "Atorvastatin", DrugClass = "Statin", TypicalDosage = "40-80mg daily", PrescriptionProbability = 0.75 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "5902-2", TestName = "Prothrombin Time", ExpectedRange = new ResultRange { MinValue = 10, MaxValue = 14, Units = "s" }, OrderProbability = 0.9 },
                new() { LoincCode = "6301-6", TestName = "INR", ExpectedRange = new ResultRange { MinValue = 0.9, MaxValue = 4.0, Units = "{INR}" }, OrderProbability = 0.9 },
                new() { LoincCode = "6690-2", TestName = "WBC", ExpectedRange = new ResultRange { MinValue = 4.0, MaxValue = 11.0, Units = "K/uL" }, OrderProbability = 0.7 }
            }
        };
    }

    private ClinicalScenario CreateHipFractureScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "hip_fracture",
            Name = "Hip Fracture",
            Weight = 8,
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "S72.001A", Description = "Fracture of unspecified part of neck of right femur, initial encounter", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "M81.0", Description = "Age-related osteoporosis", CodingSystem = "ICD10" },
                new() { Code = "D62", Description = "Acute posthemorrhagic anemia", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Oxycodone", DrugClass = "Opioid Analgesic", TypicalDosage = "5mg q4-6h PRN", PrescriptionProbability = 0.7 },
                new() { GenericName = "Enoxaparin", DrugClass = "Low Molecular Weight Heparin", TypicalDosage = "40mg SQ daily", PrescriptionProbability = 0.8 },
                new() { GenericName = "Acetaminophen", DrugClass = "Analgesic", TypicalDosage = "500mg q6h", PrescriptionProbability = 0.85 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "6690-2", TestName = "WBC", ExpectedRange = new ResultRange { MinValue = 4.0, MaxValue = 11.0, Units = "K/uL" }, OrderProbability = 0.8 },
                new() { LoincCode = "718-7", TestName = "Hemoglobin", ExpectedRange = new ResultRange { MinValue = 9.0, MaxValue = 16.0, Units = "g/dL" }, OrderProbability = 0.85 },
                new() { LoincCode = "5902-2", TestName = "Prothrombin Time", ExpectedRange = new ResultRange { MinValue = 10, MaxValue = 14, Units = "s" }, OrderProbability = 0.75 }
            }
        };
    }

    private ClinicalScenario CreatePregnancyScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "pregnancy",
            Name = "Normal Pregnancy and Delivery",
            Weight = 10,
            // O80 (delivery) is female-only and childbearing-age; couple the patient so the sex and
            // age can never contradict the diagnosis (no O80 on a male, no delivery at age 3).
            Constraints = new ScenarioConstraints { Pregnant = true, RequiredSex = Gender.Female, AgeYears = (12, 55) },
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "O80", Description = "Encounter for full-term uncomplicated delivery", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>(),
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Prenatal Vitamins", DrugClass = "Vitamin/Mineral", TypicalDosage = "1 tab daily", PrescriptionProbability = 0.95 },
                new() { GenericName = "Oxytocin", DrugClass = "Uterotonic", TypicalDosage = "10 units IV", PrescriptionProbability = 0.6 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "19080-1", TestName = "HCG", ExpectedRange = new ResultRange { MinValue = 5, MaxValue = 200000, Units = "mIU/mL" }, OrderProbability = 0.85 },
                new() { LoincCode = "882-1", TestName = "ABO Blood Type", OrderProbability = 0.9 },
                new() { LoincCode = "6690-2", TestName = "WBC", ExpectedRange = new ResultRange { MinValue = 6.0, MaxValue = 16.0, Units = "K/uL" }, OrderProbability = 0.8 }
            }
        };
    }

    private ClinicalScenario CreatePediatricURIScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "pediatric_uri",
            Name = "Pediatric Upper Respiratory Infection",
            Weight = 12,
            // A pediatric scenario must land on a child; pin the age band so the patient's age agrees
            // with the "pediatric" framing (sex is unconstrained here).
            Constraints = new ScenarioConstraints { AgeYears = (0, 17) },
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "J06.9", Description = "Acute upper respiratory infection, unspecified", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "J02.9", Description = "Acute pharyngitis, unspecified", CodingSystem = "ICD10" },
                new() { Code = "H66.90", Description = "Otitis media, unspecified", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Amoxicillin", DrugClass = "Antibiotic", TypicalDosage = "250mg TID", PrescriptionProbability = 0.5 },
                new() { GenericName = "Ibuprofen", DrugClass = "NSAID/Antipyretic", TypicalDosage = "100mg/5mL q6h", PrescriptionProbability = 0.7 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "6690-2", TestName = "WBC", ExpectedRange = new ResultRange { MinValue = 5.0, MaxValue = 15.0, Units = "K/uL" }, OrderProbability = 0.5 },
                new() { LoincCode = "18481-2", TestName = "Rapid Strep", OrderProbability = 0.6 }
            }
        };
    }

    private ClinicalScenario CreateDepressionScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "depression",
            Name = "Major Depressive Disorder",
            Weight = 12,
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "F32.1", Description = "Major depressive disorder, single episode, moderate", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "F41.1", Description = "Generalized anxiety disorder", CodingSystem = "ICD10" },
                new() { Code = "G47.00", Description = "Insomnia, unspecified", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Sertraline", DrugClass = "SSRI", TypicalDosage = "50-200mg daily", PrescriptionProbability = 0.65 },
                new() { GenericName = "Bupropion", DrugClass = "NDRI", TypicalDosage = "150mg daily", PrescriptionProbability = 0.3 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "3016-3", TestName = "TSH", ExpectedRange = new ResultRange { MinValue = 0.4, MaxValue = 4.0, Units = "mIU/L" }, OrderProbability = 0.7 },
                new() { LoincCode = "24323-8", TestName = "CMP", OrderProbability = 0.5 }
            }
        };
    }

    private ClinicalScenario CreateCirrhosisScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "cirrhosis",
            Name = "Alcoholic Cirrhosis of Liver",
            Weight = 6,
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "K70.30", Description = "Alcoholic cirrhosis of liver without ascites", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "K76.6", Description = "Portal hypertension", CodingSystem = "ICD10" },
                new() { Code = "F10.20", Description = "Alcohol dependence, uncomplicated", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Lactulose", DrugClass = "Osmotic Laxative", TypicalDosage = "30mL TID", PrescriptionProbability = 0.7 },
                new() { GenericName = "Spironolactone", DrugClass = "Potassium-Sparing Diuretic", TypicalDosage = "25-100mg daily", PrescriptionProbability = 0.6 },
                new() { GenericName = "Furosemide", DrugClass = "Loop Diuretic", TypicalDosage = "40mg daily", PrescriptionProbability = 0.5 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "24325-3", TestName = "Hepatic Function Panel", OrderProbability = 0.9 },
                new() { LoincCode = "5902-2", TestName = "Prothrombin Time", ExpectedRange = new ResultRange { MinValue = 12, MaxValue = 25, Units = "s" }, OrderProbability = 0.85 },
                new() { LoincCode = "718-7", TestName = "Hemoglobin", ExpectedRange = new ResultRange { MinValue = 8.0, MaxValue = 14.0, Units = "g/dL" }, OrderProbability = 0.75 }
            }
        };
    }

    private ClinicalScenario CreateHypothyroidScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "hypothyroid",
            Name = "Hypothyroidism",
            Weight = 10,
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "E03.9", Description = "Hypothyroidism, unspecified", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "E78.5", Description = "Hyperlipidemia, unspecified", CodingSystem = "ICD10" },
                new() { Code = "E66.9", Description = "Obesity, unspecified", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Levothyroxine", DrugClass = "Thyroid Hormone", TypicalDosage = "50-200mcg daily", PrescriptionProbability = 0.9 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "3016-3", TestName = "TSH", ExpectedRange = new ResultRange { MinValue = 5.0, MaxValue = 50.0, Units = "mIU/L" }, OrderProbability = 0.95 },
                new() { LoincCode = "3024-7", TestName = "Free T4", ExpectedRange = new ResultRange { MinValue = 0.1, MaxValue = 0.8, Units = "ng/dL" }, OrderProbability = 0.8 }
            }
        };
    }

    private ClinicalScenario CreateChronicKidneyDiseaseScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "ckd",
            Name = "Chronic Kidney Disease, Stage 3",
            Weight = 10,
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "N18.3", Description = "Chronic kidney disease, stage 3", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "I10", Description = "Essential hypertension", CodingSystem = "ICD10" },
                new() { Code = "E11.9", Description = "Type 2 diabetes mellitus", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Lisinopril", DrugClass = "ACE Inhibitor", TypicalDosage = "10-40mg daily", PrescriptionProbability = 0.7 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "2160-0", TestName = "Creatinine", ExpectedRange = new ResultRange { MinValue = 1.2, MaxValue = 3.0, Units = "mg/dL" }, OrderProbability = 0.9 },
                new() { LoincCode = "3094-0", TestName = "BUN", ExpectedRange = new ResultRange { MinValue = 15, MaxValue = 40, Units = "mg/dL" }, OrderProbability = 0.8 },
                new() { LoincCode = "2823-3", TestName = "Potassium", ExpectedRange = new ResultRange { MinValue = 3.5, MaxValue = 5.5, Units = "mmol/L" }, OrderProbability = 0.7 }
            }
        };
    }

    /// <summary>
    /// Sepsis - High-acuity infectious scenario
    /// </summary>
    private ClinicalScenario CreateSepsisScenario()
    {
        return new ClinicalScenario
        {
            ScenarioId = "sepsis",
            Name = "Sepsis (Systemic Inflammatory Response)",
            Weight = 15,
            PrimaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "A41.9", Description = "Sepsis, unspecified organism", CodingSystem = "ICD10" }
            },
            SecondaryDiagnoses = new List<DiagnosisCode>
            {
                new() { Code = "R65.20", Description = "Severe sepsis without septic shock", CodingSystem = "ICD10" },
                new() { Code = "R50.9", Description = "Fever, unspecified", CodingSystem = "ICD10" },
                new() { Code = "R06.02", Description = "Shortness of breath", CodingSystem = "ICD10" },
                new() { Code = "I95.9", Description = "Hypotension, unspecified", CodingSystem = "ICD10" }
            },
            TypicalMedications = new List<MedicationOption>
            {
                new() { GenericName = "Piperacillin-Tazobactam", DrugClass = "Antibiotic", TypicalDosage = "3.375-4.5g IV q6h", PrescriptionProbability = 0.9 },
                new() { GenericName = "Levofloxacin", DrugClass = "Antibiotic", TypicalDosage = "500-750mg IV daily", PrescriptionProbability = 0.8 },
                new() { GenericName = "Normal Saline", DrugClass = "Intravenous Fluid", TypicalDosage = "30mL/kg IV bolus", PrescriptionProbability = 0.95 },
                new() { GenericName = "Norepinephrine", DrugClass = "Vasopressor", TypicalDosage = "0.02-1.0 mcg/kg/min IV infusion", PrescriptionProbability = 0.6 }
            },
            RelevantLabTests = new List<LabTestOption>
            {
                new() { LoincCode = "6690-2", TestName = "WBC", ExpectedRange = new ResultRange { MinValue = 15.0, MaxValue = 28.0, Units = "K/uL" }, OrderProbability = 0.95 },
                new() { LoincCode = "2524-7", TestName = "Lactate", ExpectedRange = new ResultRange { MinValue = 2.2, MaxValue = 6.5, Units = "mmol/L" }, OrderProbability = 0.9 },
                new() { LoincCode = "1988-5", TestName = "CRP", ExpectedRange = new ResultRange { MinValue = 50.0, MaxValue = 300.0, Units = "mg/L" }, OrderProbability = 0.8 },
                new() { LoincCode = "34728-6", TestName = "Procalcitonin", ExpectedRange = new ResultRange { MinValue = 2.0, MaxValue = 20.0, Units = "ng/mL" }, OrderProbability = 0.75 }
            }
        };
    }
}
