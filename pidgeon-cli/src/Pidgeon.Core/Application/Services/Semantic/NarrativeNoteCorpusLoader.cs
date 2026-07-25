// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Semantic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Pidgeon.Core.Application.Services.Semantic;

/// <summary>
/// Loads the narrative-note template corpus (b73) from narrative-notes.yaml served through the
/// registered <see cref="IDataResourceResolver"/> composition — the non-lab clinical axes
/// (medication / diagnosis / specimen) whose NTE templates restate a coded fact without ever
/// contradicting it. Parsed once and cached for process lifetime, mirroring
/// <see cref="ReferenceRangeLoader"/>. Graceful: a missing or unparseable corpus yields an empty
/// axis list rather than throwing, so a note leg simply finds no family for the axis and emits
/// nothing (never boilerplate).
/// </summary>
public sealed partial class NarrativeNoteCorpusLoader : INarrativeNoteCorpus
{
    private readonly ILogger<NarrativeNoteCorpusLoader> _logger;
    private readonly IDataResourceResolver _resourceResolver;
    private readonly string _resourcePath;
    private readonly Lazy<Corpus> _corpus;

    public NarrativeNoteCorpusLoader(
        ILogger<NarrativeNoteCorpusLoader> logger,
        IDataResourceResolver resourceResolver,
        string resourcePath = "clinical/narrative-notes.yaml")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
        _resourcePath = resourcePath ?? throw new ArgumentNullException(nameof(resourcePath));
        _corpus = new Lazy<Corpus>(Load);
    }

    public int Version => _corpus.Value.Version;

    public IReadOnlyList<NarrativeNoteAxis> Axes => _corpus.Value.Axes;

    public NarrativeNoteAxis? GetAxis(string axis)
    {
        if (string.IsNullOrWhiteSpace(axis))
            return null;
        return _corpus.Value.Axes.FirstOrDefault(
            a => string.Equals(a.Axis, axis.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private Corpus Load()
    {
        // One-time process-lifetime load inside the Lazy; the registered resolvers complete
        // synchronously, so blocking here cannot deadlock.
        var streamResult = _resourceResolver.OpenReadAsync(_resourcePath).AsTask().GetAwaiter().GetResult();
        if (streamResult.IsFailure)
        {
            _logger.LogWarning("Narrative-note corpus not available: {Error}", streamResult.Error.Message);
            return Corpus.Empty;
        }

        try
        {
            string text;
            using (var stream = streamResult.Value)
            using (var reader = new StreamReader(stream))
            {
                text = reader.ReadToEnd();
            }

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var doc = deserializer.Deserialize<CorpusDocument>(text);

            var axes = new List<NarrativeNoteAxis>();
            foreach (var row in doc?.Axes ?? new List<AxisRow>())
            {
                var name = row.Axis?.Trim();
                var required = row.Required?.Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(required))
                    continue;

                var templates = (row.Templates ?? new List<string>())
                    .Select(t => t?.Trim() ?? string.Empty)
                    .Where(t => t.Length > 0)
                    .ToList();
                if (templates.Count == 0)
                    continue;

                // Qualifiers keep their leading space (the composer appends them verbatim); a null
                // entry becomes the terse empty closer rather than being dropped.
                var qualifiers = (row.Qualifiers ?? new List<string>())
                    .Select(q => q ?? string.Empty)
                    .ToList();
                if (qualifiers.Count == 0)
                    qualifiers.Add(string.Empty);

                var placeholders = (row.Placeholders ?? new List<string>())
                    .Select(p => p?.Trim() ?? string.Empty)
                    .Where(p => p.Length > 0)
                    .ToList();
                if (placeholders.Count == 0)
                    placeholders.Add(required);

                axes.Add(new NarrativeNoteAxis(name, required, placeholders, templates, qualifiers));
            }

            _logger.LogDebug("Loaded narrative-note corpus v{Version} with {Count} axes", doc?.Version ?? 0, axes.Count);
            return new Corpus(doc?.Version ?? 0, axes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse narrative-notes.yaml");
            return Corpus.Empty;
        }
    }

    private sealed record Corpus(int Version, IReadOnlyList<NarrativeNoteAxis> Axes)
    {
        public static readonly Corpus Empty = new(0, Array.Empty<NarrativeNoteAxis>());
    }

    private sealed class CorpusDocument
    {
        public int Version { get; set; }
        public List<AxisRow> Axes { get; set; } = new();
    }

    private sealed class AxisRow
    {
        public string? Axis { get; set; }
        public string? Required { get; set; }
        public List<string>? Placeholders { get; set; }
        public List<string>? Templates { get; set; }
        public List<string>? Qualifiers { get; set; }
    }
}
