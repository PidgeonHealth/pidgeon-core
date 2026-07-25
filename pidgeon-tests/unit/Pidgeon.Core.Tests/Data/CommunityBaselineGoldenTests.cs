// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Extensions;
using Pidgeon.Core.Generation;
using Pidgeon.Data.Baseline;
using Xunit;

namespace Pidgeon.Core.Tests.Data;

/// <summary>
/// The flagship payload gate: the COMMUNITY composition over the REAL Baseline
/// payload generates a seeded ADT^A01 that is byte-identical across independent
/// compositions and structurally complete (real demographics from the shipped
/// tables, not the hollow "Unknown" fallback). If community bytes ever diverge
/// run-to-run or hollow out, the payload or the seam regressed.
/// </summary>
public sealed class CommunityBaselineGoldenTests
{
    private const int Seed = 70021;

    [Theory(DisplayName = "Seeded community generation over the shipped Baseline payload is byte-identical across independent compositions - a regression means the payload broke coordinate-addressed determinism")]
    [InlineData("2.3")]
    [InlineData("2.5.1")]
    [InlineData("2.7")]
    public async Task SeededCommunityGeneration_OverBaseline_IsByteIdentical(string version)
    {
        var first = await GenerateOnceAsync(version);
        var second = await GenerateOnceAsync(version);

        first.Should().Be(second);
    }

    [Fact(DisplayName = "With the Baseline payload installed, community output carries real demographics - hollow placeholders here mean the demographics tables stopped reaching the generators")]
    public async Task BaselinePayload_UnhollowsCommunityOutput()
    {
        var message = await GenerateOnceAsync("2.3");

        message.Should().StartWith("MSH|");
        message.Should().Contain("PID|");
        message.Should().NotContain("Unknown^Unknown",
            "the shipped demographics tables must feed the name generators");

        var segments = message.Split('\n');
        var msh9 = segments.Single(segment => segment.StartsWith("MSH|")).Split('|')[8];
        msh9.Should().Be("ADT^A01",
            "HL7 v2.3 CM_MSG has only message-code and trigger-event components");

        var pid5 = segments.Single(segment => segment.StartsWith("PID|")).Split('|')[5];
        pid5.Should().MatchRegex(@"^[^^|]+\^[^^|]+",
            "the public composition must render the generated patient's family and given names");
    }

    private static async Task<string> GenerateOnceAsync(string version)
    {
        // The real seam end-to-end: Baseline provider -> composite resolver ->
        // community generation graph. Registration order per the documented
        // snapshot rule: providers before the first resolver resolution.
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddPidgeonBaselineData();
        services.AddPidgeonHl7Generation();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var generation = scope.ServiceProvider.GetRequiredService<IMessageGenerationService>();
        var result = await generation.GenerateSyntheticDataAsync(
            "hl7", "ADT^A01", 1, new GenerationOptions
            {
                Seed = Seed,
                Hl7Version = version,
                Hl7VersionExplicit = true,
                AsOf = DeterminismTestClock.Fixed,
            });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.Should().HaveCount(1);
        return Normalize(result.Value[0]);
    }

    // Mirrors Hl7GenerationSnapshotTests.Normalize so any future golden comparison
    // normalizes identically.
    private static string Normalize(string message) =>
        message.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n');
}
