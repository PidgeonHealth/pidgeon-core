// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Application.Interfaces.Reference;
using Pidgeon.Core.Domain.Clinical;
using Pidgeon.Core.Domain.Clinical.Entities;
using Pidgeon.Core.Domain.Configuration;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Application.Services.Generation.Entities;

/// <summary>
/// Per-entity generator for <see cref="Patient"/>. Each entity owns its own
/// generation logic, and the async-all-the-way
/// implementation lives outside GenerationService.cs (which must remain free of
/// sync-over-async bridges).
/// </summary>
internal sealed class PatientGenerator
{
    private static readonly FieldConstraints MrnConstraints = new()
    {
        DataType = "CX",
        Required = true,
        Pattern = @"^\d{6,10}$",
        MaxLength = 10
    };

    // The TS data-type path in the constraint value generator derives its own timestamp and
    // ignores DateTimeConstraints, so the actual date of birth comes from CalculateDateOfBirth
    // (anchored on the seeded clock) when this TS value fails the yyyyMMdd parse. No wall-clock
    // bound is carried here — a static DateTime.Today read would make a seeded run drift by day.
    private static readonly FieldConstraints DobConstraints = new()
    {
        DataType = "TS",
        Required = false
    };

    private readonly IDemographicsDataService _demographics;
    private readonly IConstraintResolver _constraints;
    private readonly LockedValueApplier _lockedValues;
    private readonly ILogger<PatientGenerator> _logger;

    public PatientGenerator(
        IDemographicsDataService demographics,
        IConstraintResolver constraints,
        LockedValueApplier lockedValues,
        ILogger<PatientGenerator> logger)
    {
        _demographics = demographics ?? throw new ArgumentNullException(nameof(demographics));
        _constraints = constraints ?? throw new ArgumentNullException(nameof(constraints));
        _lockedValues = lockedValues ?? throw new ArgumentNullException(nameof(lockedValues));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Synchronously drives the async dependency graph. When the injected
    /// <c>IConstraintResolver</c> and <c>IDemographicsDataService</c> return
    /// already-completed tasks (the common case for procedural generation),
    /// this path avoids the async state machine overhead that dominates the
    /// Debug-config hot path.
    /// </summary>
    /// <param name="ctx">Coordinate-addressed generation context (entropy + clock + options).</param>
    /// <param name="constraints">
    /// Scenario constraints (sex/age/pregnancy) the sampled patient must satisfy, or <c>null</c>
    /// when the patient is unconstrained. Consulted <em>before</em> demographic sampling so the
    /// patient matches the story: a pregnancy or sexed scenario pins the sex (and, for pregnancy or
    /// a pediatric band, the age); the given name is then always drawn from the sex-matched dataset
    /// so PID-5 never fights PID-8. All draws stay coordinate-addressed (ADR-0005), so a same-seed
    /// corpus is byte-reproducible given the same code.
    /// </param>
    public Patient Generate(EntityGenerationContext ctx, ScenarioConstraints? constraints = null)
    {
        var rng = ctx.Key.Stream();

        var gender = ResolveGender(ctx, constraints);

        var identifierResult = AwaitSync(_constraints.GenerateConstrainedValueAsync("PID.3", MrnConstraints, ctx.Key.Derive("mrn").AsRandom()));
        var mrn = identifierResult.IsSuccess ? identifierResult.Value?.ToString() : GenerateMrn(rng);

        var dob = ResolveDateOfBirth(ctx, constraints, rng);

        var (firstName, lastName, _) = AwaitSync(_demographics.GenerateRandomNameAsync(ctx.Key.Derive("name").AsRandom(), GenderToNameSex(gender)));
        var address = AwaitSync(_demographics.GenerateRandomAddressAsync(ctx.Key.Derive("address").AsRandom()));
        var phoneNumber = GeneratePhoneNumber(rng);
        var ssn = GenerateSsn(rng);

        var patient = new Patient
        {
            Id = mrn ?? "UNK",
            MedicalRecordNumber = mrn,
            Name = PersonName.Create(lastName, firstName),
            Gender = gender,
            BirthDate = dob,
            SocialSecurityNumber = ssn,
            PhoneNumber = phoneNumber,
            Address = address
        };

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var calculatedAge = dob.HasValue ? ctx.Clock.Year - dob.Value.Year : 0;
            _logger.LogDebug("Generated patient using demographic data: {MRN} - {Name}, Age {Age}",
                mrn, $"{firstName} {lastName}", calculatedAge);
        }

        return AwaitSync(_lockedValues.ApplyAsync(patient, ctx.Options, "Patient"));
    }

    /// <summary>
    /// Async entry point retained for callers that prefer awaiting. Delegates to
    /// the synchronous path since the underlying dependencies complete
    /// synchronously in the procedural generation hot path.
    /// </summary>
    public Task<Patient> GenerateAsync(EntityGenerationContext ctx, ScenarioConstraints? constraints = null) =>
        Task.FromResult(Generate(ctx, constraints));

    private string GeneratePhoneNumber(IValueRng random)
    {
        var phoneNumbers = AwaitSync(_demographics.GetPhoneNumbersAsync());
        if (phoneNumbers.Count > 0)
        {
            return phoneNumbers[random.Next(phoneNumbers.Count)];
        }

        var area = random.Next(200, 999);
        var exchange = random.Next(200, 999);
        var number = random.Next(1000, 9999);
        return $"({area}) {exchange}-{number}";
    }

    // Sync-over-async bridge, kept deliberately for the hot path.
    // Safe only while the awaited dependencies complete synchronously;
    // PatientGeneratorAwaitSyncGuardTests asserts that invariant so a future
    // genuinely-async dependency fails loudly in tests instead of deadlocking.
    private static T AwaitSync<T>(Task<T> task) =>
        task.IsCompletedSuccessfully ? task.Result : task.ConfigureAwait(false).GetAwaiter().GetResult();

    // Childbearing age window a pregnancy scenario imposes when it doesn't state an explicit band.
    private const int PregnancyMinAge = 12;
    private const int PregnancyMaxAge = 55;

    /// <summary>
    /// Resolves the patient's administrative sex. A pregnancy scenario, or an explicit
    /// <see cref="ScenarioConstraints.RequiredSex"/>, pins the sex and skips the demographic draw —
    /// this is the coupling that stops O80 (and other sexed diagnoses) from landing on a mismatched
    /// patient. Unconstrained, it samples a realistic Female/Male-dominant prevalence
    /// (<see cref="GenerationOptions.SexPrevalence"/>) off the "gender" coordinate rather than
    /// drawing uniformly from HL7 table 0001 — table 0001 carries A/F/M/N/O/U, so a uniform draw
    /// made roughly a third to a half of patients Unknown, a distribution a clinical reviewer reads
    /// as unrealistic. The draw stays coordinate-addressed (ADR-0005), so a same-seed corpus is
    /// still byte-reproducible; only PID-8 moves.
    /// </summary>
    private Gender ResolveGender(EntityGenerationContext ctx, ScenarioConstraints? constraints)
    {
        if (constraints?.Pregnant == true)
            return Gender.Female;
        if (constraints?.RequiredSex is { } required)
            return required;

        return SampleSex(ctx.Options.SexPrevalence ?? SexPrevalence.Realistic, ctx.Key.Derive("gender").AsRandom());
    }

    // Weighted single-draw over the Female/Male/Unknown prevalence. Weights are normalized here so a
    // caller can pass percentages, fractions, or raw counts; a degenerate (non-positive total)
    // distribution falls back to Unknown rather than dividing by zero.
    private static Gender SampleSex(SexPrevalence prevalence, Random random)
    {
        var total = prevalence.Total;
        if (total <= 0)
            return Gender.Unknown;

        var roll = random.NextDouble() * total;
        if (roll < prevalence.Female)
            return Gender.Female;
        if (roll < prevalence.Female + prevalence.Male)
            return Gender.Male;
        return Gender.Unknown;
    }

    /// <summary>
    /// Resolves the date of birth. When the scenario imposes an age band (a pediatric range, or the
    /// 12–55 childbearing window for pregnancy) the DOB is pinned to a constrained age drawn off the
    /// "dob" coordinate, bypassing the TS-constraint path so the age can't drift outside the band.
    /// Unconstrained, the original path is preserved byte-for-byte: try the PID-7 TS value, else fall
    /// back to a bucketed age off the shared segment stream.
    /// </summary>
    private DateTime? ResolveDateOfBirth(EntityGenerationContext ctx, ScenarioConstraints? constraints, IValueRng rng)
    {
        if (ResolveAgeBand(constraints) is { } band)
            return CalculateDateOfBirth(GenerateAge(ctx.Key.Derive("dob").Stream(), band), ctx.Clock, rng);

        var dobResult = AwaitSync(_constraints.GenerateConstrainedValueAsync("PID.7", DobConstraints, ctx.Key.Derive("dob").AsRandom()));
        if (dobResult.IsSuccess && DateTime.TryParseExact(
            dobResult.Value?.ToString(),
            "yyyyMMdd",
            null,
            System.Globalization.DateTimeStyles.None,
            out var parsedDate))
        {
            return parsedDate;
        }

        return CalculateDateOfBirth(GenerateAge(rng), ctx.Clock, rng);
    }

    /// <summary>
    /// The effective age band the scenario imposes, or <c>null</c> when age is unconstrained. Pregnancy
    /// intersects the childbearing window with any explicit <see cref="ScenarioConstraints.AgeYears"/>
    /// so both hold.
    /// </summary>
    private static (int Min, int Max)? ResolveAgeBand(ScenarioConstraints? constraints)
    {
        if (constraints is null)
            return null;

        if (constraints.Pregnant == true)
        {
            var (min, max) = constraints.AgeYears ?? (PregnancyMinAge, PregnancyMaxAge);
            return (Math.Max(min, PregnancyMinAge), Math.Min(max, PregnancyMaxAge));
        }

        return constraints.AgeYears;
    }

    // HL7 first-name dataset selector for the sex-matched name draw. Unknown/Other → mixed dataset
    // (the name-vs-sex check only judges M/F, so a non-binary sex cannot mismatch its name).
    private static string? GenderToNameSex(Gender gender) => gender switch
    {
        Gender.Male => "male",
        Gender.Female => "female",
        _ => null
    };

    private static string GenerateMrn(IValueRng random) => $"{random.Next(10000000, 99999999)}";

    private static string GenerateSsn(IValueRng random)
    {
        var area = random.Next(900, 999);
        var group = random.Next(10, 99);
        var serial = random.Next(1000, 9999);
        return $"{area}-{group:D2}-{serial:D4}";
    }

    private static readonly (int Min, int Max, double Cumulative)[] AgeBuckets =
    {
        (0, 17, 0.15),
        (18, 34, 0.35),
        (35, 54, 0.65),
        (55, 64, 0.85),
        (65, 95, 1.00)
    };

    private static int GenerateAge(IValueRng random)
    {
        var roll = random.NextDouble();
        foreach (var (min, max, cumulative) in AgeBuckets)
        {
            if (roll <= cumulative)
            {
                return random.Next(min, max + 1);
            }
        }
        return random.Next(18, 85);
    }

    // Uniform draw within a scenario-imposed age band (pediatric range, pregnancy 12–55).
    private static int GenerateAge(IValueRng random, (int Min, int Max) band)
    {
        var min = Math.Max(0, band.Min);
        var max = Math.Max(min, band.Max);
        return random.Next(min, max + 1);
    }

    private static DateTime CalculateDateOfBirth(int age, DateTime clock, IValueRng random)
    {
        // The ±1-year jitter spreads birthdays within an age year. For a newborn (age 0) a positive
        // jitter can push the date past the clock, so the computed age (message time − DOB) goes
        // negative — a patient born after the message was created. The vaccine age-plausibility check
        // (SEM-F12) correctly flags that impossible demographic, which surfaced as a clean-corpus VXU
        // false positive once PID-7 was always populated (b120). Clamp the jittered date so a DOB is
        // never in the future; every non-newborn date is already in the past and stays byte-identical.
        var dob = clock.Date.AddYears(-age).AddDays(random.Next(-365, 365));
        return dob > clock.Date ? clock.Date : dob;
    }
}
