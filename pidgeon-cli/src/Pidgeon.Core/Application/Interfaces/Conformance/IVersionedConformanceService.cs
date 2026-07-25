// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Conformance;

namespace Pidgeon.Core.Application.Interfaces.Conformance;

/// <summary>
/// The local model-to-oracle conformance function: validates locally produced content
/// (generated messages/resources) against an independent standards oracle that this codebase
/// does not author, giving real proof of conformance rather than the engine grading its own
/// homework.
///
/// This is DISTINCT from <see cref="IConformanceService"/>, which probes remote FHIR endpoints
/// over HTTP. This service performs no network I/O; it resolves an oracle by the target's
/// standard and runs the content through it locally.
/// </summary>
public interface IVersionedConformanceService
{
    /// <summary>
    /// Checks <paramref name="content"/> against the independent oracle for
    /// <paramref name="target"/>. Returns <see cref="ConformanceLevel.NotEvaluated"/> when no
    /// oracle is registered for the target's standard/version.
    /// </summary>
    VersionedConformanceResult Check(string content, ConformanceTarget target);
}
