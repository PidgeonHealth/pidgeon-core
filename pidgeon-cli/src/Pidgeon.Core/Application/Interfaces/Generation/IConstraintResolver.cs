// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core;
using Pidgeon.Core.Domain.Configuration;

namespace Pidgeon.Core.Application.Interfaces.Generation;

/// <summary>
/// Standard-agnostic constraint resolution service.
/// Generates values that comply with field-level constraints defined in healthcare standards.
/// </summary>
public interface IConstraintResolver
{
    /// <summary>
    /// Resolves constraints for a field and generates a compliant value.
    /// </summary>
    /// <param name="context">Field context (e.g., "Patient.Gender" or "PID.8")</param>
    /// <param name="constraints">Field constraint definitions (optional, will be fetched if null)</param>
    /// <param name="random">Random generator for consistent generation</param>
    /// <returns>A constraint-compliant value</returns>
    Task<Result<object>> GenerateConstrainedValueAsync(
        string context,
        FieldConstraints? constraints,
        Random random);

    /// <summary>
    /// Validates a value against field constraints.
    /// </summary>
    /// <param name="context">Field context</param>
    /// <param name="value">Value to validate</param>
    /// <param name="constraints">Field constraint definitions</param>
    /// <returns>Success if valid, failure with validation details if not</returns>
    Task<Result<bool>> ValidateValueAsync(
        string context,
        object value,
        FieldConstraints constraints);

    /// <summary>
    /// Gets constraint definitions for a field.
    /// </summary>
    /// <param name="context">Field context</param>
    /// <returns>Constraint definitions for the field</returns>
    Task<Result<FieldConstraints>> GetConstraintsAsync(string context);
}