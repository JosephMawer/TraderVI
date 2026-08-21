# Sandbox Agent Guide

These instructions apply to everything under `Sandbox/` in addition to the repository-root `AGENTS.md`.

## Purpose

Sandbox is a console application of manually selected, one-shot exploratory scripts called probes. A probe may inspect an external data source, calibrate or backtest a hypothesis, or perform an explicitly requested seed/backfill operation.

Sandbox is the project lab bench, not a production pipeline:

- Scheduled or daily work belongs in Hermes or Delphi.
- Reusable domain logic belongs in Core.
- Automated correctness assertions belong in `TraderVI.Core.Tests`.
- One-off reconnaissance, calibration, and controlled backfills belong here.

## Probe contract

Every probe implements `Sandbox.Probes.IProbe`:

- `Slug`: stable, short, kebab-case command selector.
- `Description`: one-line statement of what the probe does and against which data.
- `RunAsync()`: performs the work and prints its own human-readable output.

To add a probe:

1. Create `Probes/<Name>Probe.cs` as a sealed class in `Sandbox.Probes` implementing `IProbe`.
2. Add one `new <Name>Probe(),` entry to the registry in `Sandbox/Program.cs`.

Do not add probe-specific dispatch branches, a second command parser, or dependency-injection wiring.

## Conventions

- One probe answers one question or performs one explicitly bounded maintenance job.
- Document the thesis, assumptions, time window, side effects, and exit signal in the class XML summary.
- Reuse Core repositories and clients rather than duplicating SQL or HTTP logic.
- Backfill/seed probes must be idempotent and must use the same windows and retention policy as the production process they support.
- Reconnaissance probes read only. A writing probe may mutate only the table or artifact named in its documentation.
- Calibration probes should print assumptions and results; emit a CSV only when row-level output is valuable for later analysis.
- Never run a probe as routine validation. State its external and database effects and obtain explicit authorization first.

## Invocation

```powershell
dotnet run --project Sandbox -- <slug>
dotnet run --project Sandbox
```

The no-argument form prints the available probe list and does not run a probe.
