// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.DTOs.Data;

/// <summary>
/// Everything an <see cref="Pidgeon.Core.Application.Interfaces.Data.IFhirPackageFetcher"/>
/// needs to download a FHIR IG package from the HL7 registry and write it to disk:
/// the registry coordinates plus the manifest metadata to stamp on the extracted package.
/// Built from a <c>DataPackageRegistry.Entry</c> by the package manager.
/// </summary>
public record FhirPackageFetchRequest(
    string PackageName,
    string DownloadUrl,
    string DataType,
    string Version,
    string Description,
    string Source,
    string License);
