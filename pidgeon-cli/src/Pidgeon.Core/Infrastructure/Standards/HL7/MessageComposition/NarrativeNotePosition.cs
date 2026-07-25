// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// The clinical fact a generated NTE (Notes and Comments) restates, inferred from the NTE's
/// enclosing group hierarchy. A message's NTE slots sit at structurally distinct positions and each
/// wraps a different coded fact the message already carries, so the note stays coherent by
/// construction (it only NAMES a fact, never asserts a clinical disposition of its own).
/// </summary>
public enum NarrativeNoteKind
{
    /// <summary>No supported narrative axis for this NTE position — suppress rather than fabricate.</summary>
    None,

    /// <summary>Post-OBX observation NTE: restates the lab result the OBX carries (the b71 lab note),
    /// or, in the ORU ORDER OBSERVATION group, an order↔result or all-normal panel summary.</summary>
    Observation,

    /// <summary>Post-OBR order NTE (before the OBX group): restates the ordered test (OBR-4).</summary>
    OrderStatus,

    /// <summary>Post-RXE/RXO pharmacy-order NTE: restates the ordered medication.</summary>
    Medication,
}

/// <summary>
/// Maps an NTE's group path to the narrative axis it can coherently restate. Shared by the odds
/// (<see cref="HL7SegmentStatistics"/>), the inclusion precondition (<see cref="SegmentInclusionPolicy"/>),
/// and the content dispatcher (<c>NteValueContributor</c>) so the three never drift on which position
/// carries which note. Group names are matched case-insensitively with underscores normalised to
/// spaces, so a version that spells a group "ORDER_OBSERVATION" (v2.7) resolves the same as one that
/// spells it "ORDER OBSERVATION" (v2.5.1).
/// </summary>
public static class NarrativeNotePosition
{
    /// <summary>
    /// Classifies the narrative axis for an NTE with the given <paramref name="groupPath"/>. Returns
    /// <see cref="NarrativeNoteKind.None"/> for patient-level, top-level, and every other NTE position
    /// with no coded fact in scope — those stay off unless a caller pins them.
    /// </summary>
    public static NarrativeNoteKind Classify(IReadOnlyList<string>? groupPath)
    {
        if (groupPath is null || groupPath.Count == 0)
            return NarrativeNoteKind.None;

        // The observation-level NTE (post-OBX) is uniquely the one whose path carries a leaf-level
        // "OBSERVATION" group element — the same element that also encloses the OBX. Checked first so
        // an ORU path that carries BOTH "ORDER OBSERVATION" and "OBSERVATION" resolves to the
        // observation slot, not the order slot.
        if (ContainsGroup(groupPath, "OBSERVATION"))
            return NarrativeNoteKind.Observation;

        // The order-level NTE (post-OBR, before the OBX group): the ordered test is the coded fact
        // to restate. ORU nests it under "ORDER OBSERVATION"; MDM and other document orders use the
        // "COMMON ORDER" group.
        if (ContainsGroup(groupPath, "ORDER OBSERVATION") || ContainsGroup(groupPath, "COMMON ORDER"))
            return NarrativeNoteKind.OrderStatus;

        // The pharmacy-order NTE (post-RXE/RXO): the ordered medication is the coded fact. The leaf
        // "ORDER" (RDE, after RXE) or "ORDER DETAIL" (ORM/RDE, after RXO) marks it.
        var leaf = Normalize(groupPath[groupPath.Count - 1]);
        if (leaf is "ORDER" or "ORDER DETAIL")
            return NarrativeNoteKind.Medication;

        return NarrativeNoteKind.None;
    }

    /// <summary>
    /// Whether an observation NTE sits inside an ORU-style ORDER OBSERVATION result group (its path
    /// carries both the enclosing "ORDER OBSERVATION" group and the leaf "OBSERVATION"). This is the
    /// one position where the order's ordered test and its results are both in scope, so the Tier-2
    /// order↔result and all-normal panel notes fire here and nowhere else; every other observation NTE
    /// (an ORM/RDE ORDER DETAIL result, an ADT admission lab) keeps the plain lab restatement.
    /// </summary>
    public static bool InOrderObservationResultGroup(IReadOnlyList<string>? groupPath)
        => groupPath is not null
            && ContainsGroup(groupPath, "OBSERVATION")
            && ContainsGroup(groupPath, "ORDER OBSERVATION");

    private static bool ContainsGroup(IReadOnlyList<string> groupPath, string group)
    {
        for (var i = 0; i < groupPath.Count; i++)
            if (Normalize(groupPath[i]) == group)
                return true;
        return false;
    }

    private static string Normalize(string groupName)
        => (groupName ?? string.Empty).Replace('_', ' ').Trim().ToUpperInvariant();
}
