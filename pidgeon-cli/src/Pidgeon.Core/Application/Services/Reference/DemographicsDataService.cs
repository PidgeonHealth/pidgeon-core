// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Domain.Clinical.Entities;

namespace Pidgeon.Core.Application.Services.Reference;

/// <summary>
/// Service for accessing demographic datasets for realistic data generation.
/// Table JSON is served through the registered <see cref="IDataResourceResolver"/>
/// composition. Absent tables yield empty lists and callers fall back to their
/// hardcoded defaults, so generation degrades to placeholder demographics
/// rather than failing.
/// </summary>
public partial class DemographicsDataService : IDemographicsDataService
{
    private readonly ILogger<DemographicsDataService> _logger;
    private readonly IDataResourceResolver _resourceResolver;
    private readonly string _dataPathPrefix;
    private readonly Dictionary<string, List<string>> _cachedTables = new();
    private readonly object _cacheLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public DemographicsDataService(
        ILogger<DemographicsDataService> logger,
        IDataResourceResolver resourceResolver,
        string dataPathPrefix = "standards/hl7/v23/tables")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
        _dataPathPrefix = dataPathPrefix ?? throw new ArgumentNullException(nameof(dataPathPrefix));
    }

    public async Task<List<string>> GetFirstNamesAsync(string? gender = null)
    {
        var tableName = gender?.ToLowerInvariant() switch
        {
            "male" or "m" => "FirstNameMale",
            "female" or "f" => "FirstNameFemale",
            _ => "FirstName"
        };

        return await GetTableValuesAsync(tableName);
    }

    public async Task<List<string>> GetLastNamesAsync()
    {
        return await GetTableValuesAsync("LastName");
    }

    public async Task<List<string>> GetZipCodesAsync()
    {
        return await GetTableValuesAsync("ZipCode");
    }

    public async Task<List<string>> GetCitiesAsync()
    {
        return await GetTableValuesAsync("City");
    }

    public async Task<List<string>> GetStatesAsync()
    {
        return await GetTableValuesAsync("State");
    }

    public async Task<List<string>> GetStreetsAsync()
    {
        return await GetTableValuesAsync("Street");
    }

    public async Task<List<string>> GetPhoneNumbersAsync()
    {
        return await GetTableValuesAsync("PhoneNumber");
    }

    public async Task<List<string>> GetTableValuesAsync(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return new List<string>();

        // Check cache first
        lock (_cacheLock)
        {
            if (_cachedTables.TryGetValue(tableName, out var cachedValues))
                return cachedValues;
        }

        try
        {
            var resourcePath = $"{_dataPathPrefix}/{tableName}.json";
            var streamResult = await _resourceResolver.OpenReadAsync(resourcePath);
            if (streamResult.IsFailure)
            {
                _logger.LogWarning("Demographics table resource unavailable: {ResourcePath}", resourcePath);
                return new List<string>();
            }

            string json;
            using (var stream = streamResult.Value)
            using (var reader = new StreamReader(stream))
            {
                json = await reader.ReadToEndAsync();
            }

            var tableData = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);

            var values = new List<string>();

            // Extract values from different possible structures
            if (tableData.TryGetProperty("values", out var valuesElement))
            {
                if (valuesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in valuesElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var value = item.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                                values.Add(value);
                        }
                        else if (item.ValueKind == JsonValueKind.Object)
                        {
                            // Try common property names for the actual value
                            var valueProps = new[] { "value", "name", "display", "code" };
                            foreach (var prop in valueProps)
                            {
                                if (item.TryGetProperty(prop, out var propElement) &&
                                    propElement.ValueKind == JsonValueKind.String)
                                {
                                    var value = propElement.GetString();
                                    if (!string.IsNullOrWhiteSpace(value))
                                    {
                                        values.Add(value);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // If no values found yet, try to extract from table entries
            if (values.Count == 0 && tableData.TryGetProperty("entries", out var entriesElement))
            {
                if (entriesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in entriesElement.EnumerateArray())
                    {
                        if (entry.TryGetProperty("value", out var valueElement) &&
                            valueElement.ValueKind == JsonValueKind.String)
                        {
                            var value = valueElement.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                                values.Add(value);
                        }
                    }
                }
            }

            // Cache the results
            lock (_cacheLock)
            {
                _cachedTables[tableName] = values;
            }

            _logger.LogDebug("Loaded {Count} values from table {TableName}", values.Count, tableName);
            return values;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading table values for {TableName}", tableName);
            return new List<string>();
        }
    }

    public async Task<string> GetRandomValueAsync(string tableName, Random random)
    {
        var values = await GetTableValuesAsync(tableName);
        return values.Count > 0 ? values[random.Next(values.Count)] : "UNKNOWN";
    }

    public async Task<(string firstName, string lastName, string gender)> GenerateRandomNameAsync(Random random)
    {
        // Randomly select gender first
        var genders = new[] { "Male", "Female" };
        var gender = genders[random.Next(genders.Length)];

        // Get names based on gender
        var firstNames = await GetFirstNamesAsync(gender);
        var lastNames = await GetLastNamesAsync();

        var firstName = firstNames.Count > 0 ? firstNames[random.Next(firstNames.Count)] : "Unknown";
        var lastName = lastNames.Count > 0 ? lastNames[random.Next(lastNames.Count)] : "Unknown";

        return (firstName, lastName, gender);
    }

    public async Task<(string firstName, string lastName, string gender)> GenerateRandomNameAsync(Random random, string? sex)
    {
        var resolved = sex?.Trim().ToLowerInvariant() switch
        {
            "male" or "m" => "Male",
            "female" or "f" => "Female",
            _ => null
        };

        // No usable sex → keep the sexless overload's independent-gender draw so unknown-sex
        // patients still receive a name (the name-vs-sex check only judges M/F, never Unknown).
        if (resolved is null)
            return await GenerateRandomNameAsync(random);

        var firstNames = await GetFirstNamesAsync(resolved);
        var lastNames = await GetLastNamesAsync();

        var firstName = firstNames.Count > 0 ? firstNames[random.Next(firstNames.Count)] : "Unknown";
        var lastName = lastNames.Count > 0 ? lastNames[random.Next(lastNames.Count)] : "Unknown";

        return (firstName, lastName, resolved);
    }

    public async Task<Address> GenerateRandomAddressAsync(Random random)
    {
        var streets = await GetStreetsAsync();
        var cities = await GetCitiesAsync();
        var states = await GetStatesAsync();
        var zipCodes = await GetZipCodesAsync();

        var street = streets.Count > 0 ? streets[random.Next(streets.Count)] : "Main St";
        var houseNumber = random.Next(1, 9999);
        var city = cities.Count > 0 ? cities[random.Next(cities.Count)] : "Anytown";
        var state = states.Count > 0 ? states[random.Next(states.Count)] : "CA";
        var zipCode = zipCodes.Count > 0 ? zipCodes[random.Next(zipCodes.Count)] : "90210";

        return new Address
        {
            Street1 = $"{houseNumber} {street}",
            City = city,
            State = state,
            PostalCode = zipCode,
            Country = "USA"
        };
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cachedTables.Clear();
        }

        _logger.LogInformation("Demographics data cache cleared");
    }
}
