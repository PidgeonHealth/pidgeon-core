// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Pidgeon.Core.Architecture.Tests;

/// <summary>
/// RL5 guard for the engine-vendor-dialect seam: the HL7 vendor-dialect composition logic
/// (<c>VendorDialectPass</c>, <c>ZSegmentComposer</c>) must be DATA-DRIVEN — it shapes a message purely
/// from the <c>VendorInterfaceProfile</c> data, never from a per-vendor code branch. If a future change
/// adds <c>if (vendor == "Epic")</c> or switches on the <c>VendorProfile</c> enum, the "looks like your
/// vendor" behaviour stops being a profile a contributor can add and becomes core surgery — the exact
/// drift RL5 forbids.
///
/// Two checks, both reflection-feasible (no IL walking, mirroring <see cref="HL7LeakGuardTests"/>'s
/// scope decision):
///   1. No vendor-name string literal in a const / static-readonly field.
///   2. No dependency on the <c>VendorProfile</c> enum in any field/property/parameter — branching on a
///      vendor requires naming the enum, so its absence from the surface is a strong proxy.
/// The enum→key map (<c>VendorProfileKeyMap</c>) legitimately names vendors and lives in a different
/// namespace; it is deliberately NOT in scope here — the data table is the one allowed home for the names.
///
/// Scope limit (be honest about it): reflection sees declared fields/properties/parameters, not method
/// bodies, so a literal buried inside a method (<c>if (vendor == "Epic")</c>) or a local <c>switch</c> on
/// the enum would escape both checks — catching those needs IL analysis, the same gap accepted in
/// <see cref="HL7LeakGuardTests"/>. The field + parameter surface is the practical prevention gate (a
/// real per-vendor branch almost always surfaces a const table or a typed parameter); the human review
/// gate (arch-review) backstops the method-body case.
/// </summary>
public class VendorDialectLeakGuardTests
{
    private const string DialectNamespace = "Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition";

    private static readonly string[] ForbiddenVendorNames =
    {
        "Epic", "Cerner", "Oracle", "Millennium", "Meditech", "MEDITECH",
        "Allscripts", "AllScripts", "ALLSCRIPTS", "Athenahealth", "Athena", "ATHENA",
        "NextGen", "EClinicalWorks", "eClinicalWorks", "ECW", "Greenway", "DrChrono",
    };

    private static List<Type> DialectTypes() =>
        Assemblies.Core.GetTypes()
            .Where(t => t.Namespace != null
                        && t.Namespace.StartsWith(DialectNamespace, StringComparison.Ordinal)
                        && (t.Name.Contains("VendorDialect", StringComparison.Ordinal)
                            || t.Name.Contains("ZSegment", StringComparison.Ordinal)))
            .ToList();

    [Fact]
    public void DialectComposition_MustNotContain_VendorNameLiterals()
    {
        var dialectTypes = DialectTypes();
        dialectTypes.Should().NotBeEmpty(
            "the vendor-dialect composition types must exist — if renamed, update this guard");

        var offenders = new List<string>();
        foreach (var type in dialectTypes)
        {
            foreach (var field in type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType != typeof(string))
                    continue;

                var isConst = field.IsLiteral && !field.IsInitOnly;
                var isStaticReadonly = field.IsStatic && field.IsInitOnly;
                if (!isConst && !isStaticReadonly)
                    continue;

                string? value;
                try { value = field.GetValue(null) as string; }
                catch { continue; }

                if (string.IsNullOrEmpty(value))
                    continue;

                foreach (var vendor in ForbiddenVendorNames)
                {
                    if (value.Contains(vendor, StringComparison.Ordinal))
                        offenders.Add($"{type.FullName}.{field.Name} = \"{value}\"");
                }
            }
        }

        offenders.Should().BeEmpty(
            "vendor-dialect composition must be driven by VendorInterfaceProfile data, not vendor-name "
            + "literals. Move the vendor knowledge into the profile YAML; the C# pass stays generic.");
    }

    [Fact]
    public void DialectComposition_MustNotDependOn_TheVendorProfileEnum()
    {
        var enumType = typeof(Pidgeon.Core.Generation.Types.VendorProfile);
        var dialectTypes = DialectTypes();
        dialectTypes.Should().NotBeEmpty();

        var offenders = new List<string>();
        foreach (var type in dialectTypes)
        {
            foreach (var field in type.GetFields(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == enumType)
                    offenders.Add($"{type.FullName}.{field.Name} (field)");
            }

            foreach (var prop in type.GetProperties(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (prop.PropertyType == enumType)
                    offenders.Add($"{type.FullName}.{prop.Name} (property)");
            }

            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic |
                         BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.GetParameters().Any(p => p.ParameterType == enumType))
                    offenders.Add($"{type.FullName}.{method.Name} (parameter)");
            }
        }

        offenders.Should().BeEmpty(
            "the vendor-dialect pass shapes output from VendorInterfaceProfile data, so it must never "
            + "name the VendorProfile enum (the thing a per-vendor switch would branch on). Resolve the "
            + "enum to a profile upstream (VendorProfileResolver) and pass the typed profile down.");
    }
}
