// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Interfaces.Reference;

/// <summary>
/// Service for accessing demographic datasets for realistic data generation.
/// Provides access to comprehensive demographic values across multiple categories.
/// </summary>
public interface IDemographicsDataService
{
    /// <summary>
    /// Gets first names from our demographic datasets.
    /// </summary>
    /// <param name="gender">Optional gender filter: "male", "female", or null for all</param>
    /// <returns>List of first names from FirstName.json, FirstNameMale.json, or FirstNameFemale.json</returns>
    Task<List<string>> GetFirstNamesAsync(string? gender = null);

    /// <summary>
    /// Gets last names from LastName.json demographic dataset.
    /// </summary>
    Task<List<string>> GetLastNamesAsync();

    /// <summary>
    /// Gets ZIP codes from ZipCode.json (101 real US postal codes).
    /// </summary>
    Task<List<string>> GetZipCodesAsync();

    /// <summary>
    /// Gets cities from City.json demographic dataset.
    /// </summary>
    Task<List<string>> GetCitiesAsync();

    /// <summary>
    /// Gets states from State.json demographic dataset.
    /// </summary>
    Task<List<string>> GetStatesAsync();

    /// <summary>
    /// Gets street names from Street.json demographic dataset.
    /// </summary>
    Task<List<string>> GetStreetsAsync();

    /// <summary>
    /// Gets phone number patterns from PhoneNumber.json demographic dataset.
    /// </summary>
    Task<List<string>> GetPhoneNumbersAsync();

    /// <summary>
    /// Gets values from any table in our demographic dataset foundation.
    /// </summary>
    /// <param name="tableName">Name of the table (e.g., "FirstName", "ZipCode")</param>
    Task<List<string>> GetTableValuesAsync(string tableName);

    /// <summary>
    /// Gets a random value from the specified table.
    /// </summary>
    Task<string> GetRandomValueAsync(string tableName, Random random);

    /// <summary>
    /// Generates a random name using demographic datasets.
    /// Uses data-driven approach instead of hardcoded arrays.
    /// </summary>
    Task<(string firstName, string lastName, string gender)> GenerateRandomNameAsync(Random random);

    /// <summary>
    /// Generates a random name whose given name is drawn from the sex-matched first-name dataset for
    /// <paramref name="sex"/> ("male"/"female"/"m"/"f"), so a patient's given name agrees with their
    /// administrative sex (PID-5 must not fight PID-8). A null/blank/unrecognised sex falls back to the
    /// mixed dataset — identical to the sexless overload — so an unknown-sex patient still gets a name.
    /// Returns the resolved sex so callers can log or assert it.
    /// </summary>
    Task<(string firstName, string lastName, string gender)> GenerateRandomNameAsync(Random random, string? sex);

    /// <summary>
    /// Generates a realistic address using geographic datasets.
    /// </summary>
    Task<Pidgeon.Core.Domain.Clinical.Entities.Address> GenerateRandomAddressAsync(Random random);

    /// <summary>
    /// Clears the internal cache of demographic data.
    /// </summary>
    void ClearCache();
}