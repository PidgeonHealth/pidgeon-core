// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// The version-specific providers and trigger-event definition selected for a message,
/// returned by <see cref="IHL7VersionResolver"/>. <see cref="Version"/> may differ from the
/// requested version when auto-fallback selected a higher one.
/// </summary>
public sealed record HL7VersionResolution(
    string Version,
    TriggerEvent TriggerEvent,
    IHL7SegmentProvider SegmentProvider,
    IHL7DataTypeProvider DataTypeProvider,
    IHL7TableProvider TableProvider);
