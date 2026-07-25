// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// Default identity literals for the MSH header: sending/receiving application and facility,
/// and the processing ID. Gives these values one named home instead of inlined string
/// constants. Registered as a singleton, so the identity values have a single override point
/// (the DI registration) rather than being scattered as inline literals.
/// </summary>
public record MshHeaderDefaults
{
    /// <summary>MSH-3 Sending Application.</summary>
    public string SendingApplication { get; init; } = "PIDGEON^^L";

    /// <summary>MSH-4 Sending Facility.</summary>
    public string SendingFacility { get; init; } = "PIDGEON_FACILITY";

    /// <summary>MSH-5 Receiving Application.</summary>
    public string ReceivingApplication { get; init; } = "TARGET^^L";

    /// <summary>MSH-6 Receiving Facility.</summary>
    public string ReceivingFacility { get; init; } = "TARGET_FACILITY";

    /// <summary>MSH-11 Processing ID (P = production).</summary>
    public string ProcessingId { get; init; } = "P";
}
