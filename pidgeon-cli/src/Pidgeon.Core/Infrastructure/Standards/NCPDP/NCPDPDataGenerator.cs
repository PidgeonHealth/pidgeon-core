// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Domain.Messaging.NCPDP;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Infrastructure.Standards.NCPDP.Validation;
using Pidgeon.Core.Services.FieldValueResolvers;

namespace Pidgeon.Core.Infrastructure.Standards.NCPDP;

/// <summary>
/// Generates NCPDP SCRIPT domain messages from synthetic clinical data.
/// Maps clinical domain objects (Patient, Provider, Prescription) to NCPDP wire format models.
/// </summary>
public class NCPDPDataGenerator : INCPDPDataGenerator
{
    private readonly IGenerationService _generationService;
    private readonly IRxNormDataSource _rxNormDataSource;
    private readonly ILogger<NCPDPDataGenerator> _logger;

    private static readonly string[] PharmacyNames =
    {
        "CVS Pharmacy", "Walgreens", "Rite Aid", "Walmart Pharmacy",
        "Kroger Pharmacy", "Costco Pharmacy", "Sam's Club Pharmacy", "Publix Pharmacy"
    };

    private static readonly string[] PharmacistLastNames =
    {
        "Williams", "Davis", "Garcia", "Martinez", "Anderson", "Taylor",
        "Thomas", "Jackson", "White", "Harris"
    };

    private static readonly string[] PharmacistFirstNames =
    {
        "James", "Mary", "Robert", "Patricia", "Michael", "Linda",
        "David", "Jennifer", "Daniel", "Karen"
    };

    private static readonly string[] StreetAddresses =
    {
        "123 Main St", "456 Oak Ave", "789 Elm Blvd", "321 Pine Dr", "654 Maple Ln",
        "987 Cedar Rd", "111 Birch Way", "222 Walnut St"
    };

    private static readonly string[] Cities =
        { "Springfield", "Portland", "Columbus", "Arlington", "Riverside", "Lakewood", "Greenfield", "Fairview" };

    private static readonly string[] States =
        { "IL", "OR", "OH", "TX", "CA", "CO", "MA", "WA" };

    private static readonly string[] ZipCodes =
        { "62701", "97201", "43215", "76010", "92501", "80401", "01201", "98101" };

    private static readonly string[] CancelReasons =
    {
        "Prescriber request",
        "Patient request",
        "Therapy change",
        "Duplicate prescription",
        "Drug interaction detected",
        "Adverse reaction",
        "Insurance coverage issue",
        "Medication no longer needed"
    };

    public NCPDPDataGenerator(
        IGenerationService generationService,
        IRxNormDataSource rxNormDataSource,
        ILogger<NCPDPDataGenerator> logger)
    {
        _generationService = generationService;
        _rxNormDataSource = rxNormDataSource;
        _logger = logger;
    }

    public async Task<NCPDPMessage> GenerateNewRxAsync(GenerationOptions? options = null)
    {
        var opts = options ?? GenerationOptions.Default;
        var prescription = GetPrescription(opts);
        var rxNormDrug = await TryGetRxNormDrugAsync(opts);
        return BuildNewRx(prescription, opts, rxNormDrug);
    }

    public Task<NCPDPMessage> GenerateNewRxAsync(Prescription prescription, GenerationOptions? options = null)
    {
        var opts = options ?? GenerationOptions.Default;
        // No RxNorm enrichment on this path: the caller's prescription IS the clinical reality.
        // Enrichment only applies to self-generated content — on an unseeded call it draws a random
        // drug that MapMedication would let override the supplied name + NDC.
        return Task.FromResult(BuildNewRx(prescription, opts, rxNormDrug: null));
    }

    private static NCPDPMessage BuildNewRx(
        Prescription prescription,
        GenerationOptions opts,
        Application.DTOs.Data.RxNormDrugData? rxNormDrug)
    {
        // Coordinate-addressed entropy: every value is a pure function of (seed, coordinate),
        // so each sub-domain draws from its own derived key and an unrelated upstream draw never shifts it.
        var msgKey = GenerationDeterminism.CreateKey(opts).Derive("ncpdp").Derive("newrx");
        var clock = GenerationDeterminism.CreateClock(opts);

        var patient = MapPatient(prescription.Patient, msgKey.Derive("patient").AsRandom());
        var prescriber = MapPrescriber(prescription.Prescriber, msgKey.Derive("prescriber").AsRandom());
        // WrittenDate is the prescription's own written date, not the message clock — FHIR
        // (authoredOn) and NCPDP must agree about when the same prescription was written (audit D-25).
        var medication = MapMedication(prescription.Medication, prescription.Dosage, rxNormDrug, prescription.AllowGenericSubstitution, prescription.DatePrescribed.Date);
        var pharmacy = GeneratePharmacy(msgKey.Derive("pharmacy").AsRandom());

        var header = GenerateHeader(msgKey.Derive("header").AsRandom(), clock, prescriber.NPI, pharmacy.NCPDPID);

        return new NCPDPMessage
        {
            Version = ResolveVersion(opts),
            Header = header,
            Body = new NCPDPBody
            {
                NewRx = new NCPDPNewRx
                {
                    Patient = patient,
                    Prescriber = prescriber,
                    MedicationPrescribed = medication,
                    Pharmacy = pharmacy
                }
            }
        };
    }

    // SCRIPT release the message targets. The capability sweep passes a folder-style version per
    // cell; the supported set comes from the schema authority (NcpdpScriptVersions) so a release
    // added to the enum is honored here too. An unset or unrecognized value defaults to 2017071 so
    // existing callers and the domain default are unchanged. The serializer maps this to the on-wire stamp.
    private static string ResolveVersion(GenerationOptions opts)
    {
        var v = opts.NcpdpVersion;
        return v != null && NcpdpScriptVersions.Supported.Any(s => string.Equals(s, v, StringComparison.Ordinal))
            ? v
            : "2017071";
    }

    public async Task<NCPDPMessage> GenerateRxFillAsync(GenerationOptions? options = null)
    {
        var opts = options ?? GenerationOptions.Default;
        // Coordinate-addressed entropy: each sub-domain draws from its own derived key.
        var msgKey = GenerationDeterminism.CreateKey(opts).Derive("ncpdp").Derive("rxfill");
        var clock = GenerationDeterminism.CreateClock(opts);
        var prescription = GetPrescription(opts);
        var rxNormDrug = await TryGetRxNormDrugAsync(opts);

        var patient = MapPatient(prescription.Patient, msgKey.Derive("patient").AsRandom());
        var prescriber = MapPrescriber(prescription.Prescriber, msgKey.Derive("prescriber").AsRandom());
        var pharmacy = GeneratePharmacy(msgKey.Derive("pharmacy").AsRandom());

        var fillDate = clock.Date.AddDays(-msgKey.Derive("fill-date").AsRandom().Next(0, 7));
        var quantityDispensed = prescription.Dosage.Quantity ?? 30;

        // The dispensed medication carries the dispensed quantity and the fill date (LastFillDate),
        // the SCRIPT-valid homes for RxFill dispensing data. FillNumber has no element in the
        // RxFillDispensedMedication or RxFill schema, so it stays on the domain record only.
        // WrittenDate is the prescription's own written date (audit D-25); the fill date stays clock-anchored.
        var medication = MapMedication(prescription.Medication, prescription.Dosage, rxNormDrug, prescription.AllowGenericSubstitution, prescription.DatePrescribed.Date)
            with { Quantity = quantityDispensed, LastFillDate = fillDate };

        return new NCPDPMessage
        {
            Version = ResolveVersion(opts),
            Header = GenerateHeader(msgKey.Derive("header").AsRandom(), clock, prescriber.NPI, pharmacy.NCPDPID),
            Body = new NCPDPBody
            {
                RxFill = new NCPDPRxFill
                {
                    Patient = patient,
                    Prescriber = prescriber,
                    MedicationDispensed = medication,
                    Pharmacy = pharmacy,
                    FillStatus = "Dispensed",
                    FillNumber = msgKey.Derive("fill-number").AsRandom().Next(1, 4),
                    QuantityDispensed = quantityDispensed
                }
            }
        };
    }

    public async Task<NCPDPMessage> GenerateCancelRxAsync(GenerationOptions? options = null)
    {
        var opts = options ?? GenerationOptions.Default;
        // Coordinate-addressed entropy: each sub-domain draws from its own derived key.
        var msgKey = GenerationDeterminism.CreateKey(opts).Derive("ncpdp").Derive("cancelrx");
        var clock = GenerationDeterminism.CreateClock(opts);
        var prescription = GetPrescription(opts);
        var rxNormDrug = await TryGetRxNormDrugAsync(opts);

        var patient = MapPatient(prescription.Patient, msgKey.Derive("patient").AsRandom());
        var prescriber = MapPrescriber(prescription.Prescriber, msgKey.Derive("prescriber").AsRandom());
        // WrittenDate is the prescription's own written date (audit D-25).
        var medication = MapMedication(prescription.Medication, prescription.Dosage, rxNormDrug, prescription.AllowGenericSubstitution, prescription.DatePrescribed.Date);

        return new NCPDPMessage
        {
            Version = ResolveVersion(opts),
            Header = GenerateHeader(msgKey.Derive("header").AsRandom(), clock, prescriber.NPI),
            Body = new NCPDPBody
            {
                CancelRx = new NCPDPCancelRx
                {
                    Patient = patient,
                    Prescriber = prescriber,
                    MedicationPrescribed = medication,
                    CancelReason = CancelReasons[msgKey.Derive("cancel-reason").AsRandom().Next(CancelReasons.Length)],
                    PrescriptionNumber = $"RX{msgKey.Derive("rx-number").AsRandom().Next(100000, 999999)}"
                }
            }
        };
    }

    private Prescription GetPrescription(GenerationOptions opts)
    {
        var result = _generationService.GeneratePrescription(opts);
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to generate prescription: {result.Error}");
        return result.Value;
    }

    private async Task<Application.DTOs.Data.RxNormDrugData?> TryGetRxNormDrugAsync(GenerationOptions opts)
    {
        // RxNorm enrichment draws from a shared source that selects via Random.Shared and so cannot
        // honor a seed. When the caller asked for reproducible output (Seed set), skip enrichment and
        // use the deterministic clinical-domain medication instead of breaking byte-stable generation.
        if (opts.Seed is not null)
            return null;

        try
        {
            return await _rxNormDataSource.GetRandomDrugAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RxNorm data unavailable, using clinical domain medication data");
            return null;
        }
    }

    private static NCPDPPatient MapPatient(Patient patient, Random rng)
    {
        return new NCPDPPatient
        {
            LastName = patient.Name.Family ?? "Unknown",
            FirstName = patient.Name.Given ?? "Unknown",
            // Fixed fallback DOB (the seeded prescription always supplies a real birth date; this
            // only guards a null and must not introduce a wall-clock-dependent value).
            DateOfBirth = patient.BirthDate ?? new DateTime(1980, 1, 1),
            Gender = patient.Gender switch
            {
                Domain.Clinical.Entities.Gender.Male => "M",
                Domain.Clinical.Entities.Gender.Female => "F",
                _ => "U"
            },
            Address = patient.Address != null
                ? new NCPDPAddress
                {
                    AddressLine1 = patient.Address.Street1,
                    City = patient.Address.City,
                    State = patient.Address.State,
                    ZipCode = patient.Address.PostalCode
                }
                : GenerateRandomAddress(rng),
            Phone = patient.PhoneNumber
        };
    }

    private static NCPDPPrescriber MapPrescriber(Provider provider, Random rng)
    {
        var lastName = provider.Name.Family ?? "Unknown";

        return new NCPDPPrescriber
        {
            LastName = lastName,
            FirstName = provider.Name.Given ?? "Unknown",
            NPI = !string.IsNullOrWhiteSpace(provider.NpiNumber)
                ? provider.NpiNumber
                : GenerateValidNPI(rng),
            DEA = !string.IsNullOrWhiteSpace(provider.DeaNumber)
                ? provider.DeaNumber
                : GenerateValidDEA(lastName, rng),
            Specialty = provider.Specialty,
            ClinicName = provider.Organization,
            Phone = provider.PhoneNumber,
            Address = provider.Address != null
                ? new NCPDPAddress
                {
                    AddressLine1 = provider.Address.Street1,
                    City = provider.Address.City,
                    State = provider.Address.State,
                    ZipCode = provider.Address.PostalCode
                }
                : null
        };
    }

    private static NCPDPMedication MapMedication(
        Medication medication,
        DosageInstructions dosage,
        Application.DTOs.Data.RxNormDrugData? rxNormDrug,
        bool allowGenericSubstitution,
        DateTime writtenDate)
    {
        return new NCPDPMedication
        {
            DrugDescription = rxNormDrug?.Name ?? medication.DisplayName,
            NDCCode = !string.IsNullOrWhiteSpace(rxNormDrug?.Ndc) ? rxNormDrug.Ndc : medication.NdcCode,
            Quantity = dosage.Quantity ?? 30,
            QuantityUnitCode = QuantityUnitCodeFor(medication.DosageForm),
            DaysSupply = dosage.DaysSupply ?? 30,
            WrittenDate = writtenDate,
            SigText = GenerateSigText(dosage),
            Refills = dosage.Refills ?? 0,
            Substitutions = allowGenericSubstitution ? 0 : 1
        };
    }

    // The SCRIPT Quantity type requires a QuantityUnitOfMeasure (NCI Thesaurus code). Derive it from
    // the medication's actual dosage form rather than asserting "Tablet" for every drug; an unknown
    // form falls back to the generic "Each" dispensing unit (C64933) rather than a wrong unit.
    private static string QuantityUnitCodeFor(DosageForm? form) => form switch
    {
        DosageForm.Tablet => "C48542",      // Tablet Dosing Unit
        DosageForm.Capsule => "C48480",     // Capsule Dosing Unit
        DosageForm.Liquid or DosageForm.Drops => "C28254",   // Milliliter
        DosageForm.Cream or DosageForm.Ointment or DosageForm.Gel or DosageForm.Topical => "C48155", // Gram
        DosageForm.Patch => "C53494",       // Transdermal Patch
        DosageForm.Inhaler => "C42944",     // Inhalation (actuation)
        _ => "C64933"                       // Each (generic dispensing unit)
    };

    private static NCPDPPharmacy GeneratePharmacy(Random rng)
    {
        return new NCPDPPharmacy
        {
            NCPDPID = rng.Next(1000000, 9999999).ToString(),
            NPI = GenerateValidNPI(rng),
            StoreName = PharmacyNames[rng.Next(PharmacyNames.Length)],
            Address = GenerateRandomAddress(rng),
            Phone = $"555-{rng.Next(100, 999)}-{rng.Next(1000, 9999)}",
            PharmacistLastName = PharmacistLastNames[rng.Next(PharmacistLastNames.Length)],
            PharmacistFirstName = PharmacistFirstNames[rng.Next(PharmacistFirstNames.Length)]
        };
    }

    private static NCPDPAddress GenerateRandomAddress(Random rng)
    {
        var idx = rng.Next(StreetAddresses.Length);
        return new NCPDPAddress
        {
            AddressLine1 = StreetAddresses[idx % StreetAddresses.Length],
            City = Cities[idx % Cities.Length],
            State = States[idx % States.Length],
            ZipCode = ZipCodes[idx % ZipCodes.Length]
        };
    }

    private static NCPDPHeader GenerateHeader(Random rng, DateTime clock, string prescriberId, string? pharmacyId = null)
    {
        var toValue = pharmacyId ?? rng.Next(1000000, 9999999).ToString();
        // Deterministic message id derived from the seeded RNG (not Guid.NewGuid, which would
        // differ run-to-run), capped at the 20-char MessageID field length.
        var messageId = $"MSG-{rng.Next(0, 1_000_000_000):D9}";

        return new NCPDPHeader
        {
            To = new NCPDPRoutingInfo
            {
                Qualifier = "P",
                Value = toValue
            },
            From = new NCPDPRoutingInfo
            {
                Qualifier = "D",
                Value = prescriberId
            },
            MessageID = messageId,
            SentTime = clock
        };
    }

    /// <summary>
    /// Generates a valid 10-digit NPI using the Luhn algorithm with the NPPES prefix "80840".
    /// </summary>
    private static string GenerateValidNPI(Random rng)
        => HealthcareIdentifierFactory.GenerateNpi(rng);

    /// <summary>
    /// Generates a DEA number with valid check digit. Format: 2 letters + 7 digits.
    /// First letter is registrant type (A/B/F/M), second is first letter of last name.
    /// </summary>
    private static string GenerateValidDEA(string lastName, Random rng)
        => HealthcareIdentifierFactory.GenerateDea(lastName, rng);

    private static string GenerateSigText(DosageInstructions dosage)
    {
        var route = dosage.Route switch
        {
            RouteOfAdministration.Oral => "by mouth",
            RouteOfAdministration.Topical => "topically",
            RouteOfAdministration.Inhalation => "by inhalation",
            RouteOfAdministration.Subcutaneous => "subcutaneously",
            RouteOfAdministration.Intramuscular => "intramuscularly",
            RouteOfAdministration.Intravenous => "intravenously",
            _ => "as directed"
        };

        var frequency = dosage.Frequency.ToUpperInvariant() switch
        {
            "QD" or "DAILY" => "once daily",
            "BID" or "Q12H" => "twice daily",
            "TID" or "Q8H" => "three times daily",
            "QID" or "Q6H" => "four times daily",
            "QHS" => "at bedtime",
            "PRN" => "as needed",
            _ => dosage.Frequency.ToLowerInvariant()
        };

        var sig = $"Take {dosage.Dose} {dosage.DoseUnit} {route} {frequency}";

        if (!string.IsNullOrWhiteSpace(dosage.AdditionalInstructions))
            sig += $" {dosage.AdditionalInstructions}";

        return sig;
    }
}
