// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading;
using System.Threading.Tasks;
using Pidgeon.Core.Application.Interfaces;
using Pidgeon.Core.Application.Interfaces.Capability;
using Pidgeon.Core.Application.Interfaces.Conformance;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Domain.Capability;
using Pidgeon.Core.Domain.Conformance;
using Pidgeon.Core.Domain.Validation;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.Capability;

/// <summary>
/// Derives a cell's capability level from real evidence by re-running, at report time, the same
/// proof the conformance matrix tests perform:
///   1. generate one deterministic sample for the cell;
///   2. check it against any registered independent oracle (a validator we do not control);
///   3. validate it with our own validator in compatibility mode.
///
/// There is no asserted claims table — the level is what the engine actually proves now. Promotion
/// is gated on evidence, so the signal can only understate, never overstate, reality. A cell that
/// fails to generate is reported <see cref="CapabilityLevel.Unsupported"/>; one whose generation or
/// validation throws is caught and reported at the appropriate lower level with an explanatory
/// basis, so a single bad cell never sinks the whole report.
/// </summary>
internal sealed class RuntimeCapabilityEvidenceSource : ICapabilityEvidenceSource
{
    // Fixed seed so a report is reproducible and a cell's verdict is stable run-to-run. Mirrors the
    // deterministic seed the conformance matrix tests use.
    private const int SampleSeed = 4242;

    private readonly IMessageGenerationService _generation;
    private readonly IMessageValidationService _validation;
    private readonly IVersionedConformanceService _conformance;

    public RuntimeCapabilityEvidenceSource(
        IMessageGenerationService generation,
        IMessageValidationService validation,
        IVersionedConformanceService conformance)
    {
        _generation = generation ?? throw new ArgumentNullException(nameof(generation));
        _validation = validation ?? throw new ArgumentNullException(nameof(validation));
        _conformance = conformance ?? throw new ArgumentNullException(nameof(conformance));
    }

    public async Task<CapabilityEvidence> EvaluateAsync(
        string standard,
        string messageType,
        string? version,
        CancellationToken cancellationToken = default)
    {
        var versionLabel = version ?? "default";

        string content;
        try
        {
            // Version routes to the field the target standard's generation path consults: Hl7Version
            // for HL7, NcpdpVersion for NCPDP. A null version (or a standard with no version axis)
            // leaves the record defaults rather than forcing a value.
            var options = new GenerationOptions { Seed = SampleSeed };
            if (version is not null)
            {
                options = string.Equals(standard, "ncpdp", StringComparison.OrdinalIgnoreCase)
                    ? options with { NcpdpVersion = version }
                    : options with { Hl7Version = version, Hl7VersionExplicit = true };
            }

            var genResult = await _generation
                .GenerateSyntheticDataAsync(standard, messageType, 1, options)
                .ConfigureAwait(false);

            if (!genResult.IsSuccess || genResult.Value.Count == 0)
            {
                var reason = genResult.IsSuccess ? "produced no output" : genResult.Error.Message;
                return new CapabilityEvidence(
                    CapabilityLevel.Unsupported,
                    $"listed by {standard} plugin but does not generate at {versionLabel}: {reason}");
            }

            content = genResult.Value[0];
        }
        catch (Exception ex)
        {
            return new CapabilityEvidence(
                CapabilityLevel.Unsupported,
                $"generation threw for {standard} {messageType} @ {versionLabel}: {ex.Message}");
        }

        // Strongest tier: an independent oracle (a validator we do not control) attests the output.
        try
        {
            var target = new ConformanceTarget(standard, version ?? string.Empty, messageType);
            var conformance = _conformance.Check(content, target);
            if (conformance.Level == ConformanceLevel.Conformant)
            {
                return new CapabilityEvidence(
                    CapabilityLevel.IndependentlyValidated,
                    $"generates and passes an independent {standard} oracle ({conformance.Findings.Count} finding(s))");
            }

            // Not promoted to the top tier — fall through to our own validation, but remember the
            // independent result so the basis stays honest about it. An exhaustive switch keeps a
            // future ConformanceLevel from silently reading as "no oracle".
            var oracleNote = conformance.Level switch
            {
                ConformanceLevel.NonConformant => $"independent oracle found deviations ({conformance.Findings.Count})",
                ConformanceLevel.NotEvaluated => "no independent oracle for this cell",
                _ => $"independent oracle result: {conformance.Level}"
            };

            return await SelfValidatedEvidenceAsync(content, standard, versionLabel, oracleNote)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await SelfValidatedEvidenceAsync(
                    content, standard, versionLabel, $"independent oracle errored: {ex.Message}")
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Second tier: does the output pass our own validator in compatibility mode with zero
    /// error-severity issues? Zero errors ⇒ <see cref="CapabilityLevel.SpecValidated"/>; otherwise
    /// <see cref="CapabilityLevel.Generated"/>.
    /// </summary>
    private async Task<CapabilityEvidence> SelfValidatedEvidenceAsync(
        string content,
        string standard,
        string versionLabel,
        string oracleNote)
    {
        try
        {
            var valResult = await _validation
                .ValidateAsync(content, standard, null, ValidationMode.Compatibility)
                .ConfigureAwait(false);

            if (!valResult.IsSuccess)
            {
                return new CapabilityEvidence(
                    CapabilityLevel.Generated,
                    $"generates at {versionLabel}; own validation unavailable ({valResult.Error.Message}); {oracleNote}");
            }

            var errors = valResult.Value.ErrorCount;
            return errors == 0
                ? new CapabilityEvidence(
                    CapabilityLevel.SpecValidated,
                    $"generates and passes own compatibility validation at {versionLabel}; {oracleNote}")
                : new CapabilityEvidence(
                    CapabilityLevel.Generated,
                    $"generates at {versionLabel} but own compatibility validation found {errors} error(s); {oracleNote}");
        }
        catch (Exception ex)
        {
            return new CapabilityEvidence(
                CapabilityLevel.Generated,
                $"generates at {versionLabel}; own validation threw ({ex.Message}); {oracleNote}");
        }
    }
}
