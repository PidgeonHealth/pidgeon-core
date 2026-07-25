// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Generation;

/// <summary>
/// Guards against hostile or careless run artifacts (SHAREABLE_RUN_STANDARD.md §7.2). A shared artifact
/// can carry an arbitrarily large <c>count</c>; replaying or verifying one above the confirmation
/// threshold requires an explicit opt-in so a single pasted file cannot conscript a machine into
/// generating a hundred million messages by accident.
/// </summary>
public static class RunGuards
{
    /// <summary>Runs larger than this require an explicit confirmation (<c>--yes</c>) before replay/verify.</summary>
    public const int CountConfirmationThreshold = 10_000;

    /// <summary>True when a run's message count is large enough to require explicit confirmation.</summary>
    public static bool RequiresConfirmation(int count) => count > CountConfirmationThreshold;
}
