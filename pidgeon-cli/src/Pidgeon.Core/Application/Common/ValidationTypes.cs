// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Application.Common;

/// <summary>
/// Validation modes for different levels of strictness.
/// </summary>
public enum ValidationMode
{
    /// <summary>
    /// Strict validation according to the official specification.
    /// Rejects any deviations from the standard.
    /// </summary>
    Strict,

    /// <summary>
    /// Compatibility mode that accepts common real-world deviations.
    /// Uses vendor-specific patterns and configurations.
    /// </summary>
    Compatibility,

    /// <summary>
    /// Lenient validation that only checks for critical issues.
    /// Allows most variations as long as basic structure is intact.
    /// </summary>
    Lenient
}

/// <summary>
/// Validation severity levels.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Informational message, no action needed.
    /// </summary>
    Info,

    /// <summary>
    /// Warning that should be addressed but doesn't prevent processing.
    /// </summary>
    Warning,

    /// <summary>
    /// Error that prevents successful processing.
    /// </summary>
    Error,

    /// <summary>
    /// Critical error that indicates a serious problem.
    /// </summary>
    Critical
}