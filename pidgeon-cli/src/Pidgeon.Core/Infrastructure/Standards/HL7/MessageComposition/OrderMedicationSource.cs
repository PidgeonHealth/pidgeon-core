// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Globalization;
using Microsoft.Extensions.Logging;
using Pidgeon.Core.Application.Interfaces.Clinical;
using Pidgeon.Core.Application.Interfaces.Data;
using Pidgeon.Core.Application.Interfaces.Semantic;
using Pidgeon.Core.Application.Services.Clinical;
using Pidgeon.Core.Generation;

namespace Pidgeon.Core.Infrastructure.Standards.HL7.MessageComposition;

/// <summary>
/// The ONE medication identity for a pharmacy order chain (name + NDC + dosing facts),
/// shared by every give/dispense code position in the message.
/// </summary>
public sealed record OrderMedication(
    string DrugName,
    string Ndc,
    string DoseAmount,
    string DoseUnit,
    string DosageForm,
    string Route);

/// <summary>
/// Resolves the single medication a pharmacy order chain describes. RXO-1 (requested give),
/// RXE-2 (give) and RXG-4 (give) must all name the SAME drug for a given message — before this
/// source existed each position drew independently, and RXG-4 satisfied its bound vaccine table
/// 0292 with an actual vaccine inside an unrelated med order (audit D-05).
///
/// Every draw derives from the MESSAGE key (<see cref="SegmentGenerationContext.MessageKey"/>),
/// not the segment key, so all chain segments resolve the identical medication (ADR-0005:
/// a pure function of the coordinate). The selection hierarchy: the message's DG1 —
/// the scenario's primary diagnosis — is resolved first, and when it indicates medications the
/// PRIMARY order is one of them (condition coherence at the wire), so the ordered drug treats the
/// diagnosis the message carries. Only when the DG1 indicates no medication (no scenario, or a
/// diagnosis with no relationship mapping) does the order fall back to the historical hierarchy:
/// broad NDC marketed-strength pool (the drug-breadth lever) → scenario-wide medication →
/// medication data source (last resort). The broad pool is the primary source exactly where it was
/// always needed — the no-DG1 case — and its rate stays a configurable lever
/// (<see cref="IncidentalOrderFraction"/>).
///
/// The fabricated <see cref="Pidgeon.Core.Domain.Clinical.Entities.Prescription"/> the generation
/// plugin attaches to pharmacy messages is deliberately NOT consulted by default: its
/// scenario-blind draw from a small static pool would collapse both corpus drug breadth and
/// scenario coherence (before determinism v2 folded the batch index into the message coordinate,
/// it also made every message of a fixed-seed batch carry the same drug). A genuinely
/// caller-supplied prescription (the cross-standard equivalence oracle, Migrate conversion) opts in
/// via <see cref="SegmentGenerationContext.HonorSuppliedPrescription"/> — the chain then carries
/// that prescription's medication verbatim (audit D-23).
/// </summary>
public class OrderMedicationSource
{
    private readonly ClinicalScenarioCoordinator _scenarioCoordinator;
    private readonly IMedicationDataSource _medicationDataSource;
    private readonly IMedicationStrengthsLoader _strengths;
    private readonly IClinicalRelationshipGraph? _graph;
    private readonly IRouteFormLoader? _routeForm;
    private readonly ILogger<OrderMedicationSource> _logger;

    private const double DefaultIncidentalOrderFraction = 0.25;

    /// <summary>
    /// The drug-breadth lever (LEDGER ARCH-091). When the message's DG1
    /// indicates a medication, the primary order is DG1-linked and this rate does NOT displace it.
    /// It governs only the no-DG1-med fallback: the fraction of those orders that draw an incidental
    /// medication from the broad NDC pool (real marketed strengths, curated to exclude marker drugs
    /// and teratogens, so it is coherence-safe) rather than the scenario-wide list. Configurable so a
    /// deployment can dial corpus drug breadth without a code change; the historical flat 0.5
    /// replacement of scenario meds is retired. Emitting a SECOND incidental order alongside the
    /// DG1-linked primary needs multi-order composition and is filed as a leftover.
    /// </summary>
    public double IncidentalOrderFraction { get; init; } = DefaultIncidentalOrderFraction;

    public OrderMedicationSource(
        ClinicalScenarioCoordinator scenarioCoordinator,
        IMedicationDataSource medicationDataSource,
        IMedicationStrengthsLoader strengths,
        ILogger<OrderMedicationSource> logger,
        IClinicalRelationshipGraph? graph = null,
        IRouteFormLoader? routeForm = null)
    {
        _scenarioCoordinator = scenarioCoordinator ?? throw new ArgumentNullException(nameof(scenarioCoordinator));
        _medicationDataSource = medicationDataSource ?? throw new ArgumentNullException(nameof(medicationDataSource));
        _strengths = strengths ?? throw new ArgumentNullException(nameof(strengths));
        _graph = graph;
        _routeForm = routeForm;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The order chain's medication for this message. Resolved once per message (the state
    /// holder on the context) with every draw addressed by the message key, so each chain
    /// segment — whichever asks first — gets the identical drug. Null only when no medication
    /// data is available at all (no pool, no scenario, empty data source).
    /// </summary>
    public async Task<OrderMedication?> GetAsync(SegmentGenerationContext context)
    {
        var state = context.OrderMedication;
        if (state.Resolved)
            return state.Value;

        var resolved = await ResolveAsync(context).ConfigureAwait(false);
        // Keep the chosen form (RXE-6) and route (RXE-7) compatible before the chain shares them, so
        // no order emits a form/route pair a clinician reads as impossible (an inhaler dosed orally).
        state.Value = resolved is null
            ? null
            : HarmonizeRouteForm(resolved, context.MessageKey.Derive("order-medication"));
        state.Resolved = true;
        return state.Value;
    }

    private async Task<OrderMedication?> ResolveAsync(SegmentGenerationContext context)
    {
        // Opt-in authoritative path: the caller declared its Prescription the clinical reality
        // (audit D-23), so the chain carries that drug — no redraw, no enrichment.
        if (context.HonorSuppliedPrescription && context.Prescription is { } rx)
        {
            _logger.LogDebug("Order medication from the caller-supplied prescription: {DrugName}", rx.Medication.Name);
            return FromPrescription(rx);
        }

        var key = context.MessageKey.Derive("order-medication");

        // Primary order coheres with the message's DG1: resolve the scenario's primary diagnosis
        // (the DG1 the composer renders at setId=1) and, when it indicates medications, name one of
        // them. This is the RXE↔DG1 link (S5 B8) — the ordered drug treats the diagnosis the message
        // carries, replacing the pre-B8 grab-bag / broad-pool draw that contradicted it ~half the
        // time. The link draws off its own child coordinate, so it does not disturb any other field.
        var dg1Med = await ResolveDg1LinkedMedicationAsync(key.Derive("dg1_link")).ConfigureAwait(false);
        if (dg1Med is not null)
            return dg1Med;

        var scenarioMeds = _scenarioCoordinator.GetMedications(maxMedications: 10);
        var pool = _strengths.GetAll();

        // No DG1-indicated medication (no scenario primary, or a diagnosis with no relationship
        // mapping): fall back to the historical hierarchy, where the broad NDC pool is the primary
        // source. Breadth is preserved exactly where it was always needed. IncidentalOrderFraction
        // is the configurable rate for orders that still have a scenario med but draw an incidental
        // broad-pool drug instead — the ARCH-091 lever, no longer a flat 0.5 replacement.
        var useBroadPool = pool.Count > 0
            && (!scenarioMeds.Any()
                || key.Derive("incidental_gate").AsRandom().NextDouble() < IncidentalOrderFraction);

        if (useBroadPool)
        {
            // A real marketed strength for a real generic, so the order carries dosing a
            // validation check can confirm. Drug and strength get their own coordinates.
            var med = pool[key.Derive("ndc_drug").AsRandom().Next(pool.Count)];
            var mg = med.StrengthsMg[key.Derive("strength_choice").AsRandom().Next(med.StrengthsMg.Count)];

            _logger.LogDebug("Order medication from the NDC broad pool: {DrugName} {Dose} {Unit}",
                med.Drug, FormatMg(mg), med.Unit);

            return new OrderMedication(med.Drug, med.Ndc, FormatMg(mg), med.Unit, med.Form, med.Route);
        }

        if (scenarioMeds.Any())
        {
            var scenarioMed = scenarioMeds[key.Derive("scenario_choice").AsRandom().Next(scenarioMeds.Count)];
            var (doseAmount, doseUnit) = ParseDosage(scenarioMed.TypicalDosage);
            var ndc = await LookupNdcAsync(scenarioMed.GenericName, key.Derive("ndc_lookup").AsRandom()).ConfigureAwait(false);

            _logger.LogDebug("Order medication from scenario: {DrugName} ({DrugClass})",
                scenarioMed.GenericName, scenarioMed.DrugClass);

            return new OrderMedication(
                scenarioMed.GenericName, ndc, doseAmount, doseUnit,
                InferDosageForm(scenarioMed.DrugClass, scenarioMed.TypicalDosage),
                InferRoute(scenarioMed.DrugClass, scenarioMed.TypicalDosage));
        }

        // Last-resort fallback when the NDC pool is unavailable: a medication from the data source.
        var allMeds = await _medicationDataSource.GetMedicationsAsync().ConfigureAwait(false);
        if (allMeds.Count == 0)
            return null;

        var randomMed = allMeds[key.Derive("medication").AsRandom().Next(allMeds.Count)];
        _logger.LogDebug("No scenario medications or NDC pool; order medication from data source: {DrugName}",
            randomMed.GenericName);

        return new OrderMedication(
            randomMed.GenericName,
            randomMed.Ndc,
            randomMed.Strength.Split(' ').FirstOrDefault() ?? "1",
            randomMed.Unit,
            randomMed.DosageForm,
            randomMed.RouteName);
    }

    // The order medication indicated by the message's DG1 (the scenario's primary diagnosis),
    // resolved through the clinical relationship graph. Null when there is no graph, no primary
    // diagnosis, or the diagnosis maps to no medications — in which case ResolveAsync falls back to
    // the historical hierarchy. The single chosen medication is drawn off the supplied child
    // coordinate, so RXO-1/RXE-2/RXG-4 still resolve one identical drug (via the message-keyed state).
    private async Task<OrderMedication?> ResolveDg1LinkedMedicationAsync(GenerationKey linkKey)
    {
        if (_graph is null)
            return null;

        var primary = _scenarioCoordinator.GetPrimaryDiagnosis();
        if (primary is null || string.IsNullOrWhiteSpace(primary.Code))
            return null;

        var bundle = await _graph.ResolveAsync(primary.Code).ConfigureAwait(false);
        if (bundle.IsFailure || bundle.Value.ExpectedMedications.Count == 0)
            return null;

        var meds = bundle.Value.ExpectedMedications;
        var chosen = meds[linkKey.Derive("choice").AsRandom().Next(meds.Count)];

        // The graph display carries name + strength ("Metformin 500 MG", "Metoprolol Succinate 50 MG",
        // "Insulin Regular Human 100 UNT/ML"): the leading words up to the first numeric token are the
        // drug name (for the NDC lookup); the numeric token and its unit are the give amount + units.
        var genericName = ExtractDrugName(chosen.Display);
        var (doseAmount, doseUnit) = ParseStrengthFromDisplay(chosen.Display);
        var ndc = await LookupNdcAsync(genericName, linkKey.Derive("ndc_lookup").AsRandom()).ConfigureAwait(false);

        _logger.LogDebug("Order medication linked to DG1 {DxCode}: {DrugName} (RxNorm {RxNorm})",
            primary.Code, chosen.Display, chosen.Code);

        // The curated edge carries the drug's form/route when its formulation is not inferable from the
        // display (an inhaler, a gas): use them so RXE-6/RXE-7 cohere. Otherwise fall to the heuristics.
        var dosageForm = !string.IsNullOrWhiteSpace(chosen.Form)
            ? chosen.Form!
            : InferDosageForm(genericName, chosen.Display);
        var route = !string.IsNullOrWhiteSpace(chosen.Route)
            ? chosen.Route!
            : InferRoute(genericName, chosen.Display);

        return new OrderMedication(chosen.Display, ndc, doseAmount, doseUnit, dosageForm, route);
    }

    // The drug name from a graph display: everything before the first numeric strength token
    // ("Metformin 500 MG" -> "Metformin", "Insulin Regular Human 100 UNT/ML" -> "Insulin Regular
    // Human"). Falls back to the whole display when it carries no numeric strength.
    private static string ExtractDrugName(string display)
    {
        if (string.IsNullOrWhiteSpace(display))
            return display;
        var words = display.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var namewords = words.TakeWhile(w => !char.IsDigit(w[0])).ToArray();
        return namewords.Length > 0 ? string.Join(' ', namewords) : display;
    }

    // The give amount + units from a graph display's strength: the first numeric token and the unit
    // word after it ("Metoprolol Succinate 50 MG" -> ("50", "MG"), "Insulin Regular Human 100 UNT/ML"
    // -> ("100", "UNT")). Falls back to a unit dose when the display carries no numeric strength.
    private static (string Amount, string Unit) ParseStrengthFromDisplay(string display)
    {
        if (string.IsNullOrWhiteSpace(display))
            return ("1", "TAB");
        var words = display.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var match = System.Text.RegularExpressions.Regex.Match(words[i], @"^([\d.]+)");
            if (!match.Success)
                continue;
            var unit = i + 1 < words.Length ? words[i + 1].Split('/')[0] : "TAB";
            return (match.Groups[1].Value, unit);
        }
        return ("1", "TAB");
    }

    // The caller's medication verbatim. When the domain med carries no NDC the internal id rides in
    // the code slot (the same pragmatic choice as the data-source fallback below); a proper code-system
    // qualifier thread through the chain is ledger-noted, not built until a caller needs it.
    private static OrderMedication FromPrescription(Domain.Clinical.Entities.Prescription rx) =>
        new(
            rx.Medication.Name,
            rx.Medication.NdcCode ?? rx.Medication.Id,
            rx.Dosage.Dose,
            rx.Dosage.DoseUnit,
            (rx.Medication.DosageForm ?? Domain.Clinical.Entities.DosageForm.Tablet).ToString().ToUpperInvariant(),
            rx.Dosage.Route.ToString().ToUpperInvariant());

    private async Task<string> LookupNdcAsync(string genericName, Random rng)
    {
        try
        {
            var medications = await _medicationDataSource.GetMedicationsAsync().ConfigureAwait(false);
            var match = medications.FirstOrDefault(m =>
                m.GenericName.Contains(genericName, StringComparison.OrdinalIgnoreCase) ||
                genericName.Contains(m.GenericName, StringComparison.OrdinalIgnoreCase));

            return match?.Ndc ?? GenerateFallbackNdc(rng);
        }
        catch
        {
            return GenerateFallbackNdc(rng);
        }
    }

    /// <summary>
    /// The give amount + give units from a scenario's free-text <c>TypicalDosage</c>
    /// ("40-80mg daily", "6.25-25mg BID", "10-100 units daily", "100mg/5mL q6h"). The amount is a
    /// range's lower bound; the unit is the mass/volume token glued to the number (the dominant
    /// scenario shape) or the word after a number-only first token. Frequency and route words
    /// (daily, BID, IV, q6h, nebulized) are never units — taking the second whitespace token
    /// verbatim rendered RXE-5 as "daily"/"IV" on most scenario orders, which a clinician reads
    /// as "Atorvastatin — 40 daily". A string with no amount+unit pair at all ("AUC 5-6 IV")
    /// falls back to a unit dose.
    /// </summary>
    internal static (string Amount, string Unit) ParseDosage(string dosage)
    {
        if (string.IsNullOrWhiteSpace(dosage))
            return ("1", "TAB");

        var parts = dosage.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Number with the unit glued on, optionally a range and trailing punctuation/qualifiers:
        // "40-80mg" -> (40, mg), "3.375-4.5g" -> (3.375, g), "100mg/5mL" -> (100, mg),
        // "325mg," -> (325, mg). Unanchored tail so "/5mL" and "," never pollute the unit.
        var glued = System.Text.RegularExpressions.Regex.Match(
            parts[0], @"^([\d.]+)(?:-[\d.]+)?([A-Za-z%]+)");
        if (glued.Success)
            return (glued.Groups[1].Value, glued.Groups[2].Value);

        // Number-only first token with the unit as the next word: "10-100 units daily" ->
        // (10, units), "1 tab daily" -> (1, tab), "0.02-1.0 mcg/kg/min" -> (0.02, mcg).
        if (parts.Length >= 2)
        {
            var amount = System.Text.RegularExpressions.Regex.Match(parts[0].Split('-')[0], @"^[\d.]+");
            var unit = parts[1].Split('/')[0].TrimEnd(',');
            if (amount.Success && System.Text.RegularExpressions.Regex.IsMatch(unit, @"^[A-Za-z%]+$"))
                return (amount.Value, unit);
        }

        // No parseable amount+unit pair (e.g. carboplatin "AUC 5-6 IV"): a unit dose is the safe
        // rendering — never a frequency or route word in the units slot.
        return ("1", "dose");
    }

    /// <summary>
    /// Dosage form from the dosage text's own route tokens first (an "IV q6h" antibiotic is an
    /// injection, not a tablet; "nebulized" is a nebulizer solution), then the historical
    /// drug-class fallbacks.
    /// </summary>
    internal static string InferDosageForm(string drugClass, string dosage)
    {
        if (dosage.Contains("nebulized", StringComparison.OrdinalIgnoreCase))
            return "NEB";

        if (HasRouteToken(dosage, "IV") || HasRouteToken(dosage, "SQ") || HasRouteToken(dosage, "SC"))
            return "INJ";

        if (drugClass.Contains("Insulin", StringComparison.OrdinalIgnoreCase))
            return "INJ";

        if (drugClass.Contains("Inhaler", StringComparison.OrdinalIgnoreCase) ||
            drugClass.Contains("Bronchodilator", StringComparison.OrdinalIgnoreCase))
            return "INHALER";

        return "TAB";
    }

    /// <summary>
    /// Administration route from the dosage text's own route tokens first ("IV", "SQ"/"SC",
    /// "nebulized"), then the historical drug-class fallbacks.
    /// </summary>
    internal static string InferRoute(string drugClass, string dosage)
    {
        if (HasRouteToken(dosage, "IV"))
            return "IV";

        if (dosage.Contains("nebulized", StringComparison.OrdinalIgnoreCase))
            return "INH";

        if (HasRouteToken(dosage, "SQ") || HasRouteToken(dosage, "SC"))
            return "SC";

        if (drugClass.Contains("Insulin", StringComparison.OrdinalIgnoreCase))
            return "SC";

        if (drugClass.Contains("Inhaler", StringComparison.OrdinalIgnoreCase) ||
            drugClass.Contains("Bronchodilator", StringComparison.OrdinalIgnoreCase))
            return "INH";

        return "PO";
    }

    // A route abbreviation embedded in a free-text dosage ("3.375-4.5g IV q6h", "40mg SQ daily",
    // "500mg IV/PO daily"). Tokenised on spaces, slashes, and commas; exact uppercase match so
    // ordinary words never read as route codes.
    private static bool HasRouteToken(string dosage, string token) =>
        dosage.Split(' ', '/', ',').Any(t => string.Equals(t, token, StringComparison.Ordinal));

    /// <summary>
    /// Keeps RXE-6 (dosage form) and RXE-7 (route) compatible per the openFDA route↔form dataset
    /// (SEM-F09). A form the dataset knows constrains the route to the set it can be given by; a route
    /// the form cannot take (an oral tablet marked intravenous, an inhaler dosed orally) is replaced by
    /// a route the form allows, chosen deterministically off its own child coordinate. A form the
    /// dataset does not recognize, a form with no rule, or a route already allowed is left as composed —
    /// so a coherent order is byte-identical and only an incompatible pair moves. Without the loader
    /// (a DI graph that omits the semantic layer) the medication is returned unchanged.
    /// </summary>
    private OrderMedication HarmonizeRouteForm(OrderMedication med, GenerationKey key)
    {
        if (_routeForm is null)
            return med;

        var canonicalForm = _routeForm.ResolveForm(med.DosageForm);
        if (canonicalForm is null)
            return med;

        var allowed = _routeForm.GetAllowedRoutes(canonicalForm);
        if (allowed.Count == 0)
            return med;

        var canonicalRoute = _routeForm.ResolveRoute(med.Route);
        if (canonicalRoute is not null && allowed.Contains(canonicalRoute))
            return med;

        // Deterministic order then a seeded pick, so the harmonized route reproduces byte-for-byte.
        var ordered = allowed.OrderBy(r => r, StringComparer.Ordinal).ToList();
        var chosen = ordered[key.Derive("route_harmonize").AsRandom().Next(ordered.Count)];
        var routeCode = ToHl7Route(chosen);

        _logger.LogDebug("Harmonized route '{OldRoute}' → '{NewRoute}' for form '{Form}'",
            med.Route, routeCode, med.DosageForm);
        return med with { Route = routeCode };
    }

    // The HL7 table 0162 route code for a canonical route class. A rendering of the chosen route, not
    // a route↔form rule — the compatibility rule stays owned by route-form.yaml (SEM-F09). An unlisted
    // canonical (none today) renders as its upper-cased token, which the dataset still resolves.
    private static string ToHl7Route(string canonicalRoute) => canonicalRoute switch
    {
        "oral" => "PO",
        "intravenous" => "IV",
        "intramuscular" => "IM",
        "subcutaneous" => "SC",
        "inhalation" => "INH",
        "topical" => "TP",
        "ophthalmic" => "OP",
        "nasal" => "NS",
        "rectal" => "PR",
        "vaginal" => "VG",
        "transdermal" => "TD",
        _ => canonicalRoute.ToUpperInvariant()
    };

    private static string GenerateFallbackNdc(Random rng) =>
        $"{rng.Next(10000, 99999)}-{rng.Next(100, 999)}-{rng.Next(10, 99)}";

    // A marketed strength renders as a clean give amount: whole numbers without a trailing ".0"
    // (500, not 500.0), fractional strengths verbatim (2.5, 12.5), invariant culture.
    private static string FormatMg(double mg) =>
        mg == Math.Floor(mg)
            ? ((long)mg).ToString(CultureInfo.InvariantCulture)
            : mg.ToString("0.####", CultureInfo.InvariantCulture);
}
