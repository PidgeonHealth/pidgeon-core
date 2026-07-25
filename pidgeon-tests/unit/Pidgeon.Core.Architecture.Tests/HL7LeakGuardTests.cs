// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Pidgeon.Core.Architecture.Tests;

/// <summary>
/// Guard against regressions of the architectural rule "no HL7 segment
/// literals in Pidgeon.Core.Application.Services.Validation.*".
///
/// The HL7 logic lives in <c>HL7ValidationPlugin</c> under
/// Infrastructure; this guard is active, so any future regression
/// lights up here instead of silently drifting back in.
///
/// The scan reads const and static-readonly string fields on types in
/// the guarded namespace. Method-body literals are out of scope —
/// catching them would require IL walking, which is heavier than the
/// return justifies at this stage.
/// </summary>
[Trait("Category", "CE-S1")]
public class HL7LeakGuardTests
{
    private static readonly string[] ForbiddenSegmentNames =
    {
        "MSH", "PID", "OBX", "DG1", "RXE", "EVN", "PV1",
    };

    [Fact]
    public void Core_Services_Validation_MustNotContain_HL7_Segment_Literals()
    {
        var guardedTypes = Assemblies.Core.GetTypes()
            .Where(t => t.Namespace != null
                        && t.Namespace.StartsWith("Pidgeon.Core.Application.Services.Validation",
                                                  StringComparison.Ordinal))
            .ToList();

        guardedTypes.Should().NotBeEmpty(
            "the guard namespace must actually exist — if it has been renamed, update this test");

        var offenders = new List<string>();
        foreach (var type in guardedTypes)
        {
            foreach (var field in type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType != typeof(string))
                    continue;

                // Only inspect const and static-readonly fields — these are the
                // typical carriers of HL7 segment names as named constants.
                var isConst = field.IsLiteral && !field.IsInitOnly;
                var isStaticReadonly = field.IsStatic && field.IsInitOnly;
                if (!isConst && !isStaticReadonly)
                    continue;

                string? value;
                try
                {
                    value = field.GetValue(null) as string;
                }
                catch
                {
                    // Uninitialised static-readonly — skip rather than fail the guard.
                    continue;
                }

                if (string.IsNullOrEmpty(value))
                    continue;

                foreach (var forbidden in ForbiddenSegmentNames)
                {
                    if (string.Equals(value, forbidden, StringComparison.Ordinal))
                    {
                        offenders.Add($"{type.FullName}.{field.Name} = \"{value}\"");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            "HL7 segment literals must live in Infrastructure.Standards.HL7.*, not in "
            + "the standards-agnostic Application.Services.Validation layer. Move each "
            + "offender into the HL7 plugin.");
    }
}
