// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.DTOs.Data;

namespace Pidgeon.Core.Application.Interfaces.Data;

/// <summary>
/// Data source for medication information.
/// Provides standard-agnostic medication data for use across HL7, FHIR, and NCPDP.
/// </summary>
public interface IMedicationDataSource
{
    /// <summary>
    /// Gets all available medications.
    /// </summary>
    Task<IReadOnlyList<MedicationData>> GetMedicationsAsync();

    /// <summary>
    /// Gets a random medication.
    /// </summary>
    Task<MedicationData> GetRandomMedicationAsync();
}
