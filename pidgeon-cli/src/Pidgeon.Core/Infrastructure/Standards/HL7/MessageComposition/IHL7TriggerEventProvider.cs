// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Provides access to HL7 v2.3 trigger event definitions from embedded JSON resources.
/// </summary>
public interface IHL7TriggerEventProvider
{
    /// <summary>
    /// Gets a trigger event definition by code (e.g., "ADT_A01", "ORM_O01").
    /// </summary>
    /// <param name="triggerEventCode">Trigger event code</param>
    /// <returns>Trigger event definition or error</returns>
    Task<Result<TriggerEvent>> GetTriggerEventAsync(string triggerEventCode);

    /// <summary>
    /// Gets all available trigger event codes.
    /// </summary>
    /// <returns>List of trigger event codes</returns>
    Task<Result<IReadOnlyList<string>>> GetAvailableTriggerEventsAsync();
}

/// <summary>
/// Represents an HL7 trigger event definition with segments and their rules.
/// </summary>
/// <remarks>
/// <see cref="MessageStructure"/> is the message STRUCTURE ID from HL7's published
/// event→structure table (e.g. trigger ADT_A04 uses the ADT_A01 structure). It names both
/// the wire MSH-9.3 value and the v2.xml root element / XSD file. Optional in the JSON
/// (`message_structure`); when absent, consumers fall back to the trigger code (correct for
/// the majority of triggers whose structure is their own).
/// </remarks>
public record TriggerEvent(
    string Code,
    string Name,
    string Version,
    string Chapter,
    string Description,
    IReadOnlyList<TriggerEventSegment> Segments,
    int SegmentCount,
    string? MessageStructure = null);

/// <summary>
/// Represents a segment definition within a trigger event.
/// </summary>
/// <remarks>
/// <see cref="InclusionProbability"/> and <see cref="RepeatMin"/>/<see cref="RepeatMax"/> are
/// the oracle-side statistical knobs carried in the trigger-event JSON. They are optional in
/// the JSON (`inclusion_probability`, `repeat_range`); when absent, generation falls back to
/// the code-side defaults in <see cref="HL7SegmentStatistics"/>.
/// </remarks>
public record TriggerEventSegment(
    string SegmentCode,
    string SegmentDesc,
    string Optionality,
    string Repeatability,
    bool IsGroup,
    IReadOnlyList<string> GroupPath,
    int Level,
    int OrderIndex,
    double? InclusionProbability = null,
    int? RepeatMin = null,
    int? RepeatMax = null);
