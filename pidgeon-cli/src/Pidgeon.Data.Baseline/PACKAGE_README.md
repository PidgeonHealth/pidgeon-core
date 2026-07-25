# Pidgeon.Data.Baseline

The offline baseline data package for `Pidgeon.Core`: the provenance-cleared resource set that
makes the engine genuinely useful out of the box. Register it with `AddPidgeonBaselineData()`.

Contents (every resource is an exact, digest-carrying manifest entry):

- **HL7 v2.3–v2.8 structural definitions** — Pidgeon-derived reference JSON (trigger events,
  segments, data types, coded tables) powering generation, validation, and lookup across all
  eight versions. See NOTICE.md for the HL7 acknowledgment.
- **Demographics tables** — names, streets, cities, states, ZIP codes, phone patterns
  (US public-domain ancestry, first-party ETL) so generated patients look real.
- **Clinical realism corpus** — first-party reference intervals, lab correlations, and
  narrative-note templates (public epidemiology cited in-file).
- **FHIR R4 profile set** — the CC0 base StructureDefinitions plus the US Core subset used by
  offline FHIR validation.

`Pidgeon.Core` works without this package (typed `PACKAGE_REQUIRED` degradation); with it,
generation output is deterministic **and** clinically coherent. The commercial product carries
a far larger managed corpus (vendor profiles, terminology depth, scenario packs) — this
baseline is a deliberately partial, self-sufficient sample.

License: MPL-2.0 (package); data provenance per resource in the repository's
standards-licensing register.
