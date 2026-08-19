---
applyTo: "Sandbox/**"
---

# Sandbox Project — Copilot Instructions

> Read this **before** adding to or modifying anything under `Sandbox/`. It exists so
> you can get up to speed on the project's purpose and conventions without re-deriving
> them from the code each time.

## What the Sandbox project is

`Sandbox` is a **console app of one-shot exploratory scripts** called **probes**. A probe
hits a data source (a web API, the TMX GraphQL endpoint, or the local `TraderDB`) or runs a
**back-test / calibration** over historical bars, prints a human-readable result, and exits.

Probes are **not production code**. They are the project's lab bench:

- **Data-source reconnaissance** — "does TMX's historical query actually return multi-year
  sector history?" (`TmxSectorHistoryProbe`).
- **Threshold calibration / back-testing** — "what fire rate and forward-return hit rate do
  these Dullness thresholds produce on historical XIU bars?" (`DullnessCalibrationProbe`).
- **One-off data seeding / backfills** — "compute and persist cumulative OBV history for every
  symbol so Hermes has an anchor to continue from" (`ObvBackfillProbe`).

If a piece of logic will run **every day** or **as part of the pipeline**, it belongs in
**Hermes** (ingestion/maintenance), **Delphi** (evaluation), or **Core** (shared logic) — **not**
in Sandbox. Sandbox is for things you run **by hand, occasionally, on demand**.

## Why the probe pattern (and why it helps back-testing)

A back-test or calibration is throwaway *execution* but valuable *knowledge*. The probe pattern
keeps both without polluting the production programs:

- **Isolation** — calibration code never ships inside Delphi/Hermes, so the daily path stays lean
  and there is no dead "if backtestMode" branching in production.
- **Rerunnability** — every probe stays in the repo behind a stable slug, so months later you can
  re-run the exact experiment (re-calibrate a threshold, re-verify a data source) verbatim.
- **Auditability** — probes print their own assumptions, the window they ran over, and an explicit
  "exit signal" (what result means "do X vs. Y"). The decision rationale lives next to the code.
- **Reproducible raw data** — back-test probes typically also dump a per-row **CSV** to the working
  directory so the raw data can be re-analyzed in Excel/pandas without rerunning.

## How a probe is wired (the contract)

Every probe implements [`IProbe`](Probes/IProbe.cs):

```csharp
public interface IProbe
{
	string Slug { get; }        // command-line selector, e.g. "obv-backfill"
	string Description { get; } // one-line text shown in the usage banner
	Task RunAsync();            // does the work; prints its own output
}
```

The dispatcher is [`Program.cs`](Program.cs): a single `Probes` registry array and a slug lookup.

**Adding a probe = two edits, nothing more:**
1. Create `Probes/<Name>Probe.cs` implementing `IProbe` (sealed class, `Sandbox.Probes` namespace).
2. Add one `new <Name>Probe(),` entry to the `Probes` array in `Program.cs`.

Do **not** add per-probe `if/else` to `Main`, command-line parsing, or DI wiring — the registry +
slug lookup is the whole mechanism.

### Run a probe

```powershell
dotnet run --project Sandbox -- <slug>
dotnet run --project Sandbox            # no args → prints the list of probes
```

## Conventions to follow

- **One probe = one question.** Keep each probe focused on a single experiment, seed, or check.
- **Sealed class**, `Sandbox.Probes` namespace, file named `<Name>Probe.cs`.
- **Slug**: short, kebab-case, stable (it is a command-line contract — don't rename casually).
  Examples: `tmx-sector-history`, `dullness-calibrate`, `obv-backfill`.
- **Description**: one line, present-tense, states what it does and against what data.
- **Document the thesis in the XML `<summary>`**: what's under test, the assumptions, the window,
  and — for recon/calibration — the **exit signal** (what the result tells you to do next).
- **Prints its own output.** Probes are read by a human at the console; format for legibility
  (aligned columns, section headers). Use the existing probes as the style reference.
- **Reuse Core.** Get data through the same repositories the real programs use
  (`Core.Db.QuoteRepository`, `Core.Db.SymbolsRepository`, `Core.Db.*Repository`, `Core.TMX.TmxClient`,
  etc.). Don't hand-roll SQL or HTTP that Core already wraps.
- **Idempotent when it writes.** A seeding/backfill probe must be safe to re-run (use `MERGE`/upsert
  semantics already provided by the repository, e.g. `SymbolObvRepository.UpsertAsync`).
- **Match production policy when seeding.** If a backfill feeds a table that Hermes maintains, use
  the **same window and retention** Hermes uses (e.g. `Core.Constants.ObvRetentionMonths`, prune to
  the same cutoff) so the seeded series and the maintained series share one convention.
- **CSV for raw back-test data (optional).** When a calibration produces per-row data worth
  re-analyzing, dump a CSV to the working directory and name it in the summary.
- **No side effects beyond the stated job.** A recon probe reads; a backfill probe writes only the
  table it documents. Never let an exploratory probe mutate production state as a surprise.

## When NOT to use a probe

- It needs to run on a schedule or in the pipeline → **Hermes/Delphi**.
- The logic is reusable domain logic (an indicator, a calculator, a repository) → **Core**.
- It's an automated assertion about correctness → a **unit test**, not a probe.

Probes are deliberately manual, occasional, and self-describing. Keep them that way.
