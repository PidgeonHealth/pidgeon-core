// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;

namespace Pidgeon.Core.Application.Services.Clinical.Realism;

/// <summary>
/// The ONE abnormal-flag rule (RI-2). The flag is a pure function of (value, reference interval):
/// membership in the interval, never a midpoint of a sampling band, never a stored input. This is
/// the membership rule both value paths now share — the message-path midpoint rule and the two
/// Flock duplicates are deleted and re-pointed here.
///
/// static is allowed here: a pure function with no state (ARCHITECTURE.md red line #1 exempts
/// pure functions / constants).
/// </summary>
public static class AbnormalFlagRule
{
    /// <summary>The HL7 abnormal flag for <paramref name="value"/> against <paramref name="interval"/>.</summary>
    public static AbnormalFlag Evaluate(double value, ReferenceInterval interval)
        => value < interval.Low ? AbnormalFlag.L
         : value > interval.High ? AbnormalFlag.H
         : AbnormalFlag.N;

    /// <summary>
    /// The flag judged at emission precision (b83). OBX-5 and OBX-7 go to the wire rendered at F1,
    /// so membership is evaluated on the rendered numbers, never the raw doubles — otherwise a raw
    /// 1.32 against a raw high of 1.30 emits H while the wire reads "1.3 vs 0.6-1.3", which is
    /// Normal to every downstream reader. Parse-back of the exact rendered strings makes the flag
    /// consistent with the message as written, whatever the formatter's rounding mode.
    /// </summary>
    public static AbnormalFlag EvaluateRendered(double value, ReferenceInterval interval)
        => Evaluate(
            RenderedF1(value),
            interval with { Low = RenderedF1(interval.Low), High = RenderedF1(interval.High) });

    private static double RenderedF1(double raw)
        => double.Parse(raw.ToString("F1", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>The HL7 wire token for a flag ("N" / "L" / "H").</summary>
    public static string ToHl7(AbnormalFlag flag) => flag switch
    {
        AbnormalFlag.L => "L",
        AbnormalFlag.H => "H",
        _ => "N",
    };
}
