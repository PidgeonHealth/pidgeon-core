// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.DTOs.Data;

namespace Pidgeon.Core.Application.Interfaces.Data;

/// <summary>
/// Fetches a CC0 FHIR IG package (US Core, Da Vinci PAS / CRD / DTR) from the
/// HL7 registry and writes it to a local package directory in the same on-disk
/// shape the Python extraction scripts produce: a flat set of
/// StructureDefinition / ValueSet / CodeSystem JSON files plus a manifest.json.
///
/// <para>
/// This is the C#-side equivalent of <c>extract_fhir_davinci_pas.py</c> and its
/// siblings. It exists so <c>pidgeon data install &lt;fhir-ig&gt;</c> works on a
/// machine that never ran the Python ETL: the package manager calls this when a
/// known IG has no local source extract. Only packages with a registry
/// DownloadUrl (the public CC0 IGs) are ever fetched this way; licensed
/// terminology packages are never downloaded.
/// </para>
/// </summary>
public interface IFhirPackageFetcher
{
    /// <summary>
    /// Downloads <see cref="FhirPackageFetchRequest.DownloadUrl"/>, extracts the
    /// validation-relevant resources, and writes them plus a manifest.json into
    /// <paramref name="targetDir"/>. Returns the count of resources written, or a
    /// failure if the download or extraction fails (network, non-2xx, malformed
    /// tarball, or an archive with no usable conformance resources).
    /// </summary>
    Task<Result<int>> FetchAsync(
        FhirPackageFetchRequest request,
        string targetDir,
        CancellationToken cancellationToken = default);
}
