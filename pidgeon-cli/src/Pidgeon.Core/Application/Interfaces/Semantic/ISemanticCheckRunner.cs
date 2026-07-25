// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Validation;

namespace Pidgeon.Core.Application.Interfaces.Semantic;

/// <summary>
/// Runs every registered <see cref="ISemanticCheck"/> over one message and
/// aggregates their findings (Semantic Validation Program L2). The composite is
/// the integration seam: callers render the snapshot once, pass it here, and add
/// the returned findings to <c>ValidationResult.SemanticFindings</c> /
/// <c>CoherenceResult.SemanticFindings</c> — the advisory channel, never the
/// pass/fail channel. Rendering failure or an individual check throwing yields no
/// findings rather than an error: the semantic layer is advisory and must never
/// destabilize the structural result it rides beside.
/// </summary>
public interface ISemanticCheckRunner
{
    /// <summary>
    /// Renders the message to a clinical snapshot and runs every registered check,
    /// returning the union of their findings (empty when the message is clean or
    /// cannot be rendered). Never throws.
    /// </summary>
    IReadOnlyList<SemanticFinding> Run(string messageContent);
}
