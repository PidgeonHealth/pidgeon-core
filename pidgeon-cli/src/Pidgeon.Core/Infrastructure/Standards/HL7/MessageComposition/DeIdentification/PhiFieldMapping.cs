// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.DeIdentification;

/// <summary>
/// Mapping information for PHI fields in HL7 messages.
/// </summary>
public record PhiFieldMapping
{
    public required int HipaaCategory { get; init; }
    public required IdentifierType IdentifierType { get; init; }
    public required bool RequiresRemoval { get; init; }
    public required string Description { get; init; }
    public string? DetectionPattern { get; init; }
}
