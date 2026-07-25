// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Generic;
using Pidgeon.Core.Application.Interfaces.Capability;
using Pidgeon.Core.Application.Interfaces.Standards.HL7;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.Capability;

/// <summary>
/// The HL7 version axis for the capability matrix, backed by the embedded-schema versions the
/// engine actually carries (<see cref="IHL7DataProviderFactory.GetSupportedVersions"/>). The
/// "hl7" standard literal belongs here, in HL7 infrastructure, not in the standard-agnostic
/// capability service (architecture red line #5).
/// </summary>
internal sealed class Hl7VersionSource : IStandardVersionSource
{
    private readonly IHL7DataProviderFactory _factory;

    public Hl7VersionSource(IHL7DataProviderFactory factory)
    {
        _factory = factory;
    }

    public string StandardName => "hl7";

    public IReadOnlyList<string> GetSupportedVersions() => _factory.GetSupportedVersions();
}
