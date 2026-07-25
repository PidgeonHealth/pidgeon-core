# Pidgeon.Core

The Pidgeon healthcare-interoperability engine: deterministic HL7 v2 / FHIR R4 / NCPDP SCRIPT
generation and validation, lookup/reference, and de-identification — all local, all offline,
no PHI required or produced.

- **Generation is seed-deterministic**: the same seed and options produce byte-identical output.
- **Validation is spec-derived**: validators are driven by machine-readable standard definitions,
  not hand-coded against the generator.
- **Data flows through explicit packages.** `Pidgeon.Core` alone carries no standards data:
  install `Pidgeon.Data.Baseline` (or supply your own package directory) and register it with
  `AddPidgeonBaselineData()`. Absent data degrades honestly — typed `PACKAGE_REQUIRED` results
  and structurally valid, hollow output; never fabricated content, never a crash.
- **Composition is explicit.** Register the capabilities you use:
  `AddPidgeonHl7Validation()`, `AddPidgeonFhirValidation()`, `AddPidgeonHl7Reference()`,
  `AddPidgeonHl7Generation()`, `AddPidgeonFhirGeneration()`. NCPDP SCRIPT generation is a
  separate opt-in (`AddPidgeonNcpdpGeneration()`) and stays closed until the member-supplied
  `ncpdp-script` package is installed.

License: MPL-2.0. Source and issues: the PidgeonHealth public repository.
