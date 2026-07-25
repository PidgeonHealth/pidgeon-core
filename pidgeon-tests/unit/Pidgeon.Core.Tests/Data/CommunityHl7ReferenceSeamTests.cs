// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Extensions;
using System.Text;
using Xunit;

namespace Pidgeon.Core.Tests.Data;

/// <summary>
/// Proves the community-admitted HL7 reference/lookup closure operates end-to-end
/// through the package-neutral data seam: resolver -> HL7ReferenceJsonLoader ->
/// per-kind loaders -> JsonHL7ReferencePlugin -> StandardReferenceService. If this
/// regressed, the public composition would compile a `pidgeon lookup` surface that
/// answers "not found" for data the package provides - a ghost capability the
/// boundary program forbids.
/// </summary>
public sealed class CommunityHl7ReferenceSeamTests
{
    private static readonly string[] PortableBases =
    [
        "standards/hl7/v23", "standards/hl7/v24", "standards/hl7/v25",
        "standards/hl7/v251", "standards/hl7/v26", "standards/hl7/v27", "standards/hl7/v28",
    ];

    private const string PidSegmentJson = """
        {
          "code": "PID",
          "name": "Patient Identification",
          "description": "Patient identification segment",
          "fields": [
            { "field_name": "PID.3", "name": "Patient Identifier List", "description": "Patient identifier list", "data_type": "CX", "optionality": "R", "length": "250" }
          ]
        }
        """;

    private const string CxDataTypeJson = """
        {
          "code": "CX",
          "name": "Extended Composite ID",
          "description": "Extended composite ID with check digit",
          "fields": [
            { "position": 5, "field_description": "Identifier Type Code", "data_type": "ID", "optionality": "O", "length": "5" }
          ]
        }
        """;

    private const string Table0001Json = """
        {
          "name": "Administrative Sex",
          "description": "HL7 table 0001",
          "values": [
            { "code": "F", "description": "Female" },
            { "code": "M", "description": "Male" }
          ]
        }
        """;

    // segment_count is required by the message-composition trigger-event provider
    // that reads the same tree; the fixture carries it so it stays reusable across
    // both seams even though the reference loader never reads it.
    private const string A01TriggerJson = """
        { "name": "Admit/Visit Notification", "description": "Admit or visit notification", "usage": "Event", "segment_count": 3 }
        """;

    private const string AdtA01MessageJson = """
        { "name": "ADT Admit Message", "description": "Admit message structure", "usage": "Message", "segment_count": 3 }
        """;

    [Fact(DisplayName = "A segment lookup through the public reference seam must return the resolver-served definition - regression ships a lookup capability that answers 'not found' for data the package provides")]
    public async Task SegmentLookup_ReturnsResolverServedDefinition()
    {
        var provider = BuildSeam(WithFixtures());
        var service = provider.GetRequiredService<IStandardReferenceService>();

        var result = await service.LookupAsync("PID");

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        result.Value.Name.Should().Be("Patient Identification");
    }

    [Fact(DisplayName = "Field and component paths must project from the same segment JSON the composition providers read - regression forks the reference data model from the generation data model")]
    public async Task FieldAndComponentLookups_ResolveThroughTheSeam()
    {
        var provider = BuildSeam(WithFixtures());
        var plugin = Hl7V23Plugin(provider);

        var field = await plugin.LookupAsync("PID.3");
        field.IsSuccess.Should().BeTrue(field.IsFailure ? field.Error.Message : "");
        field.Value.Name.Should().Be("Patient Identifier List");

        // PID.3.5 walks segment -> data_type CX -> data_types/cx.json position 5,
        // exercising the async component-extraction path end to end.
        var component = await plugin.LookupAsync("PID.3.5");
        component.IsSuccess.Should().BeTrue(component.IsFailure ? component.Error.Message : "");
        component.Value.Name.Should().Be("Identifier Type Code");
    }

    [Fact(DisplayName = "Numeric-table lookup must flow through the resolver seam - regression reverts tables to the filesystem-only path that was dead in every shipped deployment")]
    public async Task NumericTableLookup_ResolvesThroughTheSeam()
    {
        var provider = BuildSeam(WithFixtures());
        var plugin = Hl7V23Plugin(provider);

        var table = await plugin.LookupAsync("0001");

        table.IsSuccess.Should().BeTrue(table.IsFailure ? table.Error.Message : "");
        table.Value.ValidValues.Should().Contain(value => value.Code == "F" && value.Description == "Female");
    }

    [Fact(DisplayName = "Trigger-event and message-type lookups must resolve at the portable path - regression breaks explain-segment for event codes")]
    public async Task TriggerEventAndMessageTypeLookups_ResolveThroughTheSeam()
    {
        var provider = BuildSeam(WithFixtures());
        var plugin = Hl7V23Plugin(provider);

        var trigger = await plugin.LookupAsync("A01");
        trigger.IsSuccess.Should().BeTrue(trigger.IsFailure ? trigger.Error.Message : "");
        trigger.Value.Name.Should().Be("Admit/Visit Notification");

        var messageType = await plugin.LookupAsync("ADT_A01");
        messageType.IsSuccess.Should().BeTrue(messageType.IsFailure ? messageType.Error.Message : "");
        messageType.Value.Name.Should().Be("ADT Admit Message");
    }

    [Fact(DisplayName = "Search must enumerate via the resolver listing - regression silently empties `pidgeon lookup --search` in package-served compositions")]
    public async Task Search_EnumeratesViaTheResolverListing()
    {
        var provider = BuildSeam(WithFixtures());
        var plugin = Hl7V23Plugin(provider);

        var results = await plugin.SearchAsync("patient");

        results.IsSuccess.Should().BeTrue(results.IsFailure ? results.Error.Message : "");
        results.Value.Should().NotBeEmpty("the PID fixture's name and description contain 'patient'");
    }

    [Fact(DisplayName = "An empty resolver must degrade to an honest not-found with no throw - regression turns absent packages into crashes or fabricated elements")]
    public async Task EmptyResolver_DegradesToHonestNotFound()
    {
        var provider = BuildSeam(new SeamResolver(new Dictionary<string, string>(StringComparer.Ordinal)));
        var service = provider.GetRequiredService<IStandardReferenceService>();
        var plugin = Hl7V23Plugin(provider);

        var lookup = await service.LookupAsync("PID");
        lookup.IsSuccess.Should().BeFalse("no package supplies the definition, so lookup must say so, not invent one");

        // Pure path validation needs no data and must keep working.
        plugin.ValidatePath("PID.3.5").IsSuccess.Should().BeTrue();
    }

    private static SeamResolver WithFixtures()
    {
        var resources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var basePath in PortableBases)
        {
            resources[$"{basePath}/segments/pid.json"] = PidSegmentJson;
            resources[$"{basePath}/data_types/cx.json"] = CxDataTypeJson;
            resources[$"{basePath}/tables/0001.json"] = Table0001Json;
            resources[$"{basePath}/trigger_events/a01.json"] = A01TriggerJson;
            resources[$"{basePath}/trigger_events/adt_a01.json"] = AdtA01MessageJson;
        }

        return new SeamResolver(resources);
    }

    private static ServiceProvider BuildSeam(SeamResolver resolver)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDataResourceResolver>(resolver);
        services.AddPidgeonHl7Reference();
        return services.BuildServiceProvider();
    }

    private static IStandardReferencePlugin Hl7V23Plugin(ServiceProvider provider)
        => provider.GetServices<IStandardReferencePlugin>()
            .First(plugin => plugin.StandardIdentifier == "hl7v23");

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
