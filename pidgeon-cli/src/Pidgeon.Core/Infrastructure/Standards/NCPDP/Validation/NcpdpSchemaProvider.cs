// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Schema;
using Microsoft.Extensions.Logging;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP.Validation;

/// <summary>
/// Supported NCPDP SCRIPT schema oracle versions. The values map to the
/// embedded XSD bundles under <c>Pidgeon.Data/standards/ncpdp-{key}/</c>.
/// </summary>
internal enum NcpdpScriptVersion
{
    /// <summary>SCRIPT 2017071 — current production standard.</summary>
    V2017071,

    /// <summary>SCRIPT 2023011.</summary>
    V2023011,

    /// <summary>SCRIPT 2023071.</summary>
    V2023071,
}

/// <summary>
/// Loads and caches compiled <see cref="XmlSchemaSet"/>s built from the embedded
/// NCPDP SCRIPT XSD oracle (<c>transport.xsd</c> + its includes) so the validation
/// plugin can perform real structural validation against the schema.
///
/// The SCRIPT XSDs declare no <c>targetNamespace</c>, so conformant on-the-wire
/// SCRIPT documents live in no namespace. Some implementations (including
/// Pidgeon's own current serializer) wrap the payload in the
/// <c>http://www.ncpdp.org/schema/SCRIPT</c> namespace. To validate both shapes
/// against the same authoritative XSD, a namespace-qualified document is checked
/// against a <em>chameleon</em> schema set: an in-memory wrapper schema carrying
/// the requested target namespace that <c>xsd:include</c>s the no-namespace
/// SCRIPT schema, absorbing every component into that namespace.
/// </summary>
internal interface INcpdpSchemaProvider
{
    /// <summary>
    /// Returns a compiled schema set for the given SCRIPT version whose components
    /// are bound to <paramref name="rootNamespace"/> (empty string for the
    /// standard no-namespace form). Returns <c>null</c> when the oracle could not
    /// be loaded or compiled; callers must treat that as the oracle being
    /// unavailable rather than as a clean validation.
    /// </summary>
    XmlSchemaSet? GetSchemaSet(NcpdpScriptVersion version, string rootNamespace);

    /// <summary>
    /// Resolves a declared version string (e.g. SCRIPT <c>TransactionVersion</c>
    /// "20170715", or a folder-style "2017071") to a supported oracle version,
    /// defaulting to <see cref="NcpdpScriptVersion.V2017071"/> when unrecognized.
    /// </summary>
    NcpdpScriptVersion ResolveVersion(string? declaredVersion);
}

/// <summary>
/// Singleton implementation of <see cref="INcpdpSchemaProvider"/>. Schema sets are
/// expensive to compile (the ECL code-list XSD alone is ~360 KB), so each
/// (version, namespace) set is compiled once on first use and cached for the
/// process lifetime. Compilation failures are logged and surfaced as a null
/// schema set so the plugin can report the oracle as unavailable instead of
/// silently passing.
///
/// The XSD bytes come from one or more <see cref="INcpdpXsdSource"/>s, tried in
/// registration order (embedded for dev/test/CI, then the member-supplied package).
/// </summary>
internal sealed class NcpdpSchemaProvider : INcpdpSchemaProvider
{
    private readonly IReadOnlyList<INcpdpXsdSource> _sources;
    private readonly ILogger<NcpdpSchemaProvider> _logger;
    private readonly ConcurrentDictionary<string, XmlSchemaSet?> _cache = new();

    public NcpdpSchemaProvider(IEnumerable<INcpdpXsdSource> sources, ILogger<NcpdpSchemaProvider> logger)
    {
        _sources = (sources ?? throw new ArgumentNullException(nameof(sources))).ToList();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static string VersionKey(NcpdpScriptVersion version) => version switch
    {
        NcpdpScriptVersion.V2017071 => "2017071",
        NcpdpScriptVersion.V2023011 => "2023011",
        NcpdpScriptVersion.V2023071 => "2023071",
        _ => "2017071",
    };

    public NcpdpScriptVersion ResolveVersion(string? declaredVersion)
    {
        if (string.IsNullOrWhiteSpace(declaredVersion))
            return NcpdpScriptVersion.V2017071;

        var v = declaredVersion.Trim();

        // SCRIPT TransactionVersion values are date-stamped (e.g. "20170715",
        // "20230115", "20230715"); the folder keys collapse those to 7 digits.
        if (v.StartsWith("2023", StringComparison.Ordinal))
        {
            // 2023011 (Jan) vs 2023071 (Jul) — disambiguate on the month digits.
            if (v.Contains("2307", StringComparison.Ordinal) || v.StartsWith("2023071", StringComparison.Ordinal))
                return NcpdpScriptVersion.V2023071;
            return NcpdpScriptVersion.V2023011;
        }

        return NcpdpScriptVersion.V2017071;
    }

    public XmlSchemaSet? GetSchemaSet(NcpdpScriptVersion version, string rootNamespace)
    {
        var ns = rootNamespace ?? string.Empty;
        var key = $"{VersionKey(version)}|{ns}";
        return _cache.GetOrAdd(key, _ => BuildSchemaSet(version, ns));
    }

    private XmlSchemaSet? BuildSchemaSet(NcpdpScriptVersion version, string rootNamespace)
    {
        var versionKey = VersionKey(version);

        // Pick the first source that can supply this version's top schema, then use that
        // single source for the whole bundle (transport + every include) so an embedded
        // transport is never combined with package-supplied includes (or vice versa).
        var source = ResolveSource(versionKey);
        if (source == null)
        {
            _logger.LogWarning("No NCPDP XSD source supplies SCRIPT {Version} (transport.xsd unavailable)", versionKey);
            return null;
        }

        var resolver = new SourceXsdResolver(source, versionKey);

        try
        {
            var schemaSet = new XmlSchemaSet { XmlResolver = resolver };

            if (string.IsNullOrEmpty(rootNamespace))
            {
                // Standard no-namespace SCRIPT: add transport.xsd (the top schema
                // that includes ecl/datatypes/structures/script/specialized).
                using var stream = source.Open(versionKey, "transport.xsd");
                if (stream == null)
                {
                    _logger.LogWarning("NCPDP transport.xsd not available for version {Version}", versionKey);
                    return null;
                }

                var settings = new XmlReaderSettings { XmlResolver = resolver, DtdProcessing = DtdProcessing.Prohibit };
                using var reader = XmlReader.Create(stream, settings, $"{SourceXsdResolver.BaseScheme}{versionKey}/transport.xsd");
                schemaSet.Add(null, reader);
            }
            else
            {
                // Namespace-qualified payload: chameleon the no-namespace SCRIPT
                // schema into the requested namespace via an in-memory wrapper.
                var wrapper =
                    "<xsd:schema xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
                    $"targetNamespace=\"{rootNamespace}\" elementFormDefault=\"qualified\">" +
                    "<xsd:include schemaLocation=\"transport.xsd\"/>" +
                    "</xsd:schema>";

                var settings = new XmlReaderSettings { XmlResolver = resolver, DtdProcessing = DtdProcessing.Prohibit };
                using var reader = XmlReader.Create(
                    new StringReader(wrapper), settings, $"{SourceXsdResolver.BaseScheme}{versionKey}/_wrapper.xsd");
                schemaSet.Add(rootNamespace, reader);
            }

            schemaSet.Compile();
            _logger.LogDebug(
                "Compiled NCPDP SCRIPT {Version} schema set ({Count} schemas, namespace '{Namespace}')",
                versionKey, schemaSet.Count, rootNamespace);
            return schemaSet;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compile NCPDP SCRIPT {Version} schema set", versionKey);
            return null;
        }
    }

    /// <summary>
    /// Returns the first registered source that can open this version's top schema
    /// (<c>transport.xsd</c>), or <c>null</c> when none can — the signal that the SCRIPT
    /// oracle is unavailable for the version.
    /// </summary>
    private INcpdpXsdSource? ResolveSource(string versionKey)
    {
        foreach (var source in _sources)
        {
            using var probe = source.Open(versionKey, "transport.xsd");
            if (probe != null)
                return source;
        }

        return null;
    }

    /// <summary>
    /// Resolves <c>xsd:include schemaLocation</c> references (e.g. "ecl.xsd") to the
    /// matching stream from the bundle's single <see cref="INcpdpXsdSource"/>. The SCRIPT
    /// XSDs reference their includes by bare filename, so resolution is filename-based
    /// within a single version bundle.
    /// </summary>
    private sealed class SourceXsdResolver : XmlResolver
    {
        public const string BaseScheme = "xsd-ncpdp://";

        private readonly INcpdpXsdSource _source;
        private readonly string _versionKey;
        private readonly Uri _baseUri;

        public SourceXsdResolver(INcpdpXsdSource source, string versionKey)
        {
            _source = source;
            _versionKey = versionKey;
            _baseUri = new Uri($"{BaseScheme}{versionKey}/");
        }

        public override Uri ResolveUri(Uri? baseUri, string? relativeUri)
            => new(_baseUri, relativeUri ?? string.Empty);

        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var fileName = Path.GetFileName(absoluteUri.AbsolutePath);
            if (string.IsNullOrEmpty(fileName))
                return null;

            var stream = _source.Open(_versionKey, fileName);
            if (stream == null)
                throw new XmlSchemaException($"NCPDP XSD include not available: {_versionKey}/{fileName}");

            return stream;
        }
    }
}
