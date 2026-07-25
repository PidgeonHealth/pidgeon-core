// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Pidgeon.Core.Application.Interfaces.Capability;
using Pidgeon.Core.Infrastructure.Standards.NCPDP.Validation;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP.Capability;

/// <summary>
/// The NCPDP version axis for the capability matrix. Reports the SCRIPT version(s) the engine
/// targets so the capability sweep can build a conformance target the NCPDP SCRIPT XSD oracle
/// recognizes. The "ncpdp" / SCRIPT-version literals belong here, in NCPDP infrastructure, not in
/// the standard-agnostic capability service (architecture red line #5).
/// </summary>
internal sealed class NcpdpVersionSource : IStandardVersionSource
{
    public string StandardName => "ncpdp";

    // SCRIPT versions the embedded NCPDP XSD oracle validates against. Sourced from the schema
    // authority (NcpdpScriptVersions) so the capability axis, the conformance oracle, and the data
    // generator share one definition — a new release added to the enum propagates to all three.
    public IReadOnlyList<string> GetSupportedVersions() => NcpdpScriptVersions.Supported;
}
