// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Validation;

/// <summary>
/// The canonical semantic fault classes (SEM-F01..SEM-F16): content-level faults that are
/// structurally valid and therefore invisible to spec conformance checks. Rules emit
/// them, the judge is prompted per class against them, and the fault-injection benchmark
/// labels and scores by them.
/// Numeric values map 1:1 to the canonical IDs (1 = SEM-F01) and are append-only.
/// </summary>
public enum SemanticFaultClass
{
    /// <summary>SEM-F01 — dose off by an order of magnitude (10× / 0.1×).</summary>
    DoseMagnitudeError = 1,

    /// <summary>SEM-F02 — result reported in a unit that is wrong for the analyte.</summary>
    WrongUnitForAnalyte = 2,

    /// <summary>SEM-F03 — value outside physiologically survivable bounds.</summary>
    PhysiologicallyImpossibleValue = 3,

    /// <summary>SEM-F04 — medication without any supporting diagnosis context.</summary>
    MedicationDiagnosisMismatch = 4,

    /// <summary>SEM-F05 — clinically impossible event ordering or timing.</summary>
    ImpossibleTimeline = 5,

    /// <summary>SEM-F06 — procedure or diagnosis conflicting with patient sex.</summary>
    SexConflictingProcedureOrDiagnosis = 6,

    /// <summary>SEM-F07 — procedure or diagnosis conflicting with patient age.</summary>
    AgeConflictingProcedureOrDiagnosis = 7,

    /// <summary>SEM-F08 — ordered test incompatible with the specimen source.</summary>
    SpecimenTestMismatch = 8,

    /// <summary>SEM-F09 — administration route incompatible with the drug formulation.</summary>
    RouteFormulationMismatch = 9,

    /// <summary>SEM-F10 — concurrently ordered drugs with a known contraindication.</summary>
    ContraindicatedDrugPair = 10,

    /// <summary>SEM-F11 — brand and generic of the same drug active simultaneously.</summary>
    BrandGenericDuplicateTherapy = 11,

    /// <summary>SEM-F12 — vaccine administered outside its age window or minimum interval.</summary>
    VaccineScheduleViolation = 12,

    /// <summary>SEM-F13 — pediatric vitals implausible against growth-chart percentiles.</summary>
    GrowthImplausiblePediatricVitals = 13,

    /// <summary>SEM-F14 — drug ordered against a documented allergy or cross-reactive class.</summary>
    DrugAllergyClassConflict = 14,

    /// <summary>SEM-F15 — free-text narrative contradicting the coded values it accompanies.</summary>
    NarrativeContradictsCodedValues = 15,

    /// <summary>SEM-F16 — teratogenic or pregnancy-contraindicated drug with pregnancy context.</summary>
    TeratogenWithPregnancyContext = 16,

    /// <summary>SEM-F17 — OBX abnormal flag contradicting the value against its own reference range.</summary>
    AbnormalFlagValueContradiction = 17,

    /// <summary>SEM-F18 — a violated numeric identity across a panel of individually-plausible results.</summary>
    PanelArithmeticIncoherence = 18,

    /// <summary>SEM-F19 — a compound vital contradicting itself (systolic below diastolic).</summary>
    CompoundVitalContradiction = 19,

    /// <summary>SEM-F20 — prescription dispense quantity incoherent with dose, frequency, and days-supply.</summary>
    PrescriptionDispenseArithmeticIncoherence = 20,

    /// <summary>SEM-F21 — result value unit differing from the unit carried in its reference range.</summary>
    ValueUnitRangeUnitMismatch = 21,

    /// <summary>SEM-F22 — encounter class contradicting the clinical content it carries.</summary>
    EncounterClassContentMismatch = 22,

    /// <summary>SEM-F23 — message trigger intent contradicting the acts the message carries.</summary>
    MessageIntentContentMismatch = 23,

    /// <summary>SEM-F24 — weight-based dose incoherent with the patient weight in the same message.</summary>
    WeightBasedDoseIncoherence = 24,

    /// <summary>SEM-F25 — a monitoring result contradicting an active therapy in the same message.</summary>
    MonitoringResultTherapyContradiction = 25,

    /// <summary>SEM-F26 — an active order dated after the patient's recorded death.</summary>
    DeceasedPatientActiveOrder = 26,

    /// <summary>SEM-F27 — clinical absurdity carried where the coherence layer default-passes (no DG1/RXE).</summary>
    CoherenceDefaultPassExploit = 27
}
