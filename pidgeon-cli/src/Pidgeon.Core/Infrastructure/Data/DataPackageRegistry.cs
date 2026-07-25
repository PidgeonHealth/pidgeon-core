// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.Core.Infrastructure.Data;

/// <summary>
/// The catalog of installable data packages that <see cref="DataPackageManager"/> installs and
/// loads from. Separated from the manager so the catalog — which grows by one entry per dataset /
/// standard — is a pure data accumulation point that stays out of the manager's logic budget.
/// License-restricted entries (UMLS / AMA / NCPDP / X12) carry no in-tree data; they are
/// member-supplied via the <c>--accept-license</c> flow.
/// </summary>
internal static class DataPackageRegistry
{
    /// <summary>
    /// One installable package: its data type, version, description, source, and license.
    /// <paramref name="DownloadUrl"/> is set only for CC0 FHIR IG packages that can be
    /// auto-fetched from the HL7 registry; it is null for terminology / licensed packages
    /// whose data is never publicly downloadable (those install from a member-supplied source).
    /// <paramref name="EmbeddedResourcePrefix"/> is set only for first-party packs whose payload
    /// ships inside the Pidgeon.Data assembly; install materializes those resources directly —
    /// no network, no ETL (the offline/air-gap lane for Pidgeon-derived packages).
    /// </summary>
    internal record Entry(
        string DataType,
        string Version,
        string Description,
        string Source,
        string License,
        string? DownloadUrl = null,
        string? EmbeddedResourcePrefix = null);

    public static readonly Dictionary<string, Entry> Packages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["loinc"] = new("lab-tests", "2.81.0", "LOINC 2.81 — laboratory observation codes", "Regenstrief Institute", "LOINC License (attribution required)"),
        ["icd10"] = new("diagnoses", "2026.0.0", "ICD-10-CM 2026 — diagnosis codes", "CMS/NCHS", "Public domain"),
        ["icd9"] = new("diagnoses", "32.0.0", "ICD-9-CM v32 — legacy diagnosis codes", "CMS", "Public domain"),
        ["ndc"] = new("medications", "2026.02.0", "NDC — National Drug Code Directory", "FDA", "Public domain"),
        ["cvx"] = new("vaccines", "2026.02.0", "CVX — vaccine administered codes", "CDC", "Public domain"),
        ["hcpcs"] = new("procedures", "2026.01.0", "HCPCS 2026 — procedure codes", "CMS", "Public domain"),
        ["snomed"] = new("snomed-concepts", "2025.09", "SNOMED CT US Edition clinical concepts", "NLM/IHTSDO", "UMLS"),
        ["rxnorm"] = new("rxnorm-drugs", "2026.02", "RxNorm — prescribable drug database", "NLM", "UMLS"),
        ["cpt"] = new("cpt-codes", "2026.0.0", "CPT 2026 — procedure codes", "AMA", "AMA Developer License"),
        // NCPDP SCRIPT XSDs are NCPDP member-only / no-commercial-use — same shape as CPT/UMLS:
        // loaders are open, the schemas are member-supplied via this license-acceptance flow and are
        // never shipped in the OSS repo.
        ["ncpdp-script"] = new("ncpdp-schemas", "2017071", "NCPDP SCRIPT XSD schema set (member-licensed; not shipped in OSS)", "NCPDP", "NCPDP SCRIPT (member-only)"),
        // X12 5010 transaction implementation guides (TR3s) + WPC external code lists are licensed by
        // ASC X12 / WPC — same BYO pattern: the envelope/codec is open, the guides are member-supplied
        // via --accept-license and never shipped. No in-tree X12 reference data today; this entry
        // reserves the gated install path for when the guides are purchased.
        ["x12-5010"] = new("x12-guides", "5010", "X12 5010 HIPAA transaction implementation guides + code lists (licensed; not shipped)", "ASC X12 / WPC", "X12 5010 implementation guides (licensed)"),
        // Commercial drug knowledge base (Medi-Span / FDB / Micromedex) — clinical-grade interaction
        // severity / dosing / screening depth. Same BYO pattern as
        // NCPDP / X12: the open ONC + SPL-derived DDI sets ship in-tree, the commercial-grade depth is
        // member-supplied via --accept-license and never shipped. No in-tree data and no consumer yet;
        // this entry reserves the gated install path for when a customer brings a license (e.g. Fusion's
        // CIPS already licenses Medi-Span). One package shape covers all three vendors.
        ["commercial-ddi"] = new("commercial-ddi", "byo", "Commercial drug-interaction knowledge base (Medi-Span / FDB / Micromedex) severity + dosing depth (member-licensed; not shipped)", "Medi-Span / FDB / Micromedex", "Commercial drug KB (member-licensed)"),
        ["census"] = new("census-demographics", "2022", "Census ACS county-level demographics", "US Census Bureau", "Public domain"),
        // First-party starter recipe pack (ARTIFACT_ECOSYSTEM_PROGRAM.md §5.1): .pidgeonrecipe
        // artifacts authored through the real export pipeline and embedded in Pidgeon.Data, so
        // install works offline/air-gapped. `data install` only materializes the files into the
        // data cache; the recipe trust gate stays the sole activation path — the user still runs
        // `pidgeon recipe install <file>` per artifact (install never activates). Every coded
        // value the pack carries is confined to the committed starter-recipes allowlist
        // (StarterRecipesAllowlistTests); 🟢 Pidgeon-derived provenance, no license gate.
        ["starter-recipes"] = new("starter-recipes", "1.0.0", "First-party starter recipes — vendor-profile + generation-config .pidgeonrecipe artifacts (activate each with pidgeon recipe install)", "Pidgeon Health", "Pidgeon-derived", EmbeddedResourcePrefix: "data.starter-recipes."),
        ["nhanes"] = new("nhanes-lab-distributions", "2021-2023", "NHANES lab value distributions by cohort", "CDC/NCHS", "Public domain"),
        ["cdc-wonder"] = new("cdc-wonder-prevalence", "2022", "CDC WONDER cause-of-death prevalence", "CDC WONDER", "Public domain"),
        // Semantic-layer open packages. Public CMS/CDC/FDA data —
        // codes / derived facts only, no gated content. Each package's data is produced by the ETL under
        // tooling/<pkg>/ from staged raw dumps (.data/, gitignored) and installed via
        // `pidgeon data install <pkg>`; it is never committed in-tree. The consuming loader + check land
        // with the sem-rules vertical that reads each.
        ["ncci"] = new("ncci-edits", "2026.1", "CMS NCCI PTP conflict pairs + MUE max-units (bare codes only; CPT descriptors join from the gated cpt package)", "CMS", "Public domain"),
        ["cdsi"] = new("cdsi-schedules", "2026.1", "CDC CDSi immunization schedules — recommended age windows, minimum dose intervals, contraindications", "CDC", "Public domain"),
        ["spl-derived"] = new("spl-drug-products", "2026.1", "openFDA SPL-derived per-NDC route / dosage-form / strength + Established Pharmacologic Class (EPC) drug-class spine", "FDA / NLM (openFDA)", "Public domain"),
        ["snomed-relationships"] = new("snomed-relationships", "2025.09", "SNOMED CT relationship data (ISA, finding site, morphology, causative agent)", "NLM/IHTSDO", "UMLS"),
        ["icd10-snomed-map"] = new("icd10-snomed-map", "2026.0.0", "ICD-10-CM to SNOMED CT concept mappings", "NLM/IHTSDO", "UMLS"),
        ["umls-icd10-snomed"] = new("umls-relationships", "2025AB", "ICD-10 to SNOMED CT mappings via UMLS", "NLM", "UMLS"),
        ["umls-condition-labs"] = new("umls-relationships", "2025AB", "Condition-to-lab associations via UMLS", "NLM", "UMLS"),
        ["umls-condition-meds"] = new("umls-relationships", "2025AB", "Condition-to-medication associations via UMLS", "NLM", "UMLS"),
        ["umls-condition-procs"] = new("umls-relationships", "2025AB", "Condition-to-procedure associations via UMLS", "NLM", "UMLS"),
        ["umls-comorbidities"] = new("umls-relationships", "2025AB", "Condition-to-condition comorbidities via UMLS", "NLM", "UMLS"),
        // CC0 FHIR IGs carry a DownloadUrl so `pidgeon data install` can auto-fetch them
        // from the HL7 registry when no local source extract is present. All four are the
        // CMS-0057-F-relevant guides (US Core for patient/provider access; PAS/CRD/DTR for prior auth).
        ["fhir-us-core-6.0"] = new("fhir-ig", "6.1.0", "US Core STU 6.1 FHIR Implementation Guide (USCDI v3)", "HL7 (hl7.org/fhir/us/core)", "CC0-1.0", "https://hl7.org/fhir/us/core/STU6.1/package.tgz"),
        // The CMS-0057-F REQUIRED baseline version (45 CFR 170.215 as adopted names US Core 3.1.1;
        // US Core 6.1 above is the permitted-updated-version ecosystem target). Same CC0 auto-fetch
        // path; installing this package makes `pidgeon conform` grade against the required baseline.
        ["fhir-us-core-3.1.1"] = new("fhir-ig", "3.1.1", "US Core STU 3.1.1 FHIR Implementation Guide (CMS-0057-F required baseline, 45 CFR 170.215 as adopted)", "HL7 (hl7.org/fhir/us/core)", "CC0-1.0", "https://hl7.org/fhir/us/core/STU3.1.1/package.tgz"),
        ["fhir-davinci-pas-2.1"] = new("fhir-ig", "2.1.0", "Da Vinci Prior Authorization Support 2.1 FHIR IG (CMS-0057-F)", "HL7 (hl7.org/fhir/us/davinci-pas)", "CC0-1.0", "https://hl7.org/fhir/us/davinci-pas/STU2.1/package.tgz"),
        ["fhir-davinci-crd-2.1"] = new("fhir-ig", "2.1.0", "Da Vinci Coverage Requirements Discovery 2.1 FHIR IG (CMS-0057-F)", "HL7 (hl7.org/fhir/us/davinci-crd)", "CC0-1.0", "https://hl7.org/fhir/us/davinci-crd/STU2.1/package.tgz"),
        ["fhir-davinci-dtr-2.0"] = new("fhir-ig", "2.0.1", "Da Vinci Documentation Templates and Rules 2.0 FHIR IG (CMS-0057-F)", "HL7 (hl7.org/fhir/us/davinci-dtr)", "CC0-1.0", "https://hl7.org/fhir/us/davinci-dtr/STU2/package.tgz"),
        // The base FHIR R4 (4.0.1) definitions package: every resource/datatype
        // StructureDefinition snapshot plus the spec's own ValueSets/CodeSystems.
        // Deliberately an explicit opt-in install (never a silent transitive fetch —
        // see FhirIgDependencyResolver.SkippedPackages): the embedded subset stubs
        // stay the offline-lean default, and installing this package unlocks the
        // structural generation tier (schema-derived minimal instances for the base
        // R4 types with no dedicated builder) plus full-definition base validation.
        ["fhir-r4-core"] = new("fhir-ig", "4.0.1", "FHIR R4 (4.0.1) core definitions — base StructureDefinitions, ValueSets, and CodeSystems for all resource types", "HL7 (hl7.org/fhir/R4)", "CC0-1.0", "https://packages.fhir.org/hl7.fhir.r4.core/4.0.1")
    };

    /// <summary>
    /// A named group of packages installable with one command. Catalog data kept here
    /// (not in <see cref="DataPackageManager"/>) so the manager stays logic-only.
    /// </summary>
    internal record BundleDefinition(string DisplayName, string Description, string License, string[] Packages);

    public static readonly Dictionary<string, BundleDefinition> Bundles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["clinical-intelligence"] = new(
            "Clinical Intelligence Pack",
            "Cross-terminology clinical relationships (ICD-10, SNOMED, LOINC, RxNorm, CPT)",
            "UMLS",
            new[] { "umls-icd10-snomed", "umls-condition-labs", "umls-condition-meds", "umls-condition-procs", "umls-comorbidities" })
    };

    /// <summary>
    /// Maps an <see cref="Entry.License"/> value to the human-readable license name the installer
    /// names when it refuses an un-accepted install. A package whose license is a key here is
    /// member-supplied: its data is never shipped in-tree or in the OSS repo, and
    /// <c>InstallPackageAsync</c> fails until <c>--accept-license</c> is passed. Catalog/policy data,
    /// kept here (not in <see cref="DataPackageManager"/>) so the manager stays logic-only.
    /// </summary>
    public static readonly Dictionary<string, string> LicensesRequiringAcceptance = new(StringComparer.Ordinal)
    {
        ["UMLS"] = "UMLS Metathesaurus License",
        ["AMA Developer License"] = "AMA CPT License",
        ["NCPDP SCRIPT (member-only)"] = "NCPDP membership / SCRIPT license",
        ["X12 5010 implementation guides (licensed)"] = "ASC X12 / WPC implementation-guide license",
        ["Commercial drug KB (member-licensed)"] = "a commercial drug knowledge base (Medi-Span / FDB / Micromedex) license",
    };
}
