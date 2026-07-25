# Pidgeon.Core

The community engine behind Pidgeon — a healthcare interoperability toolkit for generating,
validating, and de-identifying HL7 v2, FHIR R4, and NCPDP messages with synthetic data.
`Pidgeon.Core` is the shared library the Pidgeon CLI and desktop apps build on.

## What it does

- **Generate** synthetic HL7 v2 and FHIR R4 messages and resources.
- **Validate** messages against published standard definitions, in strict and compatibility
  modes.
- **De-identify** message content on-device, preserving referential integrity.

Validation is derived from the standards' published, machine-readable definitions, so the
generator and validator are checked against the same source rather than against each other.

Structural coverage is a snapshot of the engine's runtime capability, not a fixed guarantee:
broad HL7 v2 generation and validation across v2.3–v2.8, and FHIR R4 resource generation. Run
the CLI's `capabilities` command for the exact type × version matrix your build reports.

## Packages

The engine ships as two NuGet packages:

- `Pidgeon.Core` — the engine assembly.
- `Pidgeon.Data.Baseline` — the offline baseline resource set the engine reads.

> **Not yet published.** The public packages are not on NuGet yet; the package ids are
> reserved and the first packages ship with the public beta. Until then, build from source.

## Build from source

Requires the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build
dotnet test
```

## License

Pidgeon's community engine and baseline data package are licensed under the Mozilla Public
License 2.0 (see `LICENSE`). Third-party and standards acknowledgments are in `NOTICE`. HL7®
and FHIR® are registered trademarks of Health Level Seven International; their use here is
descriptive and does not constitute endorsement by HL7.

## Contributing & security

Contributions are welcome under the Developer Certificate of Origin — see
[`CONTRIBUTING.md`](CONTRIBUTING.md). To report a vulnerability, see [`SECURITY.md`](SECURITY.md).
