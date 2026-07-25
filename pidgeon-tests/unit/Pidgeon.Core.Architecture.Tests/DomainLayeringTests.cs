// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Linq;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Pidgeon.Core.Architecture.Tests;

/// <summary>
/// Pidgeon.Core is organised into Domain, Application, and Infrastructure
/// layers. Standards-specific knowledge (HL7, FHIR, NCPDP) lives in the
/// Infrastructure layer. These tests prevent Application-layer services
/// from coupling to Infrastructure except through two sanctioned seams:
///   * Application.Interfaces.Standards.* — interfaces for standard-specific
///     concerns are by definition standard-aware.
///   * Application.Services.**.Plugins.**  — plugins are explicitly
///     standard-aware by contract.
///
/// Every other leak is documented in the known-leak whitelist below.
/// The burndown (LEDGER ARCH-090) reduced these to a single documented
/// permanent exception (HL7Message, a sanctioned interface seam): the
/// datatype DTO leaks were decoupled onto Domain value types and the
/// MessageValidationService entry was retired as inert.
/// </summary>
[Trait("Category", "CE-S1")]
public class DomainLayeringTests
{
    /// <summary>
    /// Classes in Pidgeon.Core.Application that are allowed to depend on
    /// Pidgeon.Core.Infrastructure.Standards.HL7.*, each pending extraction
    /// of the dependency.
    /// </summary>
    // test-guard: allow whitelist shrink 1 -> 0 (ARCH-090): MessageValidationService
    // dispatches only through IStandardValidationPlugin (Application.Interfaces.Standards)
    // with plugins injected via DI, so it carries no compiled dependency on
    // Infrastructure.Standards.HL7; its extraction (via IStandardValidator) already landed.
    // Earlier (shrink 3 -> 1): DeIdentificationService and PhiDetector were extracted
    // behind IStandardDeIdentificationPlugin. Emptying this list makes the test STRICTER,
    // not weaker.
    private static readonly string[] KnownHl7LeakWhitelist =
    {
    };

    /// <summary>
    /// Domain types allowed to depend on Application layer types. After the
    /// ARCH-090 burndown this is a single permanent exception: HL7Message,
    /// whose only Application coupling is the sanctioned IStandardMessage
    /// interface seam (not a DTO leak). New entries should fail the test.
    /// </summary>
    // test-guard: allow whitelist shrink 5 -> 1 (ARCH-090): AddressField and
    // PersonNameField were decoupled from Application.DTOs onto the Domain
    // Address/PersonName value types (the leak is gone, the datatype classes stay);
    // XAD_ExtendedAddress/XPN_ExtendedPersonName were phantom entries (no types by
    // those names ever existed). Removing all four makes the test STRICTER, not weaker.
    private static readonly string[] KnownDomainToApplicationLeakWhitelist =
    {
        // HL7Message implements the Application.Interfaces.Standards.IStandardMessage
        // contract; that coupling is by design (the standard-message seam), not a DTO leak.
        "HL7Message",
    };

    [Fact]
    public void Application_Services_MustNotDependOn_Infrastructure_Standards_HL7_OutsideWhitelist()
    {
        var result = Types.InAssembly(Assemblies.Core)
            .That()
            .ResideInNamespace("Pidgeon.Core.Application.Services")
            .And()
            .DoNotResideInNamespaceMatching(@"Pidgeon\.Core\.Application\.Services\..*\.Plugins(\..*)?$")
            .ShouldNot()
            .HaveDependencyOn("Pidgeon.Core.Infrastructure.Standards.HL7")
            .GetResult();

        if (result.IsSuccessful)
        {
            return;
        }

        var offenders = result.FailingTypeNames ?? Enumerable.Empty<string>();
        var unexpected = offenders
            .Select(TypeName)
            .Where(name => !KnownHl7LeakWhitelist.Contains(name))
            .ToArray();

        unexpected.Should().BeEmpty(
            "Application services must not depend on Infrastructure.Standards.HL7 "
            + "except via sanctioned seams (Interfaces.Standards.*, Plugins) or the "
            + "known-leak whitelist. Unexpected offenders: "
            + string.Join(", ", unexpected));
    }

    /// <summary>
    /// Application-layer classes allowed to depend on Pidgeon.Core.Infrastructure.Data.
    /// Each entry is a resolver-seamed data loader whose manifest-excluded
    /// Legacy*Compatibility partial ctor forwards LegacyPidgeonDataResourceResolver.Instance
    /// (the sanctioned private-monorepo bridge); the partial compiles the Infrastructure.Data
    /// reference into the Application type even though the community manifest never admits it.
    /// New entries require the same seam justification - anything else is a layering leak.
    /// </summary>
    private static readonly string[] KnownInfrastructureDataLeakWhitelist =
    {
        "DemographicsDataService",
        "NarrativeNoteCorpusLoader",
        "LabCorrelationProvider",
        "ReferenceIntervalProvider",
        // The NCPDP attestation gate's manifest-excluded legacy overload adapts the private
        // package manager through Infrastructure.Data.LegacyDataPackageManagerCatalog (G3);
        // the admitted half resolves against the read-only catalog contract only.
        "NcpdpAccessGate",
    };

    [Fact]
    public void Application_Services_MustNotDependOn_Infrastructure_Data_OutsideWhitelist()
    {
        var result = Types.InAssembly(Assemblies.Core)
            .That()
            .ResideInNamespace("Pidgeon.Core.Application.Services")
            .And()
            .DoNotResideInNamespaceMatching(@"Pidgeon\.Core\.Application\.Services\..*\.Plugins(\..*)?$")
            .ShouldNot()
            .HaveDependencyOn("Pidgeon.Core.Infrastructure.Data")
            .GetResult();

        if (result.IsSuccessful)
        {
            return;
        }

        var offenders = result.FailingTypeNames ?? Enumerable.Empty<string>();
        var unexpected = offenders
            .Select(TypeName)
            .Where(name => !KnownInfrastructureDataLeakWhitelist.Contains(name))
            .ToArray();

        unexpected.Should().BeEmpty(
            "Application services must not depend on Infrastructure.Data except via the "
            + "resolver-seam Legacy*Compatibility bridge documented in the whitelist. "
            + "Unexpected offenders: "
            + string.Join(", ", unexpected));
    }

    [Fact]
    public void Domain_MustNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(Assemblies.Core)
            .That()
            .ResideInNamespace("Pidgeon.Core.Domain")
            .ShouldNot()
            .HaveDependencyOn("Pidgeon.Core.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            FormatFailure("Domain layer depended on Infrastructure", result));
    }

    [Fact]
    public void Domain_MustNotDependOn_Application_OutsideWhitelist()
    {
        var result = Types.InAssembly(Assemblies.Core)
            .That()
            .ResideInNamespace("Pidgeon.Core.Domain")
            .ShouldNot()
            .HaveDependencyOn("Pidgeon.Core.Application")
            .GetResult();

        if (result.IsSuccessful)
        {
            return;
        }

        var offenders = result.FailingTypeNames ?? Enumerable.Empty<string>();
        var unexpected = offenders
            .Select(TypeName)
            .Where(name => !KnownDomainToApplicationLeakWhitelist.Contains(name))
            .ToArray();

        unexpected.Should().BeEmpty(
            "Domain types must not depend on Application types. The current "
            + "offenders are whitelisted pending the Domain→Application extraction "
            + "sprint; new offenders are regressions. Unexpected: "
            + string.Join(", ", unexpected));
    }

    private static string TypeName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot < 0 ? fullName : fullName[(lastDot + 1)..];
    }

    private static string FormatFailure(string summary, NetArchTest.Rules.TestResult result)
    {
        var offenders = result.FailingTypeNames is { Count: > 0 }
            ? string.Join(", ", result.FailingTypeNames)
            : "(no types reported)";
        return $"{summary}. Offending types: {offenders}";
    }
}
