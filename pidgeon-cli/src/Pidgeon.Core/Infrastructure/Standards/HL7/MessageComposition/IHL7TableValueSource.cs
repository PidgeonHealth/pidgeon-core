// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Selects random values from HL7 coded-value tables and demographic tables during field
/// generation. Each draw reads its table's coordinate off the segment key on
/// <see cref="SegmentGenerationContext"/>, so output stays reproducible for a given seed.
/// </summary>
public interface IHL7TableValueSource
{
    /// <summary>
    /// Returns the Code of a random entry from the HL7 table with the given id, or an empty
    /// string when the table is missing or has no values. Draws once from the table coordinate.
    /// </summary>
    Task<string> GenerateValueFromTableAsync(int tableId, IHL7TableProvider tableProvider, SegmentGenerationContext context);

    /// <summary>
    /// Returns a random value from a demographic table (FirstName, LastName, City, …), or
    /// null when the table is empty. Draws once from the table coordinate.
    /// </summary>
    Task<string?> GenerateValueFromDemographicTableAsync(string tableName, SegmentGenerationContext context);
}
