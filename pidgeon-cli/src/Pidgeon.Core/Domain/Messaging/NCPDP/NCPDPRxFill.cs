// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Messaging.NCPDP;

public record NCPDPRxFill
{
    public required NCPDPPatient Patient { get; init; }
    public required NCPDPPrescriber Prescriber { get; init; }
    public required NCPDPMedication MedicationDispensed { get; init; }
    public required NCPDPPharmacy Pharmacy { get; init; }
    public required string FillStatus { get; init; }
    public int FillNumber { get; init; } = 1;
    public int? QuantityDispensed { get; init; }
}
