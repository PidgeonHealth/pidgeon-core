// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Capability;

/// <summary>
/// The honest capability tier we can claim for VALIDATING one artifact type (e.g. a single
/// C-CDA document-level template) against a standard.
///
/// This is a SECOND, distinct axis from <see cref="CapabilityLevel"/>. That ladder is
/// generation-rooted — every rung means "output <em>generates</em> AND…". This ladder makes
/// no generation claim: it describes only how strongly we can VALIDATE an artifact we are
/// handed, for standards we check but do not produce (today, C-CDA). Reusing the generate
/// ladder here would falsely imply we generate conformant output for these standards — the
/// exact over-claim the capability model exists to prevent — so the two axes are kept
/// separate, and a future generate axis for C-CDA / X12 can slot alongside without collision.
///
/// The values are ordered ascending so callers can compare strength. As with the generate
/// ladder, each promotion is gated on LIVE evidence (a validator that is actually available
/// right now), so the signal can only understate, never overstate, what we can verify.
/// </summary>
public enum ValidateCapabilityLevel
{
    /// <summary>
    /// The artifact type is named by the registry but no validator is currently available for
    /// it (neither a schema oracle nor a conformance oracle loaded). We can recognize the type
    /// but cannot attest anything about an instance's correctness.
    /// </summary>
    Unrecognized = 0,

    /// <summary>
    /// A structural/grammar oracle is available (e.g. the base CDA R2 XSD schema set), so we
    /// can validate well-formedness and schema grammar — but no template-level conformance
    /// oracle attests the IG constraints (required elements, cardinality, value-set binding).
    /// </summary>
    Structural = 1,

    /// <summary>
    /// Both a structural oracle AND a template-conformance oracle (e.g. the C-CDA Schematron)
    /// are available, so we can validate grammar AND IG-level template conformance. The
    /// strongest, most defensible validate claim.
    /// </summary>
    Conformance = 2
}
