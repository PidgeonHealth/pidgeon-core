// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Threading;
using System.Threading.Tasks;
using Pidgeon.Core.Domain.Validation;

namespace Pidgeon.Core.Application.Interfaces.Standards;

/// <summary>
/// Per-standard validator plugin. One implementation per supported healthcare
/// standard (HL7, FHIR, NCPDP, ...) lives under
/// <c>Pidgeon.Core.Infrastructure.Standards.{Standard}.Validation</c>.
///
/// <see cref="IMessageValidationService"/> dispatches to these plugins by
/// injecting <c>IEnumerable&lt;IStandardValidationPlugin&gt;</c> and walking
/// <see cref="CanHandle"/> until one matches. Adding a new standard is purely
/// additive — register a new plugin, no Core changes required.
/// </summary>
public interface IStandardValidationPlugin
{
    /// <summary>
    /// Canonical standard identifier, e.g. "hl7", "fhir", "ncpdp".
    /// Matched case-insensitively against the optional <c>standard</c> parameter
    /// passed to <see cref="IMessageValidationService.ValidateAsync"/>.
    /// </summary>
    string StandardName { get; }

    /// <summary>
    /// Cheap structural sniff: does this plugin own the given content?
    /// Implementations should return quickly (prefix/header check, no parsing).
    /// </summary>
    bool CanHandle(string messageContent);

    /// <summary>
    /// Validate the message. Implementations own all standard-specific logic
    /// (required fields, structural rules, data type conformance, profile checks).
    ///
    /// <paramref name="profile"/> is optional. When non-null, the plugin should
    /// validate against that profile in addition to (or in place of) its base
    /// structural rules. Profile naming is plugin-defined: FHIR accepts aliases
    /// like "us-core" or "us-core-patient", canonical URLs, and local file paths;
    /// HL7 and NCPDP currently ignore the parameter until their own vendor/
    /// profile validation paths are wired through this plugin.
    /// </summary>
    Task<Result<ValidationResult>> ValidateAsync(
        string messageContent,
        string? profile,
        ValidationMode mode,
        CancellationToken cancellationToken = default);
}
