// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using YamlDotNet.Serialization;

namespace Pidgeon.Core.Domain.Clinical;

/// <summary>
/// Manifest for a scenario pack: a named, versioned, tier-gated collection of clinical scenarios.
/// Packs are the unit of distribution for golden clinical acceptance suites.
/// </summary>
public class ScenarioPackManifest
{
    [YamlMember(Alias = "name")]
    public string Name { get; init; } = string.Empty;

    [YamlMember(Alias = "version")]
    public string Version { get; init; } = "1.0.0";

    [YamlMember(Alias = "tier")]
    public string Tier { get; init; } = "free";

    [YamlMember(Alias = "description")]
    public string Description { get; init; } = string.Empty;

    [YamlMember(Alias = "author")]
    public string Author { get; init; } = string.Empty;

    [YamlMember(Alias = "min_engine_version")]
    public string MinEngineVersion { get; init; } = "1.0.0";

    [YamlMember(Alias = "categories")]
    public List<PackCategory> Categories { get; init; } = new();

    [YamlMember(Alias = "total_scenarios")]
    public int TotalScenarios { get; init; }

    /// <summary>
    /// Optional relative path to the scenarios directory (relative to the pack.yaml location).
    /// If not specified, defaults to "scenarios/" subdirectory or the pack root directory.
    /// </summary>
    [YamlMember(Alias = "scenarios_path")]
    public string? ScenariosPath { get; init; }
}

/// <summary>
/// A category within a scenario pack (e.g., "Lab Orders", "Pharmacy").
/// </summary>
public class PackCategory
{
    [YamlMember(Alias = "name")]
    public string Name { get; init; } = string.Empty;

    [YamlMember(Alias = "scenario_count")]
    public int ScenarioCount { get; init; }

    [YamlMember(Alias = "description")]
    public string Description { get; init; } = string.Empty;
}
