// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;
using Pidgeon.Core.Domain.Capability;

namespace Pidgeon.Core.Application.Interfaces.Capability;

/// <summary>
/// Surfaces the per-(type, version) capability matrix so our published claims match proven
/// reality. The matrix is derived at runtime from registered components and a live
/// generate→validate→oracle sweep — there is no hand-maintained claims table to drift.
/// </summary>
public interface ICapabilitySignalingService
{
    /// <summary>
    /// Builds the capability report. <paramref name="query"/> optionally scopes the sweep to a
    /// single standard/version (a full report sweeps every generatable cell, which is the
    /// honest default but costs proportionally more time). The sweep generates and validates a
    /// deterministic sample per cell, hence async.
    /// </summary>
    Task<CapabilityReport> DescribeCapabilitiesAsync(
        CapabilityQuery? query = null,
        CancellationToken cancellationToken = default);
}
