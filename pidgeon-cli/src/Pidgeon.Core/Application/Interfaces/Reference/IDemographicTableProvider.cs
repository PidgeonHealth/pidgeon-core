// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Reference;

/// <summary>
/// Supplies static demographic value lists (first names, last names, cities, etc.)
/// loaded once from embedded <c>Pidgeon.Data</c> JSON resources.
///
/// Registered as a Singleton with a thread-safe internal cache so the same
/// 20-ish tables are read from disk once per process rather than once per
/// request scope.
/// </summary>
public interface IDemographicTableProvider
{
    /// <summary>
    /// Returns the full value list for a demographic table (e.g. "FirstName",
    /// "LastName", "City"). Returns an empty list when the table is unknown;
    /// never returns null. Thread-safe; safe to call from batch generation.
    /// </summary>
    IReadOnlyList<string> GetValues(string tableName);
}
