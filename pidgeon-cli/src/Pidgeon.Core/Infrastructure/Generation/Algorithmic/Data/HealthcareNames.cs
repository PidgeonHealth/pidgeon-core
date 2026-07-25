// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Generation.Algorithmic.Data;

/// <summary>
/// Free tier healthcare names dataset - 50 first names and 50 surnames.
/// Provides diverse, realistic names covering ~40% of US population patterns.
/// Curated for healthcare appropriateness and demographic representation.
/// </summary>
public static class HealthcareNames
{
    /// <summary>
    /// Top 35 male first names with demographic diversity.
    /// Covers most common male names in US healthcare settings across all ethnicities.
    /// </summary>
    public static readonly string[] MaleFirstNames = new[]
    {
        // Traditional male names (high frequency)
        "James", "John", "Robert", "Michael", "William", "David",
        "Richard", "Joseph", "Thomas", "Christopher", "Charles", "Daniel",
        "Matthew", "Anthony", "Mark", "Donald", "Steven", "Paul",
        "Andrew", "Kenneth", "Joshua", "Kevin", "Brian", "George",
        
        // Hispanic/Latino male names
        "Jose", "Antonio", "Carlos", "Luis", "Francisco",
        "Juan", "Ricardo", "Fernando", "Miguel", "Roberto",
        
        // African American male names
        "DeShawn", "Jamal", "Marcus", "Darius", "Terrell",
        
        // Asian male names (common in US)
        "Wei", "Hiroshi", "Raj", "Aiden", "Jin"
    };

    /// <summary>
    /// Top 35 female first names with demographic diversity.
    /// Covers most common female names in US healthcare settings across all ethnicities.
    /// </summary>
    public static readonly string[] FemaleFirstNames = new[]
    {
        // Traditional female names (high frequency)
        "Mary", "Patricia", "Jennifer", "Linda", "Elizabeth", "Barbara",
        "Susan", "Jessica", "Sarah", "Karen", "Lisa", "Nancy",
        "Betty", "Dorothy", "Sandra", "Donna", "Carol", "Ruth",
        "Sharon", "Michelle", "Kimberly", "Deborah", "Laura", "Cynthia",
        
        // Hispanic/Latina female names
        "Maria", "Anna", "Rosa", "Carmen", "Esperanza",
        "Gloria", "Teresa", "Juanita", "Francisca", "Guadalupe",
        
        // African American female names
        "Keisha", "Tamika", "Shanice", "Denise", "Monique",
        
        // Asian female names (common in US)
        "Mei", "Sakura", "Priya", "Grace", "Amy"
    };

    /// <summary>
    /// Combined array of all 70 first names for convenience.
    /// </summary>
    public static readonly string[] FirstNames = MaleFirstNames.Concat(FemaleFirstNames).ToArray();

    /// <summary>
    /// Top 50 US surnames with ethnic diversity.
    /// Covers approximately 25% of population, includes various cultural origins.
    /// </summary>
    public static readonly string[] LastNames = new[]
    {
        // Most common surnames
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia",
        "Miller", "Davis", "Rodriguez", "Martinez", "Hernandez", "Lopez",
        "Gonzalez", "Wilson", "Anderson", "Thomas", "Taylor", "Moore",
        "Jackson", "Martin", "Lee", "Perez", "Thompson", "White",
        "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson",
        
        // Additional diversity
        "Walker", "Young", "Allen", "King", "Wright", "Scott",
        "Torres", "Nguyen", "Hill", "Flores", "Green", "Adams",
        "Nelson", "Baker", "Hall", "Rivera", "Campbell", "Mitchell",
        "Carter", "Roberts"
    };

    /// <summary>
    /// Gets demographic weighting for ethnic representation in healthcare contexts.
    /// Based on US Census data adapted for healthcare population patterns.
    /// </summary>
    public static readonly Dictionary<string, float> EthnicityWeights = new()
    {
        ["White"] = 0.60f,
        ["Hispanic"] = 0.18f,
        ["Black"] = 0.13f,
        ["Asian"] = 0.06f,
        ["Native American"] = 0.02f,
        ["Other"] = 0.01f
    };

    /// <summary>
    /// Gets gender distribution weights for healthcare patient generation.
    /// Slightly female-skewed to reflect healthcare utilization patterns.
    /// </summary>
    public static readonly Dictionary<string, float> GenderWeights = new()
    {
        ["F"] = 0.52f, // Females utilize healthcare more frequently
        ["M"] = 0.48f
    };

    /// <summary>
    /// Generates a random culturally-appropriate name combination.
    /// </summary>
    /// <param name="random">Random number generator (use seed for deterministic results)</param>
    /// <param name="preferredGender">Optional gender preference for first name selection</param>
    /// <returns>A tuple of (FirstName, LastName, Gender) with cultural consistency</returns>
    public static (string FirstName, string LastName, string Gender) GenerateRandomName(Random random, string? preferredGender = null)
    {
        // Determine gender
        var gender = preferredGender ?? GetRandomGender(random);
        
        // Select appropriate first name based on gender
        var firstNameArray = gender == "M" ? MaleFirstNames : FemaleFirstNames;
        var firstName = firstNameArray[random.Next(firstNameArray.Length)];
        var lastName = LastNames[random.Next(LastNames.Length)];
        
        // Simple cultural consistency - Hispanic first names with Hispanic surnames more likely
        if (IsHispanicName(firstName) && random.NextDouble() < 0.7)
        {
            var hispanicSurnames = new[] { "Garcia", "Rodriguez", "Martinez", "Hernandez", 
                                         "Lopez", "Gonzalez", "Perez", "Sanchez", "Ramirez", 
                                         "Torres", "Flores", "Rivera" };
            lastName = hispanicSurnames[random.Next(hispanicSurnames.Length)];
        }
        
        return (firstName, lastName, gender);
    }

    /// <summary>
    /// Determines the gender of a given first name.
    /// </summary>
    /// <param name="firstName">The first name to check</param>
    /// <returns>"M" for male, "F" for female, or null if name not found</returns>
    public static string? GetGenderForName(string firstName)
    {
        if (MaleFirstNames.Contains(firstName)) return "M";
        if (FemaleFirstNames.Contains(firstName)) return "F";
        return null;
    }

    /// <summary>
    /// Determines if a first name is commonly Hispanic/Latino.
    /// Used for cultural consistency in name pairing.
    /// </summary>
    private static bool IsHispanicName(string firstName) => firstName switch
    {
        // Male Hispanic names
        "Jose" or "Antonio" or "Carlos" or "Luis" or "Francisco" or 
        "Juan" or "Ricardo" or "Fernando" or "Miguel" or "Roberto" or
        // Female Hispanic names  
        "Maria" or "Anna" or "Rosa" or "Carmen" or "Esperanza" or "Gloria" or
        "Teresa" or "Juanita" or "Francisca" or "Guadalupe" => true,
        _ => false
    };

    /// <summary>
    /// Gets weighted random ethnicity based on healthcare demographics.
    /// </summary>
    public static string GetRandomEthnicity(Random random)
    {
        var value = (float)random.NextDouble();
        var cumulative = 0.0f;
        
        foreach (var kvp in EthnicityWeights)
        {
            cumulative += kvp.Value;
            if (value <= cumulative)
                return kvp.Key;
        }
        
        return "Other";
    }

    /// <summary>
    /// Gets weighted random gender based on healthcare utilization patterns.
    /// </summary>
    public static string GetRandomGender(Random random)
    {
        var value = (float)random.NextDouble();
        return value <= GenderWeights["F"] ? "F" : "M";
    }

    /// <summary>
    /// Gets a random first name for the specified gender.
    /// </summary>
    public static string GetRandomFirstName(Random random, string gender)
    {
        var nameArray = gender == "M" ? MaleFirstNames : FemaleFirstNames;
        return nameArray[random.Next(nameArray.Length)];
    }
}