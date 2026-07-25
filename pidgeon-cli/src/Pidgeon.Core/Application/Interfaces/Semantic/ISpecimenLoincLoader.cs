// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Semantic;

/// <summary>
/// Maps HL7 table 0070 specimen-source codes to the LOINC SYSTEM (specimen) axis
/// (Semantic Validation dataset #12). Enables SEM-F08 (specimen ↔ test mismatch):
/// a serum-only analyte reported on a urine specimen is detectable because the
/// specimen code resolves to a LOINC system that the analyte's own LOINC system
/// does not match. Lookup is case-insensitive on the 0070 code.
/// </summary>
public interface ISpecimenLoincLoader
{
    /// <summary>The LOINC SYSTEM-axis short name for an HL7 table 0070 specimen code; empty if not mapped.</summary>
    string GetLoincSystem(string hl7SpecimenCode);

    /// <summary>True if the 0070 specimen code maps to the given LOINC SYSTEM-axis short name.</summary>
    bool MapsToSystem(string hl7SpecimenCode, string loincSystem);

    /// <summary>
    /// The HL7 table 0070 specimen codes whose LOINC system is one of <paramref name="loincSystems"/>
    /// (case-insensitive), in the crosswalk's file order. The generation-side mirror of the F08
    /// injector's out-of-system picker: given an ordered analyte's acceptable systems (e.g. a serum
    /// chemistry's {"ser","plas"}), it yields the specimen codes coherent WITH that analyte so a
    /// generated OBR-15 / SPM-4 lands on a specimen the analyte's LOINC accepts. File order keeps a
    /// seeded pick reproducible. Empty when the crosswalk is unavailable or no code matches.
    /// </summary>
    IReadOnlyList<string> SpecimenCodesForSystems(IReadOnlyCollection<string> loincSystems);

    /// <summary>Total count of specimen→system rows loaded (0 if the dataset is unavailable).</summary>
    Result<int> GetLoadedRowCount();
}
