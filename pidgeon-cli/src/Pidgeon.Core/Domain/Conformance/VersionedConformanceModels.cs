// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using System.Linq;
using Pidgeon.Core.Domain.Validation;

namespace Pidgeon.Core.Domain.Conformance;

// Local model-to-oracle conformance surface (engine-hardening lane L1).
// Distinct from the live-endpoint probe types in ConformanceModels.cs, which describe HTTP
// probing of remote FHIR endpoints. These types describe whether locally generated content
// conforms to an independent standards oracle (e.g. NCPDP SCRIPT XSD validation).

/// <summary>
/// Identifies which oracle applies to a piece of content: the (standard, version, artifact)
/// tuple. Standard and Version are strings so a new standard plugs in without a core change;
/// ArtifactType is optional (e.g. "NewRx") for finer reporting.
/// </summary>
public sealed record ConformanceTarget(
    string Standard,
    string Version,
    string? ArtifactType = null);

/// <summary>
/// Outcome of an independent conformance check. NotEvaluated means no oracle was available for
/// the target's standard/version — honest signaling that the standard is not independently
/// validated yet.
/// </summary>
public enum ConformanceLevel
{
    Conformant,
    NonConformant,
    NotEvaluated
}

/// <summary>
/// A single conformance deviation surfaced by an oracle. Reuses the shared
/// <see cref="ValidationSeverity"/> {Error, Warning, Info} so callers need not learn a second
/// severity scale.
/// </summary>
public sealed record ConformanceFinding(
    ValidationSeverity Severity,
    string Message,
    string? Location = null);

/// <summary>
/// Result of running content through an independent oracle for a given target.
/// </summary>
public sealed record VersionedConformanceResult(
    ConformanceTarget Target,
    ConformanceLevel Level,
    IReadOnlyList<ConformanceFinding> Findings)
{
    public bool IsConformant => Level == ConformanceLevel.Conformant;

    public bool HasErrors => Findings.Any(f => f.Severity == ValidationSeverity.Error);
}
