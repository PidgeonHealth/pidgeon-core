// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;
using Pidgeon.Core.Domain.Capability;

namespace Pidgeon.Core.Application.Interfaces.Capability;

/// <summary>
/// Evaluates a single capability cell by gathering real evidence rather than asserting a claim.
/// The production implementation runs the same operation the conformance matrix tests perform —
/// generate a deterministic sample, validate it, and (where an oracle exists) check it against an
/// independent validator — so the resulting <see cref="CapabilityLevel"/> is re-proven from the
/// engine on every report. Separated from the signaling service so the matrix-assembly logic can
/// be unit-tested with a fake evidence source.
/// </summary>
public interface ICapabilityEvidenceSource
{
    /// <summary>
    /// Determines the capability level for one cell. <paramref name="version"/> is null for
    /// single-version standards. Implementations must not throw for an individual cell — a cell
    /// that fails to generate or validate is reported at the appropriate (lower) level with an
    /// explanatory basis, so one bad cell never sinks the whole report.
    /// </summary>
    Task<CapabilityEvidence> EvaluateAsync(
        string standard,
        string messageType,
        string? version,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Names the generation tier serving a message type for one standard — e.g. FHIR's
/// "clinical" (a curated hand-built builder: scenario-coherent content) versus
/// "structural" (the schema-derived synthesizer: valid instance, minimal population)
/// — so the capability matrix distinguishes the two claims per cell and structural
/// coverage can never borrow the clinical-coherence claim. Optional per standard:
/// the signaling service stamps null (no tier axis) when no source claims the
/// standard, so standards without tiers are unaffected. Tier strings are sourced
/// here, never hardcoded in the signaling service (architecture red line 5).
/// </summary>
public interface IGenerationTierSource
{
    /// <summary>Standard this source describes (matches a generation plugin's StandardName).</summary>
    string StandardName { get; }

    /// <summary>The tier label serving <paramref name="messageType"/>, or null when undetermined.</summary>
    string? DescribeTier(string messageType);
}
