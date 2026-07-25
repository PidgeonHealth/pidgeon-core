// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Domain.Reference.Entities;
using Pidgeon.Core.Infrastructure.Reference.HL7;
using Pidgeon.Core.Infrastructure.Standards.HL7;

namespace Pidgeon.Core.Infrastructure.Reference;

/// <summary>
/// Configuration for HL7 version-specific reference plugin.
/// </summary>
public record HL7VersionConfig(
    string Version,
    string StandardId,
    string StandardName,
    string DataPath);

/// <summary>
/// JSON-backed HL7 reference plugin. Dispatches to per-concern loaders
/// under <c>Infrastructure.Reference.HL7</c>, reading reference JSON through
/// the injected <see cref="IDataResourceResolver"/> at portable paths.
/// The plugin owns routing, path validation, cache, and orchestration
/// (Search / ListChildren / ListTopLevel / suggestions); the loaders
/// own the per-kind JSON projection. Lookup internals live in the
/// <c>JsonHL7ReferencePlugin.Lookup</c> partial.
/// </summary>
/// <remarks>
/// There is no shared loader interface (segments, data types, tables, and
/// trigger events have different shapes); loaders register explicitly via
/// <c>AddHL7ReferenceLoaders</c>, are stateless singletons, and
/// receive an <see cref="HL7ResourceContext"/> per call.
/// </remarks>
public partial class JsonHL7ReferencePlugin : IStandardReferencePlugin, IAdvancedStandardReferencePlugin
{
    private readonly ILogger<JsonHL7ReferencePlugin> _logger;
    private readonly IMemoryCache _cache;
    private readonly HL7VersionConfig _config;
    private readonly IDataResourceResolver _resolver;

    private readonly HL7ReferenceJsonLoader _jsonLoader;
    private readonly SegmentDefinitionLoader _segmentLoader;
    private readonly DataTypeLoader _dataTypeLoader;
    private readonly TableLoader _tableLoader;
    private readonly TriggerEventLoader _triggerEventLoader;

    private HL7ResourceContext? _context;
    private bool _isInitialized;
    private readonly object _initLock = new();

    private static readonly Regex HL7PathPattern = new(@"^[A-Z]{2,3}(\.\d+){0,2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TablePattern = new(@"^\d{4}$", RegexOptions.Compiled);
    private static readonly Regex NamedTablePattern = new(@"^[A-Z][a-zA-Z0-9]*$", RegexOptions.Compiled);
    private static readonly Regex TriggerEventPattern = new(@"^[A-Z]\d{2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MessageTypePattern = new(@"^[A-Z]{3}_[A-Z]\d{2}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DataTypePattern = new(@"^[A-Z]{2,3}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string StandardIdentifier => _config.StandardId;
    public string StandardName => _config.StandardName;
    public string Version => _config.Version;

    public JsonHL7ReferencePlugin(
        HL7VersionConfig config,
        ILogger<JsonHL7ReferencePlugin> logger,
        IMemoryCache cache,
        IDataResourceResolver resolver,
        HL7ReferenceJsonLoader jsonLoader,
        SegmentDefinitionLoader segmentLoader,
        DataTypeLoader dataTypeLoader,
        TableLoader tableLoader,
        TriggerEventLoader triggerEventLoader)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _cache = cache;
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _jsonLoader = jsonLoader;
        _segmentLoader = segmentLoader;
        _dataTypeLoader = dataTypeLoader;
        _tableLoader = tableLoader;
        _triggerEventLoader = triggerEventLoader;
    }

    /// <summary>
    /// Builds the per-version lookup context: the portable data-path prefix for
    /// this version plus the injected <see cref="IDataResourceResolver"/> that
    /// serves the reference JSON. Deferred to first use so test harnesses that
    /// register many versions don't pay startup cost. Where the data actually
    /// lives (data package, legacy embedded assembly, test double) is the
    /// resolver registration's decision, never this plugin's.
    /// </summary>
    private HL7ResourceContext EnsureInitialized()
    {
        if (_isInitialized && _context is not null)
            return _context;

        lock (_initLock)
        {
            if (_isInitialized && _context is not null)
                return _context;

            _logger.LogDebug("Initializing JsonHL7ReferencePlugin for {StandardName}...", _config.StandardName);

            _context = new HL7ResourceContext(
                _config,
                Hl7ReferenceDataPath.FromStandardId(_config.DataPath),
                _resolver);
            _isInitialized = true;
            return _context;
        }
    }

    public bool CanHandle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalizedPath = path.ToUpperInvariant();

        if (HL7PathPattern.IsMatch(normalizedPath)) return true;
        if (TablePattern.IsMatch(normalizedPath)) return true;
        if (NamedTablePattern.IsMatch(normalizedPath)) return true;
        if (TriggerEventPattern.IsMatch(normalizedPath)) return true;
        if (MessageTypePattern.IsMatch(normalizedPath)) return true;

        if (DataTypePattern.IsMatch(normalizedPath))
            return IsKnownElement(normalizedPath);

        return false;
    }

    public double GetConfidence(string path)
    {
        if (!CanHandle(path))
            return 0.0;

        var normalizedPath = path.ToUpperInvariant();

        if (TablePattern.IsMatch(normalizedPath)) return 0.98;
        if (NamedTablePattern.IsMatch(normalizedPath)) return 0.96;
        if (TriggerEventPattern.IsMatch(normalizedPath)) return 0.95;
        if (MessageTypePattern.IsMatch(normalizedPath)) return 0.94;

        if (HL7PathPattern.IsMatch(normalizedPath))
        {
            var parts = normalizedPath.Split('.');
            var segment = parts[0];
            var commonSegments = new[] { "MSH", "PID", "OBR", "OBX", "RXE", "RXA", "PV1", "EVN", "NK1", "DG1", "AL1", "GT1", "IN1", "IN2", "PR1" };
            if (commonSegments.Contains(segment)) return 0.95;
            return 0.8;
        }

        if (DataTypePattern.IsMatch(normalizedPath))
        {
            var ctx = EnsureInitialized();

            if (_jsonLoader.Exists(ctx, $"segments/{HL7ReservedResourceName.ResourceStem(normalizedPath)}.json")) return 0.9;
            if (_jsonLoader.Exists(ctx, $"data_types/{normalizedPath.ToLowerInvariant()}.json")) return 0.85;
            return 0.6;
        }

        return 0.3;
    }

    public async Task<Result<StandardElement>> LookupAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!CanHandle(path))
        {
            return Result<StandardElement>.Failure($"Invalid HL7 path format: '{path}'. Expected format: PID.3.5 or MSH.3");
        }

        var context = EnsureInitialized();
        var normalizedPath = path.ToUpperInvariant();
        var cacheKey = $"{StandardIdentifier}:element:{normalizedPath}";

        if (_cache.TryGetValue(cacheKey, out StandardElement? cachedElement))
            return Result<StandardElement>.Success(cachedElement!);

        try
        {
            var element = await LoadElementAsync(context, normalizedPath, cancellationToken);
            if (element != null)
            {
                _cache.Set(cacheKey, element, TimeSpan.FromMinutes(30));
                return Result<StandardElement>.Success(element);
            }
            return Result<StandardElement>.Failure($"Element '{path}' not found in {_config.StandardName} specification");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading element {Path}", path);
            return Result<StandardElement>.Failure($"Error loading element: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<StandardElement>>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Result<IReadOnlyList<StandardElement>>.Success(Array.Empty<StandardElement>());

        var context = EnsureInitialized();

        try
        {
            var results = new List<StandardElement>();
            var queryLower = query.ToLowerInvariant();

            foreach (var segmentFile in _jsonLoader.EnumerateFiles(context, "segments"))
            {
                var segmentElements = await _segmentLoader.LoadAllSegmentElementsAsync(context, segmentFile, cancellationToken);
                var matches = segmentElements.Where(e =>
                    e.Name.ToLowerInvariant().Contains(queryLower) ||
                    e.Description.ToLowerInvariant().Contains(queryLower) ||
                    e.Path.ToLowerInvariant().Contains(queryLower) ||
                    e.Examples.Any(ex => ex.ToLowerInvariant().Contains(queryLower)));
                results.AddRange(matches);
            }

            return Result<IReadOnlyList<StandardElement>>.Success(results.OrderBy(e => e.Path).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for '{Query}'", query);
            return Result<IReadOnlyList<StandardElement>>.Failure($"Search failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<StandardElement>>> ListChildrenAsync(string parentPath, CancellationToken cancellationToken = default)
    {
        var context = EnsureInitialized();

        try
        {
            var parentPathUpper = parentPath.ToUpperInvariant();
            var segment = parentPathUpper.Split('.')[0];

            var segmentFile = $"segments/{HL7ReservedResourceName.ResourceStem(segment)}.json";
            if (!_jsonLoader.Exists(context, segmentFile))
                return Result<IReadOnlyList<StandardElement>>.Success(Array.Empty<StandardElement>());

            var segmentElements = await _segmentLoader.LoadAllSegmentElementsAsync(context, segmentFile, cancellationToken);
            var children = segmentElements
                .Where(e => e.ParentPath?.Equals(parentPathUpper, StringComparison.OrdinalIgnoreCase) == true)
                .OrderBy(e => e.Path)
                .ToList();

            return Result<IReadOnlyList<StandardElement>>.Success(children);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing children for {ParentPath}", parentPath);
            return Result<IReadOnlyList<StandardElement>>.Failure($"List children failed: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<StandardElement>>> ListTopLevelElementsAsync(CancellationToken cancellationToken = default)
    {
        var context = EnsureInitialized();

        try
        {
            var results = new List<StandardElement>();

            foreach (var segmentFile in _jsonLoader.EnumerateFiles(context, "segments"))
            {
                var segmentElements = await _segmentLoader.LoadAllSegmentElementsAsync(context, segmentFile, cancellationToken);
                var topLevel = segmentElements.Where(e => !e.Path.Contains('.')).FirstOrDefault();
                if (topLevel != null) results.Add(topLevel);
            }

            return Result<IReadOnlyList<StandardElement>>.Success(results.OrderBy(e => e.Path).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing top-level elements");
            return Result<IReadOnlyList<StandardElement>>.Failure($"List top-level elements failed: {ex.Message}");
        }
    }

    public Result ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Result.Failure("Path cannot be empty");

        if (!HL7PathPattern.IsMatch(path))
            return Result.Failure($"Invalid HL7 path format: '{path}'. Expected format: PID.3.5, OBR.2, or MSH");

        var parts = path.Split('.');
        var segment = parts[0].ToUpperInvariant();

        if (segment.Length is < 2 or > 3)
            return Result.Failure($"Invalid segment name: '{segment}'. Segment names must be 2-3 characters");

        if (parts.Length > 1)
        {
            for (int i = 1; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out var fieldNumber) || fieldNumber <= 0)
                    return Result.Failure($"Invalid field number: '{parts[i]}'. Field numbers must be positive integers");
            }
        }

        return Result.Success();
    }

    public async Task<Result> InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        try
        {
            await Task.Yield();
            _logger.LogInformation("{StandardName} reference plugin initialized", _config.StandardName);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize {StandardName} reference plugin", _config.StandardName);
            return Result.Failure($"Initialization failed: {ex.Message}");
        }
    }

    public async Task<Result> PreloadElementsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var loadTasks = paths.Select(async path =>
        {
            try
            {
                await LookupAsync(path, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to preload element {Path}", path);
                return false;
            }
        });

        var results = await Task.WhenAll(loadTasks);
        var successCount = results.Count(r => r);
        _logger.LogInformation("Preloaded {SuccessCount}/{TotalCount} elements", successCount, results.Length);
        return Result.Success();
    }

    public ReferencePluginStatistics GetStatistics()
    {
        // TODO(reference, low): these are display-only placeholder values carried from
        // the pre-seam implementation; accurate figures need cache introspection and
        // per-plugin lookup counters. Wire real counters before any consumer acts on them.
        return new ReferencePluginStatistics
        {
            TotalElements = 500,
            LoadedElements = 0,
            MemoryUsage = 0,
            AverageLookupTime = 50.0,
            CacheHitRate = 85.0,
            SuccessfulLookups = 0,
            FailedLookups = 0
        };
    }

    public void ClearCache()
    {
        if (_cache is MemoryCache memoryCache)
            memoryCache.Compact(1.0);
        _logger.LogInformation("{StandardName} reference plugin cache cleared", _config.StandardName);
    }
}
