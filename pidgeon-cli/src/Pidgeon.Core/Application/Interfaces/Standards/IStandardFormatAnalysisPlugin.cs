// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Configuration.Entities;

namespace Pidgeon.Core.Application.Interfaces.Standards;

/// <summary>
/// Plugin interface for standard-specific format deviation analysis.
/// Each healthcare standard (HL7, FHIR, NCPDP) implements this interface
/// to provide specialized logic for detecting format variations and deviations.
/// Follows plugin architecture sacred principle: standard logic isolated in plugins.
/// </summary>
public interface IStandardFormatAnalysisPlugin
{
    /// <summary>
    /// The healthcare standard name this plugin handles (HL7v23, FHIRv4, etc.).
    /// </summary>
    string StandardName { get; }

    /// <summary>
    /// Determines if this plugin can handle format analysis for the given standard.
    /// </summary>
    /// <param name="standard">Healthcare standard name</param>
    /// <returns>True if this plugin can handle the standard</returns>
    bool CanHandle(string standard);

    /// <summary>
    /// Detects encoding deviations specific to this healthcare standard.
    /// For HL7: Field separators, component separators, escape characters.
    /// For FHIR: JSON formatting, encoding standards.
    /// For NCPDP: SCRIPT-specific encoding patterns.
    /// </summary>
    /// <param name="messages">Collection of messages to analyze</param>
    /// <returns>Result containing detected encoding deviations</returns>
    Task<Result<IReadOnlyList<FormatDeviation>>> DetectEncodingDeviationsAsync(
        IEnumerable<string> messages);

    /// <summary>
    /// Detects structural deviations in message format specific to this standard.
    /// For HL7: Segment ordering, extra segments, missing required segments.
    /// For FHIR: Resource structure, missing required elements.
    /// For NCPDP: Message structure, transaction patterns.
    /// </summary>
    /// <param name="messages">Collection of messages to analyze</param>
    /// <param name="messageType">Specific message type (ADT^A01, Patient, NewRx, etc.)</param>
    /// <returns>Result containing detected structural deviations</returns>
    Task<Result<IReadOnlyList<FormatDeviation>>> DetectStructuralDeviationsAsync(
        IEnumerable<string> messages,
        string messageType);

    /// <summary>
    /// Detects field-level formatting deviations specific to this standard.
    /// For HL7: Field formats, component patterns, data types.
    /// For FHIR: Element formats, coding systems, value patterns.
    /// For NCPDP: Field formats, data element patterns.
    /// </summary>
    /// <param name="messages">Collection of messages to analyze</param>
    /// <param name="segmentType">Specific segment/resource type (MSH, Patient, etc.)</param>
    /// <returns>Result containing detected field format deviations</returns>
    Task<Result<IReadOnlyList<FormatDeviation>>> DetectFieldFormatDeviationsAsync(
        IEnumerable<string> messages,
        string segmentType);

    /// <summary>
    /// Performs comprehensive deviation analysis combining all deviation types
    /// specific to this healthcare standard.
    /// </summary>
    /// <param name="messages">Collection of messages to analyze</param>
    /// <param name="messageType">Specific message type (optional)</param>
    /// <returns>Result containing all detected format deviations</returns>
    Task<Result<IReadOnlyList<FormatDeviation>>> DetectAllDeviationsAsync(
        IEnumerable<string> messages,
        string? messageType = null);
}