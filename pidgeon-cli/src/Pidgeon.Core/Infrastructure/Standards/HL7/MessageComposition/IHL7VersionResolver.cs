// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Common;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Resolves which HL7 version's providers a message is generated against, applying the
/// trigger-event fallback policy. Separates version routing from message orchestration.
/// Pure version selection over the embedded reference data: holds no generation state and
/// draws no randomness.
/// </summary>
public interface IHL7VersionResolver
{
    /// <summary>
    /// Resolves the version-specific providers and trigger-event definition for
    /// <paramref name="triggerEventCode"/>. When the requested <c>options.Hl7Version</c> lacks
    /// the event: an explicit version yields a failure naming the versions that do have it; a
    /// non-explicit version auto-falls-forward to the lowest higher version that has it. Fails
    /// when no supported version has the event.
    /// </summary>
    Task<Result<HL7VersionResolution>> ResolveAsync(string messageType, string triggerEventCode, GenerationOptions options);
}
