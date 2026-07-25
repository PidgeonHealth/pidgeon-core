// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Interfaces.Standards;
using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition.DeIdentification;

/// <summary>
/// HL7 v2.x plugin for <see cref="IStandardDeIdentificationPlugin"/>. Thin
/// delegation over the existing HL7 de-identification trio:
/// <see cref="HL7DeIdentifier"/> (message de-identification),
/// <see cref="PhiPatternDetector"/> (value-level PHI heuristics), and
/// <see cref="SafeHarborFieldMapper"/> (HIPAA Safe Harbor field mappings).
///
/// All HL7-specific logic stays in those types; the plugin exists so the
/// Application-layer orchestrators (<c>DeIdentificationService</c>,
/// <c>PhiDetector</c>) depend on the plugin seam instead of importing
/// Infrastructure.Standards.HL7 directly.
/// </summary>
internal sealed class HL7DeIdentificationPlugin : IStandardDeIdentificationPlugin
{
    private readonly HL7DeIdentifier _deIdentifier;
    private readonly PhiPatternDetector _patternDetector;
    private readonly SafeHarborFieldMapper _fieldMapper;
    private IReadOnlyDictionary<string, Application.Services.DeIdentification.PhiFieldMapping>? _projectedMappings;

    public string StandardName => "hl7";

    public HL7DeIdentificationPlugin(
        HL7DeIdentifier deIdentifier,
        PhiPatternDetector patternDetector,
        SafeHarborFieldMapper fieldMapper)
    {
        _deIdentifier = deIdentifier ?? throw new ArgumentNullException(nameof(deIdentifier));
        _patternDetector = patternDetector ?? throw new ArgumentNullException(nameof(patternDetector));
        _fieldMapper = fieldMapper ?? throw new ArgumentNullException(nameof(fieldMapper));
    }

    /// <summary>
    /// HL7 v2 pipe-delimited content starts with an MSH segment header.
    /// Matches the HL7 branch of the de-identification format detection so
    /// plugin dispatch routes exactly the messages the format switch did.
    /// </summary>
    public bool CanHandle(string messageContent)
    {
        if (string.IsNullOrWhiteSpace(messageContent))
            return false;

        return messageContent.TrimStart().StartsWith("MSH|", StringComparison.Ordinal);
    }

    public Result<string> DeIdentifyMessage(string messageContent, DeIdentificationContext context)
        => _deIdentifier.DeIdentifyMessage(messageContent, context);

    public bool IsDeIdentifiedPlaceholder(string? fieldValue)
        => _patternDetector.IsDeIdentifiedPlaceholder(fieldValue);

    public bool ContainsPotentialPhi(string? fieldValue)
        => _patternDetector.ContainsPotentialPhi(fieldValue);

    public double GetPhiDetectionConfidence(string fieldValue)
        => _patternDetector.GetDetectionConfidence(fieldValue);

    public IdentifierType DetectPhiIdentifierType(string fieldValue)
        => _patternDetector.DetectIdentifierType(fieldValue);

    public Application.Services.DeIdentification.PhiFieldMapping? GetPhiFieldMapping(string fieldPath)
        => GetPhiFieldMappings().TryGetValue(fieldPath, out var mapping) ? mapping : null;

    /// <summary>
    /// Projects the infrastructure-level Safe Harbor mappings to the
    /// Application-layer <c>PhiFieldMapping</c> contract records. Built once
    /// per plugin instance — the underlying mappings are immutable.
    /// </summary>
    public IReadOnlyDictionary<string, Application.Services.DeIdentification.PhiFieldMapping> GetPhiFieldMappings()
        => _projectedMappings ??= _fieldMapper.GetAllMappings()
            .ToDictionary(
                kvp => kvp.Key,
                kvp => new Application.Services.DeIdentification.PhiFieldMapping
                {
                    HipaaCategory = kvp.Value.HipaaCategory,
                    IdentifierType = kvp.Value.IdentifierType,
                    RequiresRemoval = kvp.Value.RequiresRemoval,
                    Description = kvp.Value.Description,
                    DetectionPattern = kvp.Value.DetectionPattern
                });
}
