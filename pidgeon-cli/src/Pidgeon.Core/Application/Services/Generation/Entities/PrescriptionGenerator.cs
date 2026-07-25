// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Domain.Clinical;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.Generation.Entities;

/// <summary>
/// Per-entity generator for <see cref="Prescription"/>. Composes patient +
/// medication + provider + dosage instructions using a shared seeded random
/// so generation stays deterministic.
/// </summary>
internal sealed class PrescriptionGenerator
{
    private static readonly string[] DosageFrequencies = { "QD", "BID", "TID", "QID", "Q6H", "Q8H", "Q12H" };
    private static readonly string[] DosageAmounts = { "0.5", "1", "1.5", "2", "2.5" };
    private static readonly string?[] SpecialInstructions =
    {
        "Take with food",
        "Take on empty stomach",
        "Do not crush or chew",
        "May cause drowsiness",
        "Avoid alcohol",
        "Take at bedtime",
        "Take with plenty of water",
        null
    };

    private readonly PatientGenerator _patientGenerator;
    private readonly ProviderGenerator _providerGenerator;
    private readonly MedicationGenerator _medicationGenerator;
    private readonly ILogger<PrescriptionGenerator> _logger;

    public PrescriptionGenerator(
        PatientGenerator patientGenerator,
        ProviderGenerator providerGenerator,
        MedicationGenerator medicationGenerator,
        ILogger<PrescriptionGenerator> logger)
    {
        _patientGenerator = patientGenerator ?? throw new ArgumentNullException(nameof(patientGenerator));
        _providerGenerator = providerGenerator ?? throw new ArgumentNullException(nameof(providerGenerator));
        _medicationGenerator = medicationGenerator ?? throw new ArgumentNullException(nameof(medicationGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Prescription Generate(EntityGenerationContext ctx, ScenarioConstraints? constraints = null)
    {
        var rng = ctx.Key.Stream();
        var patient = _patientGenerator.Generate(ctx.Derive("patient"), constraints);
        var medication = _medicationGenerator.Generate(ctx.Derive("medication"));
        var prescriber = _providerGenerator.Generate(ctx.Derive("provider"));

        var prescription = new Prescription
        {
            Id = $"RX{rng.Next(1000000, 9999999)}",
            Patient = patient,
            Medication = medication,
            Prescriber = prescriber,
            Dosage = GenerateDosageInstructions(rng, medication),
            DatePrescribed = ctx.Clock.Date.AddDays(-rng.Next(0, 90)),
            Instructions = SpecialInstructions[rng.Next(SpecialInstructions.Length)]
        };

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Generated prescription using demographic data: {RxId} for {Patient}",
                prescription.Id, prescription.Patient.Name.DisplayName);
        }

        return prescription;
    }

    public Task<Prescription> GenerateAsync(EntityGenerationContext ctx, ScenarioConstraints? constraints = null) =>
        Task.FromResult(Generate(ctx, constraints));

    private static DosageInstructions GenerateDosageInstructions(IValueRng random, Medication medication)
    {
        var units = medication.DosageForm switch
        {
            DosageForm.Tablet or DosageForm.Capsule => "tablet",
            DosageForm.Liquid => "mL",
            DosageForm.Injectable => "unit",
            DosageForm.Cream or DosageForm.Ointment => "gram",
            _ => "dose"
        };

        var route = medication.DosageForm switch
        {
            DosageForm.Tablet or DosageForm.Capsule or DosageForm.Liquid => RouteOfAdministration.Oral,
            DosageForm.Injectable => RouteOfAdministration.Subcutaneous,
            DosageForm.Topical or DosageForm.Cream or DosageForm.Ointment => RouteOfAdministration.Topical,
            _ => RouteOfAdministration.Oral
        };

        return new DosageInstructions
        {
            Dose = DosageAmounts[random.Next(DosageAmounts.Length)],
            DoseUnit = units,
            Frequency = DosageFrequencies[random.Next(DosageFrequencies.Length)],
            Route = route,
            Quantity = GenerateQuantity(random, medication),
            DaysSupply = GenerateDaysSupply(random, medication),
            Refills = GenerateRefills(random, medication)
        };
    }

    private static int GenerateQuantity(IValueRng random, Medication medication)
    {
        return medication.DosageForm switch
        {
            DosageForm.Tablet or DosageForm.Capsule => new[] { 30, 60, 90 }[random.Next(3)],
            DosageForm.Liquid => new[] { 100, 150, 200, 240, 300 }[random.Next(5)],
            DosageForm.Injectable => new[] { 1, 2, 3, 5 }[random.Next(4)],
            DosageForm.Cream or DosageForm.Ointment => new[] { 15, 30, 45, 60 }[random.Next(4)],
            _ => 30
        };
    }

    private static int GenerateDaysSupply(IValueRng random, Medication medication)
    {
        return medication.RequiresDeaRegistration()
            ? new[] { 7, 14, 30 }[random.Next(3)]
            : new[] { 30, 60, 90 }[random.Next(3)];
    }

    private static int GenerateRefills(IValueRng random, Medication medication)
    {
        return medication.RequiresDeaRegistration()
            ? random.Next(0, 6)
            : random.Next(0, 12);
    }
}
