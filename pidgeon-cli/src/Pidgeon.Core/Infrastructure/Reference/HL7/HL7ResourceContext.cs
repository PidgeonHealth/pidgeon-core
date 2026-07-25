// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Interfaces.Data;

namespace Pidgeon.Core.Infrastructure.Reference.HL7;

/// <summary>
/// Resolved lookup context for a single HL7 reference plugin instance.
/// Carries the version config, the portable data-path prefix for that version
/// (e.g. <c>standards/hl7/v23</c>), and the <see cref="IDataResourceResolver"/>
/// that serves the reference JSON. Constructed once per plugin at first use and
/// passed through to each per-concern loader on every call.
/// </summary>
/// <remarks>
/// <see cref="JsonHL7ReferencePlugin"/> dispatches to per-concern
/// loaders (segments / data types / tables / trigger events). The loaders
/// are stateless singletons; all version-specific state lives on this
/// context object so the loaders never hold per-version state of their own.
/// </remarks>
public sealed record HL7ResourceContext(
    HL7VersionConfig Config,
    string PortableBasePath,
    IDataResourceResolver Resolver);
