// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.DTOs.Data;

namespace Pidgeon.Core.Application.Interfaces.Data;

/// <summary>
/// Data source for RxNorm prescribable drug concepts.
/// Provides standard-agnostic drug data for use across HL7, FHIR, and other standards.
/// </summary>
public interface IRxNormDataSource
{
    /// <summary>
    /// Gets all available RxNorm drug records.
    /// </summary>
    Task<IReadOnlyList<RxNormDrugData>> GetDrugsAsync();

    /// <summary>
    /// Gets a random RxNorm drug record.
    /// </summary>
    Task<RxNormDrugData?> GetRandomDrugAsync();

    /// <summary>
    /// Gets RxNorm drugs matching a term type (e.g., "SCD", "SBD", "IN", "BN").
    /// </summary>
    Task<IReadOnlyList<RxNormDrugData>> GetDrugsByTermTypeAsync(string termType);

    /// <summary>
    /// Searches RxNorm drugs by keyword in name or ingredient name.
    /// </summary>
    Task<IReadOnlyList<RxNormDrugData>> SearchDrugsAsync(string keyword);
}
