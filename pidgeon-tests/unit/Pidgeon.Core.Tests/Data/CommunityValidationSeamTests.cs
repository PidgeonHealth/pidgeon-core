// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pidgeon.Core.Application.Interfaces;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Domain.Validation;
using Pidgeon.Core.Extensions;
using System.Text;
using Xunit;

namespace Pidgeon.Core.Tests.Data;

/// <summary>
/// Proves the community-admitted HL7 validation closure operates end-to-end through
/// the package-neutral data seam: resolver → provider factory → validation plugin →
/// standards-agnostic dispatcher, with zero legacy-assembly involvement. If this
/// regressed, the public composition would compile but could not actually validate —
/// a ghost capability the boundary program forbids.
/// </summary>
public sealed class CommunityValidationSeamTests
{
    private const string ValidAdtA01 =
        "MSH|^~\\&|SEND|FAC|RCV|FAC2|20240101120000||ADT^A01|MSG00001|P|2.3\r" +
        "EVN|A01|20240101120000\r" +
        "PID|1||12345";

    private const string AdtA01MissingPid =
        "MSH|^~\\&|SEND|FAC|RCV|FAC2|20240101120000||ADT^A01|MSG00002|P|2.3\r" +
        "EVN|A01|20240101120000";

    [Fact(DisplayName = "A structurally valid ADT^A01 must produce no Error-severity issue - a regression here means the community composition ships a validator that rejects conformant messages")]
    public async Task ValidMessage_ValidatesThroughThePublicSeam()
    {
        var service = BuildSeam();

        var result = await service.ValidateAsync(ValidAdtA01, standard: "hl7", mode: ValidationMode.Strict);

        result.IsSuccess.Should().BeTrue();
        result.Value.Standard.Should().Be("hl7");
        result.Value.Issues.Where(issue => issue.Severity == ValidationSeverity.Error)
            .Should().BeEmpty();
        // Guard against a silently-skipped structure pass: an unrecognized trigger event
        // reports HL7-STRUCT-002 and skips every structural rule, which would make this
        // test pass without validating anything.
        result.Value.Issues.Should().NotContain(issue => issue.RuleId == "HL7-STRUCT-002");
    }

    [Fact(DisplayName = "A message missing a trigger-required segment is reported as an error - silence here would let invalid messages pass the public validator")]
    public async Task MissingRequiredSegment_IsReportedAsAnError()
    {
        var service = BuildSeam();

        var result = await service.ValidateAsync(AdtA01MissingPid, standard: "hl7", mode: ValidationMode.Strict);

        result.IsSuccess.Should().BeTrue();
        result.Value.Issues.Should().Contain(issue =>
            issue.Severity == ValidationSeverity.Error &&
            issue.Message.Contains("PID", StringComparison.OrdinalIgnoreCase));
    }

    private static IMessageValidationService BuildSeam()
    {
        var resolver = new SeamResolver(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["standards/hl7/v23/segments/msh.json"] = """
                {"code":"MSH","name":"Message header segment","fields":[
                  {"position":1,"field_name":"MSH.1","field_description":"Field Separator","length":"1","data_type":"ST","optionality":"R","repeatability":"-","table":""},
                  {"position":2,"field_name":"MSH.2","field_description":"Encoding Characters","length":"4","data_type":"ST","optionality":"R","repeatability":"-","table":""},
                  {"position":7,"field_name":"MSH.7","field_description":"Date/Time of Message","length":"26","data_type":"TS","optionality":"R","repeatability":"-","table":""},
                  {"position":9,"field_name":"MSH.9","field_description":"Message Type","length":"7","data_type":"CM","optionality":"R","repeatability":"-","table":""}
                ]}
                """,
            ["standards/hl7/v23/segments/evn.json"] = """
                {"code":"EVN","name":"Event type","fields":[
                  {"position":1,"field_name":"EVN.1","field_description":"Event Type Code","length":"3","data_type":"ID","optionality":"R","repeatability":"-","table":""}
                ]}
                """,
            ["standards/hl7/v23/segments/pid.json"] =
                """{"code":"PID","name":"Patient Identification","fields":[]}""",
            ["standards/hl7/v23/trigger_events/adt_a01.json"] = """
                {"code":"ADT_A01","name":"Admit/visit notification","version":"HL7 v2.3","chapter":"Patient Administration","description":"Admit","segment_count":3,"segments":[
                  {"segment_code":"MSH","segment_desc":"Message header segment","optionality":"R","repeatability":"-","is_group":false,"group_path":[],"level":0,"order_index":0},
                  {"segment_code":"EVN","segment_desc":"Event type","optionality":"R","repeatability":"-","is_group":false,"group_path":[],"level":0,"order_index":1},
                  {"segment_code":"PID","segment_desc":"Patient Identification","optionality":"R","repeatability":"-","is_group":false,"group_path":[],"level":0,"order_index":2}
                ]}
                """,
        });

        // Wire through the composition's explicit registration seam only - the same path a
        // community consumer takes. Constructing the internal plugin/service types directly
        // would prove a path the public composition does not offer.
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDataResourceResolver>(resolver);
        services.AddPidgeonHl7Validation();
        return services.BuildServiceProvider().GetRequiredService<IMessageValidationService>();
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
