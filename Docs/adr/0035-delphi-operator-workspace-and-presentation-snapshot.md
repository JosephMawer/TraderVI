# ADR-0035: Delphi operator workspace and presentation snapshot

- **Status:** Accepted
- **Date:** 2026-08-26
- **Domains:** architecture, data-pipeline, decision-engine
- **Related:** ADR-0020, ADR-0033, ADR-0034

## Context

The immediate problem is that Delphi's first desktop tab shows published picks
but hides most of the evidence that explains them: regime, advance/decline
breadth, sectors, US confirmation, market tape, Granville, universe exclusions,
relative-strength coverage, OBV, CLX, gates, and models. The parent problem is
making Delphi understandable without forcing the user to read one long console
transcript. The root goal is informed strategic control over how Delphi is
tuned and when its recommendations are accepted.

The shared workflow already produces a diagnostic report and a human summary,
but those strings alone are a poor GUI contract. `DailyPick` also cannot
reconstruct a run's full reasoning after the application restarts.

## Decision

Turn the Delphi view into a task-oriented operator workspace backed by a
versioned, typed presentation snapshot produced from the same facts already
given to `DelphiReportBuilder`.

### Confirmed direction

- Keep Delphi's calculation, ranking, gating, and persistence behavior
  unchanged. This feature presents existing evidence; it does not introduce a
  new signal or recompute a decision in WPF.
- Use nested views for Overview, Picks, Market, Granville, Diagnostics, and the
  Full Report. Do not create a separate tab for every individual indicator.
- Make Overview the default and keep the complete diagnostic text available
  for copying and detailed investigation.
- Return the typed snapshot with `DelphiWorkflowRunResult` so a GUI-started run
  can render it immediately without parsing console text.
- For persisted calibration runs, include the snapshot as a versioned child of
  the existing immutable `CalibrationRun.RunContextJson`. This adds no SQL
  object or migration.
- When opening the tab, anchor details to the newest OfficialPaper calibration
  run for the saved recommendation date that is no later than the saved picks.
  Never silently combine saved picks with a different or subsequent run.
- Support older runs with an explicitly labelled reconstruction from their
  existing calibration JSON and date-scoped local records. Missing historical
  facts remain unavailable; they are not replaced with current values.
- Loading any view remains read-only and does not launch Delphi.

### Accepted first-slice defaults

1. Overview shows the final recommendation, regime, breadth, Granville posture,
   sector participation, and the most important warnings.
2. Picks retains separate Continuation and Breakout tables.
3. Market shows A/D facts, sector rows, US confirming indices, market tape, and
   CLX.
4. Granville shows every emitted indicator with category, signal, points, and
   explanation, plus the aggregate counts and adjustment.
5. Diagnostics shows strategy/model identity, universe exclusions, freshness,
   RS/OBV coverage, and the winning pick's signals and gate pipeline.
6. Full Report shows Delphi's structured summary and diagnostic reports in a
   scrollable, copyable text surface.

## Alternatives considered

- **Show only the full console transcript.** Rejected because it preserves
  detail but makes the most important market relationships difficult to scan.
- **Parse report text into controls.** Rejected because whitespace and wording
  would become a fragile application API.
- **Recalculate the report in WPF from current tables.** Rejected because later
  data corrections could make the UI disagree with what Delphi knew at run
  time.
- **Add a new report table.** Deferred because the versioned immutable run
  context can hold the bounded presentation snapshot without a migration.

## Consequences

- The desktop surface exposes Delphi's reasoning at both summary and diagnostic
  depth.
- Report formatting and WPF presentation share facts but remain separate host
  responsibilities.
- Future Delphi data sources must update the report builder and presentation
  snapshot together under the existing mandatory reporting rule.
- New runs are fully reopenable. Legacy runs may show a reconstruction label
  and unavailable fields where the old evidence contract did not capture them.
- Snapshot payload growth is bounded to run-level presentation facts; full
  candidate evidence remains in its existing normalized/JSON records.

## Review questions

1. Why does WPF receive typed Delphi facts instead of parsing console text?
2. Which nested view answers “what should I do?” and which preserves every
   diagnostic detail?
3. Why may a legacy reconstructed view contain unavailable fields rather than
   substituting today's values?
