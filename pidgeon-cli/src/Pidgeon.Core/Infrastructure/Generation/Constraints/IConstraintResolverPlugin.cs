// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core;
using Pidgeon.Core.Domain.Configuration;

namespace Pidgeon.Core.Infrastructure.Generation.Constraints;

/// <summary>
/// Standard-specific constraint resolution plugin.
/// Each healthcare standard (HL7, FHIR, NCPDP) implements its own plugin.
/// </summary>
public interface IConstraintResolverPlugin
{
    /// <summary>
    /// Standard identifier (e.g., "hl7v23", "fhir-r4", "ncpdp")
    /// </summary>
    string StandardId { get; }

    /// <summary>
    /// Priority for plugin selection (higher = preferred)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Determines if this plugin can handle the given context.
    /// </summary>
    /// <param name="context">Field context (e.g., "PID.8", "Patient.gender")</param>
    /// <returns>True if this plugin can resolve constraints for the context</returns>
    bool CanResolve(string context);

    /// <summary>
    /// Gets constraint definitions for a field.
    /// </summary>
    /// <param name="context">Field context</param>
    /// <returns>Field constraint definitions</returns>
    Task<Result<FieldConstraints>> GetConstraintsAsync(string context);

    /// <summary>
    /// Generates a constraint-compliant value.
    /// </summary>
    /// <param name="constraints">Field constraints to satisfy</param>
    /// <param name="random">Random generator for consistent results</param>
    /// <returns>A value that satisfies the constraints</returns>
    Task<Result<object>> GenerateValueAsync(
        FieldConstraints constraints,
        Random random);

    /// <summary>
    /// Validates a value against constraints.
    /// </summary>
    /// <param name="value">Value to validate</param>
    /// <param name="constraints">Field constraints to check against</param>
    /// <returns>Success if valid, failure with details if not</returns>
    Task<Result<bool>> ValidateValueAsync(
        object value,
        FieldConstraints constraints);
}