// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP.Validation;

/// <summary>
/// Supplies the raw NCPDP SCRIPT XSD streams that <see cref="NcpdpSchemaProvider"/>
/// compiles into a schema set. Decouples the schema-compilation logic from where the
/// XSDs physically live: embedded in Pidgeon.Data for dev/test/CI, or a member-supplied
/// <c>ncpdp-script</c> data package in customer builds where the embed is stripped.
/// </summary>
internal interface INcpdpXsdSource
{
    /// <summary>
    /// Opens the named XSD (e.g. <c>transport.xsd</c>) for a SCRIPT version key (e.g.
    /// <c>2017071</c>), or returns <c>null</c> when this source cannot supply it. The
    /// caller owns and disposes the returned stream.
    /// </summary>
    Stream? Open(string versionKey, string fileName);
}
