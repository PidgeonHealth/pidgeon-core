// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Conformance;

namespace Pidgeon.Core.Application.Interfaces.Conformance;

/// <summary>
/// An independent validation oracle for a single standard. Implementations wrap a validator
/// this codebase does not author (e.g. the .NET BCL XSD validator running the official NCPDP
/// SCRIPT schemas), keeping standard-specific logic out of core services.
/// </summary>
public interface IIndependentOracle
{
    /// <summary>True if this oracle can validate content for the given target.</summary>
    bool CanValidate(ConformanceTarget target);

    /// <summary>
    /// Validates <paramref name="content"/> for <paramref name="target"/>. Only called when
    /// <see cref="CanValidate"/> returned true.
    /// </summary>
    VersionedConformanceResult Validate(string content, ConformanceTarget target);
}
