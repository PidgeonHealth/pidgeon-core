// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Extensions;
using Pidgeon.Core.Generation;
using System.Text;
using Xunit;

namespace Pidgeon.Core.Tests.Data;

/// <summary>
/// Proves the community-admitted HL7 generation closure operates end-to-end through the
/// package-neutral data seam: resolver → providers → composer → IMessageGenerationService,
/// with zero legacy-assembly involvement. The fixture set is deliberately minimal — no
/// demographics tables, no constraint tables, no fallback-values resource — so these tests
/// also pin the honest-degradation contract: absent data relaxes generation, never fails it.
/// </summary>
public sealed class CommunityGenerationSeamTests
{
    private const int Seed = 70021;

    [Fact(DisplayName = "The same seed through two independent community compositions produces byte-identical output - a regression here means community generation lost coordinate-addressed determinism")]
    public async Task SameSeed_ProducesByteIdenticalOutput_ThroughTheSeam()
    {
        var first = await GenerateOnceAsync();
        var second = await GenerateOnceAsync();

        first.Should().Be(second);
    }

    [Fact(DisplayName = "With no demographics tables installed, the demographics service falls back to 'Unknown' placeholders instead of failing - a regression here means a zero-payload community install fails instead of degrading")]
    public async Task MissingDemographics_FallsBackToPlaceholderValues()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDataResourceResolver>(new SeamResolver(Fixtures));
        services.AddPidgeonHl7Generation();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // The silent-fallback contract (kept deliberately, G1 review): absent name tables
        // yield hardcoded placeholders on the patient entity, never a failure. On the wire,
        // PID-5 renders empty in this composition (the table-backed name resolver is a
        // private data-source consumer) - hollow but structurally valid output.
        var demographics = scope.ServiceProvider
            .GetRequiredService<Pidgeon.Core.Application.Interfaces.Reference.IDemographicsDataService>();
        var (firstName, lastName, _) = await demographics.GenerateRandomNameAsync(new Random(Seed));

        firstName.Should().Be("Unknown");
        lastName.Should().Be("Unknown");

        var message = await GenerateOnceAsync();
        message.Should().StartWith("MSH|");
    }

    [Fact(DisplayName = "With no constraint tables and no fallback-values resource, generation still succeeds unconstrained - a regression here means absent data throws instead of honestly degrading")]
    public async Task MissingConstraintData_DegradesToUnconstrainedGeneration()
    {
        var message = await GenerateOnceAsync();

        message.Should().NotBeNullOrWhiteSpace();
        message.Should().StartWith("MSH|");
        message.Should().Contain("PID|");
    }

    private static async Task<string> GenerateOnceAsync()
    {
        // Wire through the composition's explicit registration seam only - the same path a
        // community consumer takes. A fresh provider per call proves cross-process
        // determinism, not cache reuse.
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDataResourceResolver>(new SeamResolver(Fixtures));
        services.AddPidgeonHl7Generation();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var generation = scope.ServiceProvider.GetRequiredService<IMessageGenerationService>();
        var result = await generation.GenerateSyntheticDataAsync(
            "hl7", "ADT^A01", 1, new GenerationOptions { Seed = Seed, AsOf = DeterminismTestClock.Fixed });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.Should().HaveCount(1);
        return result.Value[0];
    }

    // Structural fixtures only: trigger event, segments, data types. Deliberately absent:
    // demographics tables, constraint tables, generation/hl7-fallback-values.json,
    // clinical/narrative-notes.yaml - their absence is what the degradation pins exercise.
    private static readonly IReadOnlyDictionary<string, string> Fixtures =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["standards/hl7/v23/trigger_events/adt_a01.json"] = """
                {"code":"ADT_A01","name":"Admit/visit notification","version":"HL7 v2.3","chapter":"Patient Administration","description":"Admit","segment_count":3,"segments":[
                  {"segment_code":"MSH","segment_desc":"Message header segment","optionality":"R","repeatability":"-","is_group":false,"group_path":[],"level":0,"order_index":0},
                  {"segment_code":"EVN","segment_desc":"Event type","optionality":"R","repeatability":"-","is_group":false,"group_path":[],"level":0,"order_index":1},
                  {"segment_code":"PID","segment_desc":"Patient Identification","optionality":"R","repeatability":"-","is_group":false,"group_path":[],"level":0,"order_index":2}
                ]}
                """,
            ["standards/hl7/v23/segments/msh.json"] = """
                {"code":"MSH","name":"Message header segment","fields":[
                  {"position":1,"field_name":"MSH.1","field_description":"Field Separator","length":"1","data_type":"ST","optionality":"R","repeatability":"-","table":""},
                  {"position":2,"field_name":"MSH.2","field_description":"Encoding Characters","length":"4","data_type":"ST","optionality":"R","repeatability":"-","table":""},
                  {"position":7,"field_name":"MSH.7","field_description":"Date/Time of Message","length":"26","data_type":"TS","optionality":"R","repeatability":"-","table":""},
                  {"position":9,"field_name":"MSH.9","field_description":"Message Type","length":"7","data_type":"CM","optionality":"R","repeatability":"-","table":""},
                  {"position":10,"field_name":"MSH.10","field_description":"Message Control ID","length":"20","data_type":"ST","optionality":"R","repeatability":"-","table":""},
                  {"position":11,"field_name":"MSH.11","field_description":"Processing ID","length":"3","data_type":"PT","optionality":"R","repeatability":"-","table":""},
                  {"position":12,"field_name":"MSH.12","field_description":"Version ID","length":"8","data_type":"ID","optionality":"R","repeatability":"-","table":"0104"}
                ]}
                """,
            ["standards/hl7/v23/segments/evn.json"] = """
                {"code":"EVN","name":"Event type","fields":[
                  {"position":1,"field_name":"EVN.1","field_description":"Event Type Code","length":"3","data_type":"ID","optionality":"R","repeatability":"-","table":"0003"},
                  {"position":2,"field_name":"EVN.2","field_description":"Recorded Date/Time","length":"26","data_type":"TS","optionality":"R","repeatability":"-","table":""}
                ]}
                """,
            ["standards/hl7/v23/segments/pid.json"] = """
                {"code":"PID","name":"Patient Identification","fields":[
                  {"position":3,"field_name":"PID.3","field_description":"Patient ID (Internal ID)","length":"20","data_type":"CX","optionality":"R","repeatability":"Y","table":""},
                  {"position":5,"field_name":"PID.5","field_description":"Patient Name","length":"48","data_type":"XPN","optionality":"R","repeatability":"-","table":""},
                  {"position":7,"field_name":"PID.7","field_description":"Date of Birth","length":"26","data_type":"TS","optionality":"O","repeatability":"-","table":""},
                  {"position":8,"field_name":"PID.8","field_description":"Sex","length":"1","data_type":"IS","optionality":"O","repeatability":"-","table":"0001"}
                ]}
                """,
            ["standards/hl7/v23/data_types/ts.json"] = """
                {"code":"TS","name":"Time stamp","components":[
                  {"position":1,"component_name":"Time","data_type":"ST","optionality":"R"}
                ]}
                """,
            ["standards/hl7/v23/data_types/xpn.json"] = """
                {"code":"XPN","name":"Extended person name","components":[
                  {"position":1,"component_name":"Family name","data_type":"ST","optionality":"O"},
                  {"position":2,"component_name":"Given name","data_type":"ST","optionality":"O"}
                ]}
                """,
            ["standards/hl7/v23/data_types/cx.json"] = """
                {"code":"CX","name":"Extended composite ID with check digit","components":[
                  {"position":1,"component_name":"ID","data_type":"ST","optionality":"O"}
                ]}
                """
        };

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
