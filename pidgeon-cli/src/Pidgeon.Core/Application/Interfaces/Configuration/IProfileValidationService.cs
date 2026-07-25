// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Configuration.Entities;
using Pidgeon.Core.Domain.Validation;

namespace Pidgeon.Core.Application.Interfaces.Configuration;

/// <summary>
/// Validates healthcare messages against a vendor profile (VendorSpecification).
/// Checks required fields, expected segments, and profile-specific constraints.
/// </summary>
public interface IProfileValidationService
{
    /// <summary>
    /// Validates a raw message against a vendor profile's field and segment requirements.
    /// </summary>
    /// <param name="message">Raw HL7 message content</param>
    /// <param name="profile">Vendor specification defining expected fields and segments</param>
    /// <returns>Profile validation result with issues list</returns>
    Task<Result<ProfileValidationResult>> ValidateAsync(string message, VendorSpecification profile);
}

/// <summary>
/// Result of validating a message against a vendor profile.
/// </summary>
public record ProfileValidationResult
{
    /// <summary>
    /// Whether the message conforms to the profile (no errors, warnings allowed).
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Profile that was validated against.
    /// </summary>
    public required string ProfileId { get; init; }

    /// <summary>
    /// Validation issues found during profile validation.
    /// </summary>
    public required List<ValidationIssue> Issues { get; init; }

    /// <summary>
    /// Number of profile rules checked.
    /// </summary>
    public required int RulesChecked { get; init; }
}
