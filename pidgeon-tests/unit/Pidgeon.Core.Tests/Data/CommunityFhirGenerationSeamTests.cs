// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pidgeon.Core.Application.Interfaces.Clinical;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Extensions;
using Pidgeon.Core.Generation;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Pidgeon.Core.Tests.Data;

/// <summary>
/// Proves the community-admitted FHIR generation closure operates end-to-end through the
/// public composition: resolver → shared entity seams → builders → IMessageGenerationService.
/// FHIR resource building is code-driven (no data-resolver dependency at build time), so the
/// determinism and degradation pins run against an EMPTY resolver; the positive pin proves the
/// lab-realism providers read their clinical YAML through portable paths, not the legacy assembly.
/// </summary>
public sealed class CommunityFhirGenerationSeamTests
{
    private const int Seed = 70021;

    [Theory(DisplayName = "The same seed through two independent community compositions produces byte-identical FHIR output - a regression here means community FHIR generation lost coordinate-addressed determinism")]
    [InlineData("Patient")]
    [InlineData("Observation")]
    [InlineData("Bundle")]
    public async Task SameSeed_ProducesByteIdenticalFhirOutput_ThroughTheSeam(string resourceType)
    {
        var first = await GenerateOnceAsync(resourceType);
        var second = await GenerateOnceAsync(resourceType);

        first.Should().Be(second);
    }

    [Fact(DisplayName = "With no clinical realism YAMLs installed, Observation generation still succeeds as valid JSON - a regression here means a zero-payload community install fails instead of degrading")]
    public async Task MissingRealismData_ObservationStillGenerates()
    {
        var json = await GenerateOnceAsync("Observation");

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("resourceType").GetString().Should().Be("Observation");
    }

    [Fact(DisplayName = "The lab-realism providers read their clinical YAML through the portable resolver paths - a regression here means the community composition silently reverts to the legacy embedded assembly")]
    public void RealismProviders_ReadThroughThePortableSeam()
    {
        var resolver = new SeamResolver(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["clinical/reference-intervals.yaml"] = """
                intervals:
                  - loinc: "2345-7"
                    analyte: "Glucose"
                    unit: "mg/dL"
                    low: 70
                    high: 99
                    shape: "normal"
                """,
            ["clinical/lab-correlations.yaml"] = """
                correlations:
                  - member_a: { loinc: "2345-7", analyte: "Glucose" }
                    member_b: { loinc: "4548-4", analyte: "HbA1c" }
                    spearman_rho: 0.7
                """,
        });

        using var scope = BuildProvider(resolver).CreateScope();

        var intervals = scope.ServiceProvider.GetRequiredService<IReferenceIntervalProvider>();
        var interval = intervals.Get("2345-7");
        interval.Should().NotBeNull();
        interval!.Unit.Should().Be("mg/dL");
        interval.Low.Should().Be(70);
        interval.High.Should().Be(99);

        var correlations = scope.ServiceProvider.GetRequiredService<ILabCorrelationProvider>();
        correlations.SpearmanRho("2345-7", "4548-4").Should().Be(0.7);
        correlations.SpearmanRho("4548-4", "2345-7").Should().Be(0.7);
    }

    private static async Task<string> GenerateOnceAsync(string resourceType)
    {
        // Fresh provider per call proves cross-process determinism, not cache reuse.
        using var scope = BuildProvider(new SeamResolver(
            new Dictionary<string, string>(StringComparer.Ordinal))).CreateScope();

        var generation = scope.ServiceProvider.GetRequiredService<IMessageGenerationService>();
        var result = await generation.GenerateSyntheticDataAsync(
            "fhir", resourceType, 1, new GenerationOptions { Seed = Seed, AsOf = DeterminismTestClock.Fixed });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.Should().HaveCount(1);
        return result.Value[0];
    }

    private static ServiceProvider BuildProvider(IDataResourceResolver resolver)
    {
        // Wire through the composition's explicit registration seam only - the same path a
        // community consumer takes.
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(resolver);
        services.AddPidgeonFhirGeneration();
        return services.BuildServiceProvider();
    }

    private sealed class SeamResolver : IDataResourceResolver
    {
        private readonly IReadOnlyDictionary<string, string> _resources;

        public SeamResolver(IReadOnlyDictionary<string, string> resources)
        {
            _resources = resources;
        }

        public Result<IReadOnlyList<string>> ListResourcePaths(string pathPrefix)
        {
            var paths = _resources.Keys
                .Where(path => path.StartsWith(pathPrefix + "/", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return paths.Length == 0
                ? Result<IReadOnlyList<string>>.Failure(Error.Create(
                    "PACKAGE_REQUIRED", "No package supplies the requested prefix.", pathPrefix))
                : Result<IReadOnlyList<string>>.Success(Array.AsReadOnly(paths));
        }

        public ValueTask<Result<Stream>> OpenReadAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            if (!_resources.TryGetValue(path, out var content))
            {
                return ValueTask.FromResult(Result<Stream>.Failure(Error.Create(
                    "PACKAGE_REQUIRED", "No package supplies the requested resource.", path)));
            }

            Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            return ValueTask.FromResult(Result<Stream>.Success(stream));
        }
    }
}
