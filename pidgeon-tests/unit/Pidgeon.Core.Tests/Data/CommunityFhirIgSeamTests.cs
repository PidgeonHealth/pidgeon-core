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
using Pidgeon.Core.Infrastructure.Data;
using System.Text;
using Xunit;

namespace Pidgeon.Core.Tests.Data;

/// <summary>
/// Proves the community-admitted installed-IG package path operates end-to-end through
/// the package-neutral catalog seam: InstalledDataPackageCatalog -> FhirIGService ->
/// one-shot plugin bootstrap -> resolver-fed StructureDefinitionLoader -> profile
/// validation. If this regressed, the public composition would compile an IG surface
/// that silently ignores installed packages - a ghost capability the boundary program
/// forbids. Every test pins an explicit install directory; the machine-local
/// ~/.pidgeon/data must never influence an assertion.
/// </summary>
public sealed class CommunityFhirIgSeamTests
{
    private const string UsCorePatientCanonical =
        "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient";

    // Passes the curated required-field floor (resourceType + name) AND the base SD,
    // but carries no identifier - only the installed package's synthetic
    // Patient.identifier min=1 can flag it.
    private const string PatientWithoutIdentifier =
        """{"resourceType":"Patient","name":[{"family":"Doe"}],"gender":"male"}""";

    // The base R4 spec has Patient.identifier as optional (min=0); the package SD staged
    // below is SYNTHETIC and sets identifier min=1 deliberately, so an identifier error
    // can ONLY come from the installed package's profile driving the check.
    private const string UsCorePatientSd = """
        {
          "resourceType": "StructureDefinition",
          "url": "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient",
          "name": "USCorePatientProfile",
          "type": "Patient",
          "kind": "resource",
          "snapshot": {
            "element": [
              { "path": "Patient", "min": 1, "max": "1" },
              { "path": "Patient.identifier", "min": 1, "max": "*", "type": [ { "code": "Identifier" } ] }
            ]
          }
        }
        """;

    private const string BasePatientSd = """
        {
          "resourceType": "StructureDefinition",
          "url": "http://hl7.org/fhir/StructureDefinition/Patient",
          "name": "Patient",
          "type": "Patient",
          "kind": "resource",
          "snapshot": {
            "element": [
              { "path": "Patient", "min": 1, "max": "1" }
            ]
          }
        }
        """;

    [Fact(DisplayName = "A profile delivered by an installed IG package must drive --profile validation through the public seam - regression means the community composition ships an IG path that silently ignores installed packages")]
    public async Task InstalledPackageProfile_DrivesProfileValidation()
    {
        using var install = new TempInstallDir();
        install.StagePackage("fhir-us-core-6.0", "6.1.0", UsCorePatientSd);
        var service = BuildIgSeam(install.Root);

        var result = await service.ValidateAsync(
            PatientWithoutIdentifier, standard: "fhir", profile: UsCorePatientCanonical, mode: ValidationMode.Strict);

        result.IsSuccess.Should().BeTrue();
        result.Value.Issues.Should().Contain(issue =>
            issue.Severity == ValidationSeverity.Error &&
            issue.Location.Contains("identifier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "A bare IG short name must resolve against installed packages via the one-shot bootstrap - regression strands `pidgeon validate --profile us-core-patient` on a config failure despite an installed package")]
    public async Task BareShortName_ResolvesAgainstInstalledPackage()
    {
        using var install = new TempInstallDir();
        install.StagePackage("fhir-us-core-6.0", "6.1.0", UsCorePatientSd);
        var service = BuildIgSeam(install.Root);

        var result = await service.ValidateAsync(
            PatientWithoutIdentifier, standard: "fhir", profile: "us-core-patient", mode: ValidationMode.Strict);

        result.IsSuccess.Should().BeTrue();
        result.Value.Issues.Should().Contain(issue =>
            issue.Severity == ValidationSeverity.Error &&
            issue.Location.Contains("identifier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "With no installed package the IG profile path must fail as a typed config error, never a fabricated pass - regression turns absent data into wrong answers")]
    public async Task NoInstalledPackage_ProfileFailsTyped_BaseStillValidates()
    {
        using var install = new TempInstallDir();
        var service = BuildIgSeam(install.Root);

        var profiled = await service.ValidateAsync(
            PatientWithoutIdentifier, standard: "fhir", profile: UsCorePatientCanonical, mode: ValidationMode.Strict);
        profiled.IsSuccess.Should().BeFalse(
            "an unresolvable profile is a config error, not a validated-clean resource");

        var baseline = await service.ValidateAsync(
            PatientWithoutIdentifier, standard: "fhir", mode: ValidationMode.Strict);
        baseline.IsSuccess.Should().BeTrue();
        baseline.Value.Issues.Where(issue => issue.Severity == ValidationSeverity.Error)
            .Should().BeEmpty("base validation must keep working when no IG package is installed");
    }

    [Fact(DisplayName = "The filesystem catalog must report only manifest-bearing directories and stamp the installed manifest's version - regression makes ghost or misversioned packages appear installed")]
    public void FilesystemCatalog_ReportsOnlyManifestBearingDirectories()
    {
        using var install = new TempInstallDir();
        install.StagePackage("fhir-us-core-6.0", "6.1.0", UsCorePatientSd);
        Directory.CreateDirectory(Path.Combine(install.Root, "bare-directory-no-manifest"));
        var catalog = new InstalledDataPackageCatalog(
            NullLogger<InstalledDataPackageCatalog>.Instance, install.Root);

        catalog.GetInstalledContentRoot("fhir-us-core-6.0").Should().NotBeNull();
        catalog.GetInstalledContentRoot("bare-directory-no-manifest").Should().BeNull(
            "a directory without manifest.json is not an installed package");
        catalog.GetInstalledContentRoot("never-installed").Should().BeNull();

        catalog.FindInstalled("fhir-us-core-6.0")!.Identity.Version.Should().Be("6.1.0",
            "the community catalog stamps the installed manifest's version");

        var listed = catalog.ListInstalledPackages();
        listed.Should().ContainSingle().Which.Identity.Id.Should().Be("fhir-us-core-6.0");
    }

    private static IMessageValidationService BuildIgSeam(string installDirectory)
    {
        var resolver = new SeamResolver(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["standards/fhir/r4/profiles/base/StructureDefinition-Patient.json"] = BasePatientSd,
        });

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDataResourceResolver>(resolver);
        // Pin the catalog to the test's install dir BEFORE the extension runs so its
        // TryAdd keeps this instance - the machine-local ~/.pidgeon/data must never
        // leak into an assertion.
        services.AddSingleton<IDataPackageCatalog>(
            new InstalledDataPackageCatalog(
                NullLogger<InstalledDataPackageCatalog>.Instance, installDirectory));
        services.AddPidgeonFhirValidation();
        services.AddPidgeonFhirIgPackages();
        return services.BuildServiceProvider().GetRequiredService<IMessageValidationService>();
    }

    /// <summary>
    /// A unique, disposable package install root. Staged packages follow the real
    /// installer layout: <c>{root}/{package-name}/manifest.json</c> plus the package's
    /// StructureDefinition JSON files in the same directory.
    /// </summary>
    private sealed class TempInstallDir : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(), "pidgeon-ig-seam-" + Guid.NewGuid().ToString("N"));

        public TempInstallDir() => Directory.CreateDirectory(Root);

        public void StagePackage(string name, string version, string structureDefinitionJson)
        {
            var dir = Path.Combine(Root, name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "manifest.json"), $$"""
                {
                  "name": "{{name}}",
                  "version": "{{version}}",
                  "description": "Test-staged IG package",
                  "source": "test-fixture",
                  "license": "CC0-1.0",
                  "recordCount": 1,
                  "dataType": "fhir-ig",
                  "schema": "fhir-ig-package",
                  "generatedAt": "2026-01-01T00:00:00Z"
                }
                """);
            File.WriteAllText(Path.Combine(dir, "StructureDefinition-us-core-patient.json"), structureDefinitionJson);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a straggling handle must not fail the test run.
            }
        }
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
