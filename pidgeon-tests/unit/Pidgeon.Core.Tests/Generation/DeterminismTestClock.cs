// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Tests;

/// <summary>
/// The fixed wall-clock determinism and golden tests pin via <c>GenerationOptions.AsOf</c> (or inject
/// directly) so seeded runs stay byte-stable now that the production clock is the real current time
/// after production adopted a real current-time clock. The value matches the former
/// <c>GenerationDeterminism.SeededClock</c>, so the
/// existing wire goldens remain byte-identical: the fixed instant simply moved from a production constant
/// to where a fixed test clock belongs.
/// </summary>
internal static class DeterminismTestClock
{
    public static readonly DateTime Fixed = new(2024, 1, 1, 9, 0, 0, DateTimeKind.Unspecified);
}
