// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Configuration.Entities;

namespace Pidgeon.Core.Application.Interfaces.Standards;

/// <summary>
/// Plugin interface for standard-specific field pattern parsing and delegation.
/// Each healthcare standard (HL7, FHIR, NCPDP) implements this interface
/// to provide specialized parsing logic and delegate analysis to adapters.
/// Follows plugin architecture sacred principle: plugins handle parsing, adapters handle analysis.
/// </summary>
public interface IStandardFieldAnalysisPlugin
{
    /// <summary>
    /// The healthcare standard name this plugin handles (HL7v23, FHIRv4, etc.).
    /// </summary>
    string StandardName { get; }

    /// <summary>
    /// Determines if this plugin can handle field analysis for the given standard.
    /// </summary>
    /// <param name="standard">Healthcare standard name</param>
    /// <returns>True if this plugin can handle the standard</returns>
    bool CanHandle(string standard);

    /// <summary>
    /// Analyzes field population patterns across a collection of messages.
    /// Plugin responsibility: Parse standard-specific raw messages into domain objects,
    /// then delegate analysis to IMessagingToConfigurationAdapter.
    /// For HL7: Parse HL7 strings → HL7Message objects → delegate to adapter
    /// For FHIR: Parse JSON → FHIR resources → delegate to adapter  
    /// For NCPDP: Parse NCPDP strings → NCPDP objects → delegate to adapter
    /// </summary>
    /// <param name="messages">Collection of raw message strings to parse and analyze</param>
    /// <param name="messageType">Specific message type for pattern analysis</param>
    /// <returns>Result containing field patterns from adapter analysis</returns>
    Task<Result<FieldPatterns>> AnalyzeFieldPatternsAsync(
        IEnumerable<string> messages,
        string messageType);
}