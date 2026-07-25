// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Pidgeon.Core.Architecture.Tests;

/// <summary>
/// Guard: every <c>IFHIRResourceBuilder</c> implementation must live
/// under <c>Pidgeon.Core.Infrastructure.Standards.FHIR.*</c>. Keeps the
/// standards-specific builder pattern out of the Application layer.
/// </summary>
[Trait("Category", "CE-S3a")]
public class FHIRBuilderLocationTests
{
    [Fact]
    public void All_FHIR_Resource_Builders_Live_In_Infrastructure_FHIR()
    {
        var result = Types.InAssembly(Assemblies.Core)
            .That()
            .ImplementInterface(typeof(Pidgeon.Core.Infrastructure.Standards.FHIR.R4.Builders.IFHIRResourceBuilder))
            .And()
            .AreNotAbstract()
            .Should()
            .ResideInNamespaceMatching(@"Pidgeon\.Core\.Infrastructure\.Standards\.FHIR\..*")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "FHIR resource builders must be standard-specific Infrastructure types. Offenders: "
            + (result.FailingTypeNames is { Count: > 0 }
                ? string.Join(", ", result.FailingTypeNames)
                : "(none reported)"));
    }
}
