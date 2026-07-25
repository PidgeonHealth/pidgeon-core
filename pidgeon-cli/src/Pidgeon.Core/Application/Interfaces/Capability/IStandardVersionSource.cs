// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;

namespace Pidgeon.Core.Application.Interfaces.Capability;

/// <summary>
/// The version axis of the capability matrix for one standard. Implementations live in the
/// Infrastructure layer (where standard/version literals belong, per architecture red line #5)
/// and report which versions of their standard the engine carries. The capability service
/// matches a generation plugin's <c>StandardName</c> against <see cref="StandardName"/> to
/// expand a standard into its (type × version) cells, so the service itself stays free of any
/// standard or version literal.
///
/// A standard with no registered version source is treated as single-version (cells carry a
/// null version), e.g. FHIR R4.
/// </summary>
public interface IStandardVersionSource
{
    /// <summary>Canonical standard id this source describes (e.g. "hl7", "ncpdp").</summary>
    string StandardName { get; }

    /// <summary>The versions of this standard the engine carries (e.g. "2.3".."2.8").</summary>
    IReadOnlyList<string> GetSupportedVersions();
}
