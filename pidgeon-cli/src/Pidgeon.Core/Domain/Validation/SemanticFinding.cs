// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;

namespace Pidgeon.Core.Domain.Validation;

/// <summary>
/// Where a semantic finding came from: a deterministic clinical check or the advisory LLM judge.
/// </summary>
public enum SemanticFindingSource
{
    /// <summary>Emitted by a deterministic, dataset-fed clinical check.</summary>
    Rule,

    /// <summary>Emitted by the advisory on-device LLM semantic judge.</summary>
    Judge
}

/// <summary>
/// A single clinical-sense observation about a structurally valid message — the additive
/// semantic channel beside spec-conformance issues. A semantic finding is advisory by
/// construction: <see cref="Severity"/> is fixed at <see cref="ValidationSeverity.Advisory"/>
/// with no escalation path, so it can never fail a message, block a pipeline, or trigger
/// remediation (the program's liability posture; human-in-the-loop always).
/// </summary>
public sealed record SemanticFinding
{
    private readonly double _confidence;

    /// <summary>The canonical fault class this finding reports (SEM-F01..SEM-F16).</summary>
    public required SemanticFaultClass FaultClass { get; init; }

    /// <summary>Human-readable statement of what looks clinically wrong and why.</summary>
    public required string Finding { get; init; }

    /// <summary>
    /// Detector confidence in [0, 1]. Deterministic rules typically emit 1.0; judge output is
    /// calibrated per model × check, and sub-threshold findings are suppressed upstream.
    /// </summary>
    public required double Confidence
    {
        get => _confidence;
        init => _confidence = value is >= 0.0 and <= 1.0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Confidence), value, "Confidence must be within [0, 1].");
    }

    /// <summary>Message locations the finding is grounded in (e.g. "RXE.3", "OBX[2].5").</summary>
    public IReadOnlyList<string> EvidenceFields { get; init; } = Array.Empty<string>();

    /// <summary>Which detector tier produced this finding.</summary>
    public required SemanticFindingSource Source { get; init; }

    /// <summary>
    /// Always <see cref="ValidationSeverity.Advisory"/>. Deliberately get-only: the advisory
    /// ceiling is enforced by the type system, not by convention.
    /// </summary>
    public ValidationSeverity Severity => ValidationSeverity.Advisory;
}
