// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Services.DeIdentification;
using Pidgeon.Core.Domain.DeIdentification;

namespace Pidgeon.Core.Application.Interfaces.Standards;

/// <summary>
/// Per-standard de-identification plugin. One implementation per supported
/// healthcare standard lives under
/// <c>Pidgeon.Core.Infrastructure.Standards.{Standard}</c>.
///
/// Follows the same dispatch pattern as <see cref="IStandardValidationPlugin"/>:
/// the Application-layer orchestrators inject
/// <c>IEnumerable&lt;IStandardDeIdentificationPlugin&gt;</c> and select a plugin
/// by <see cref="CanHandle"/> (content sniff) or <see cref="StandardName"/>
/// (explicit standard). Adding a new standard is purely additive — register a
/// new plugin, no Core changes required.
///
/// The interface carries two faces, matching the two Application services it
/// decouples from Infrastructure:
/// <list type="bullet">
/// <item><description>Message de-identification
/// (<see cref="DeIdentifyMessage"/>) — consumed by
/// <c>DeIdentificationService</c>.</description></item>
/// <item><description>PHI inspection (the placeholder / pattern / field-mapping
/// members) — consumed by <c>PhiDetector</c>, which orchestrates value- and
/// field-level scans over the plugin's standard-specific knowledge.</description></item>
/// </list>
/// </summary>
public interface IStandardDeIdentificationPlugin
{
    /// <summary>
    /// Canonical standard identifier, e.g. "hl7", "fhir", "ncpdp".
    /// Matched case-insensitively when an orchestrator selects a plugin by name.
    /// </summary>
    string StandardName { get; }

    /// <summary>
    /// Cheap structural sniff: does this plugin own the given content?
    /// Implementations should return quickly (prefix/header check, no parsing).
    /// </summary>
    bool CanHandle(string messageContent);

    /// <summary>
    /// De-identifies a complete message, replacing PHI with synthetic or
    /// redacted values while preserving referential integrity through
    /// <paramref name="context"/>.
    /// </summary>
    Result<string> DeIdentifyMessage(string messageContent, DeIdentificationContext context);

    /// <summary>
    /// Whether a field value is a de-identification placeholder (already
    /// redacted/synthetic content that must not be re-flagged as PHI).
    /// </summary>
    bool IsDeIdentifiedPlaceholder(string? fieldValue);

    /// <summary>
    /// Whether a field value matches any PHI heuristic pattern.
    /// </summary>
    bool ContainsPotentialPhi(string? fieldValue);

    /// <summary>
    /// Confidence (0-1) that a field value contains PHI.
    /// </summary>
    double GetPhiDetectionConfidence(string fieldValue);

    /// <summary>
    /// Most likely HIPAA identifier type for a field value, based on
    /// pattern matching.
    /// </summary>
    IdentifierType DetectPhiIdentifierType(string fieldValue);

    /// <summary>
    /// HIPAA Safe Harbor mapping for a standard-specific field path
    /// (e.g. "PID.5" for HL7), or null when the field carries no PHI.
    /// </summary>
    PhiFieldMapping? GetPhiFieldMapping(string fieldPath);

    /// <summary>
    /// All HIPAA Safe Harbor field mappings this plugin knows about,
    /// keyed by field path.
    /// </summary>
    IReadOnlyDictionary<string, PhiFieldMapping> GetPhiFieldMappings();
}
