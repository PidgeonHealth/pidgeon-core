// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pidgeon.Core.Application.Interfaces.Capability;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Domain.Capability;

namespace Pidgeon.Core.Application.Services.Capability;

/// <summary>
/// Assembles the capability matrix from registered components. It enumerates every generatable
/// (standard, type) from the generation plugins, expands each standard along its registered
/// <see cref="IStandardVersionSource"/> (single-version when none is registered), and asks the
/// <see cref="ICapabilityEvidenceSource"/> to evaluate each cell from real evidence.
///
/// Holds no standard, version, or type literals — every such string is sourced from a registered
/// component (plugin <c>StandardName</c>, plugin <c>GetSupportedMessageTypes()</c>, version source).
/// This keeps the service standard-agnostic per architecture red line #5.
///
/// The report carries TWO axes. The generate axis (<see cref="CapabilityReport.Cells"/>) is built
/// above. The validate axis (<see cref="CapabilityReport.ValidateCells"/>) is built from the
/// registered <see cref="IValidateCapabilitySource"/>s — standards we validate but do not generate
/// (today, C-CDA). The axes are independent: a standard on the validate axis makes no generation
/// claim, which is the whole point of keeping them separate.
/// </summary>
internal sealed class CapabilitySignalingService : ICapabilitySignalingService
{
    private readonly IReadOnlyList<IMessageGenerationPlugin> _generationPlugins;
    private readonly IReadOnlyList<IStandardVersionSource> _versionSources;
    private readonly IReadOnlyList<IValidateCapabilitySource> _validateSources;
    private readonly IReadOnlyList<IGenerationTierSource> _tierSources;
    private readonly ICapabilityEvidenceSource _evidence;
    private readonly TimeProvider _time;

    public CapabilitySignalingService(
        IEnumerable<IMessageGenerationPlugin> generationPlugins,
        IEnumerable<IStandardVersionSource> versionSources,
        IEnumerable<IValidateCapabilitySource> validateSources,
        ICapabilityEvidenceSource evidence,
        TimeProvider time,
        IEnumerable<IGenerationTierSource>? tierSources = null)
    {
        _generationPlugins = (generationPlugins ?? throw new ArgumentNullException(nameof(generationPlugins))).ToList();
        _versionSources = (versionSources ?? throw new ArgumentNullException(nameof(versionSources))).ToList();
        _validateSources = (validateSources ?? throw new ArgumentNullException(nameof(validateSources))).ToList();
        _tierSources = tierSources?.ToList() ?? new List<IGenerationTierSource>();
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task<CapabilityReport> DescribeCapabilitiesAsync(
        CapabilityQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        var cells = new List<CapabilityCell>();

        foreach (var plugin in _generationPlugins)
        {
            var standard = plugin.StandardName;
            if (query?.Standard is { } wantStandard &&
                !string.Equals(standard, wantStandard, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var versions = VersionsFor(standard, query?.Version);
            var types = plugin.GetSupportedMessageTypes()
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Tier axis is optional per standard: null when no registered source
            // claims it. The tier string comes from the source (plugin-side), so
            // the service stays free of standard-specific labels.
            var tierSource = _tierSources.FirstOrDefault(
                s => string.Equals(s.StandardName, standard, StringComparison.OrdinalIgnoreCase));

            foreach (var version in versions)
            {
                foreach (var type in types)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var evidence = await _evidence
                        .EvaluateAsync(standard, type, version, cancellationToken)
                        .ConfigureAwait(false);
                    cells.Add(new CapabilityCell(standard, type, version, evidence.Level, evidence.Basis)
                    {
                        GenerationTier = tierSource?.DescribeTier(type),
                    });
                }
            }
        }

        var ordered = cells
            .OrderBy(c => c.Standard, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.MessageType, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var validateCells = BuildValidateCells(query, cancellationToken);

        return new CapabilityReport(ordered, _time.GetUtcNow()) { ValidateCells = validateCells };
    }

    /// <summary>
    /// The validate axis: each registered <see cref="IValidateCapabilitySource"/> describes how
    /// strongly we can validate its standard's artifacts. Honors the query's standard filter so a
    /// scoped query (e.g. --standard hl7) does not surface unrelated validate rows. Validate cells
    /// carry no version dimension of their own, so a version filter resolves through the standard's
    /// registered <see cref="IStandardVersionSource"/>: a query naming a version the standard
    /// carries (e.g. --standard x12 --std-version 5010) keeps its rows; a version the standard does
    /// not carry — or a standard with no version source at all (C-CDA) — yields none.
    /// </summary>
    private IReadOnlyList<ValidateCapabilityCell> BuildValidateCells(
        CapabilityQuery? query,
        CancellationToken cancellationToken)
    {
        var validateCells = new List<ValidateCapabilityCell>();
        foreach (var source in _validateSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (query?.Standard is { } wantStandard &&
                !string.Equals(source.StandardName, wantStandard, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (query?.Version is { } wantVersion && !CarriesVersion(source.StandardName, wantVersion))
                continue;

            validateCells.AddRange(source.Describe());
        }

        return validateCells
            .OrderBy(c => c.Standard, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.ArtifactType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// True when the standard's registered version source lists the requested version. A standard
    /// with no version source carries no named versions, so any version filter excludes it.
    /// </summary>
    private bool CarriesVersion(string standard, string version) =>
        _versionSources.Any(s =>
            string.Equals(s.StandardName, standard, StringComparison.OrdinalIgnoreCase) &&
            s.GetSupportedVersions().Any(v => string.Equals(v, version, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The version axis for a standard: the matching version source's versions, optionally
    /// filtered by the query; a single null version when no source is registered (single-version
    /// standards such as FHIR R4).
    /// </summary>
    private IReadOnlyList<string?> VersionsFor(string standard, string? wantVersion)
    {
        var source = _versionSources.FirstOrDefault(
            s => string.Equals(s.StandardName, standard, StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            // Single-version standard. A version filter that names a version this standard
            // does not carry yields no cells.
            return wantVersion is null ? new string?[] { null } : Array.Empty<string?>();
        }

        var versions = source.GetSupportedVersions().AsEnumerable();
        if (wantVersion is not null)
        {
            versions = versions.Where(v => string.Equals(v, wantVersion, StringComparison.OrdinalIgnoreCase));
        }

        return versions.Cast<string?>().ToList();
    }
}
