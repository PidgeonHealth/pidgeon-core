// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Validation;

/// <summary>
/// One ElementDefinition constraint (invariant): key, severity ("error" | "warning"),
/// human-readable description, and the FHIRPath expression. Keys are NOT unique across
/// profiles — US Core 6.1.0 reuses "us-core-6" for two different rules — so behavior is
/// always scoped by (profile, element), never by key alone. Xpath-only constraints
/// (no expression — rare legacy R4 entries) are dropped at parse time; the evaluator
/// runs FHIRPath expressions only, as the official validator does.
/// </summary>
public sealed record FHIRConstraint(string Key, string Severity, string Human, string Expression);
