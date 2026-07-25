// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Domain.Messaging.NCPDP;

public record NCPDPMedication
{
    public required string DrugDescription { get; init; }
    public string? NDCCode { get; init; }
    public int Quantity { get; init; }

    /// <summary>
    /// NCI Thesaurus code for the quantity's unit of measure (e.g. C48542 = Tablet Dosing Unit),
    /// emitted as Quantity/QuantityUnitOfMeasure/Code. When null the optional element is omitted
    /// rather than asserting a unit the source data did not carry.
    /// </summary>
    public string? QuantityUnitCode { get; init; }

    public int DaysSupply { get; init; }

    /// <summary>
    /// Date the prescription was written. Required by the SCRIPT PrescribedMedication schema
    /// (Medication/WrittenDate). Threaded from the generator's deterministic clock so the same
    /// seed yields the same wire output.
    /// </summary>
    public DateTime WrittenDate { get; init; }

    /// <summary>
    /// Date the medication was most recently dispensed. The SCRIPT schema's home for a fill date
    /// on a dispensed medication (Medication/LastFillDate). Set on RxFill dispensing; null for a
    /// new prescription that has not been filled, in which case the optional element is omitted.
    /// </summary>
    public DateTime? LastFillDate { get; init; }

    public required string SigText { get; init; }
    public int Refills { get; init; }
    public int Substitutions { get; init; }
}
