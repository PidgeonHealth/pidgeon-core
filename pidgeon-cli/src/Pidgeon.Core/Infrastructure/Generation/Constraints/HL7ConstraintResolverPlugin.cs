// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Domain.Configuration;

namespace Pidgeon.Core.Infrastructure.Generation.Constraints;

/// <summary>
/// HL7 v2.3 constraint resolver plugin. Thin dispatcher composing the
/// per-concern helpers: data loading
/// (<see cref="HL7ConstraintDataLoader"/>), JSON parsing
/// (<see cref="HL7ConstraintExtractor"/>), pattern enhancement
/// (<see cref="HL7PatternEnhancer"/>), value generation
/// (<see cref="HL7ConstraintValueGenerator"/>), and validation
/// (<see cref="HL7ConstraintValueValidator"/>).
/// </summary>
public sealed class HL7ConstraintResolverPlugin : IConstraintResolverPlugin
{
    private static readonly Regex HL7FieldPattern = new(@"^[A-Z]{2,3}\.(\d+)(?:\.(\d+))?$", RegexOptions.Compiled);

    private readonly IStandardReferenceService _referenceService;
    private readonly HL7ConstraintExtractor _extractor;
    private readonly HL7ConstraintValueGenerator _valueGenerator;
    private readonly HL7ConstraintValueValidator _valueValidator;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HL7ConstraintResolverPlugin> _logger;

    public string StandardId => "hl7v23";
    public int Priority => 100;

    public HL7ConstraintResolverPlugin(
        IStandardReferenceService referenceService,
        HL7ConstraintExtractor extractor,
        HL7ConstraintValueGenerator valueGenerator,
        HL7ConstraintValueValidator valueValidator,
        IMemoryCache cache,
        ILogger<HL7ConstraintResolverPlugin> logger)
    {
        _referenceService = referenceService ?? throw new ArgumentNullException(nameof(referenceService));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _valueGenerator = valueGenerator ?? throw new ArgumentNullException(nameof(valueGenerator));
        _valueValidator = valueValidator ?? throw new ArgumentNullException(nameof(valueValidator));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool CanResolve(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
            return false;

        return HL7FieldPattern.IsMatch(context);
    }

    public async Task<Result<FieldConstraints>> GetConstraintsAsync(string context)
    {
        var cacheKey = $"hl7-constraints:{context}";
        if (_cache.TryGetValue<FieldConstraints>(cacheKey, out var cached))
        {
            return Result<FieldConstraints>.Success(cached!);
        }

        try
        {
            var match = HL7FieldPattern.Match(context);
            if (!match.Success)
            {
                return Result<FieldConstraints>.Failure($"Invalid HL7 context format: {context}");
            }

            var segmentName = context.Split('.')[0];
            var fieldPosition = int.Parse(match.Groups[1].Value);

            var segmentResult = await _referenceService.LookupAsync(StandardId, segmentName);
            if (!segmentResult.IsSuccess)
            {
                return Result<FieldConstraints>.Failure($"Segment {segmentName} not found");
            }

            var constraints = await _extractor.ExtractFieldConstraintsAsync(segmentResult.Value, fieldPosition);

            _cache.Set(cacheKey, constraints, TimeSpan.FromMinutes(30));

            return Result<FieldConstraints>.Success(constraints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting constraints for {Context}", context);
            return Result<FieldConstraints>.Failure($"Failed to get constraints: {ex.Message}");
        }
    }

    public Task<Result<object>> GenerateValueAsync(FieldConstraints constraints, Random random)
        => _valueGenerator.GenerateValueAsync(constraints, random);

    public Task<Result<bool>> ValidateValueAsync(object value, FieldConstraints constraints)
        => _valueValidator.ValidateValueAsync(value, constraints);
}
