# ADR-0034: Delphi shared workflow and desktop tab

- **Status:** Accepted
- **Date:** 2026-08-26
- **Domains:** architecture, data-pipeline, decision-engine, risk-management
- **Related:** ADR-0020, ADR-0023, ADR-0033

## Context

The immediate problem is adding Delphi to the TraderVI desktop shell without
duplicating its large command-line orchestration or accidentally running a
consequential evaluation. The parent problem is operating one authoritative
daily recommendation workflow from either a CLI or a visual surface. The root
goal is to tune Delphi from measured paper-trading outcomes while the user
keeps strategic control over when recommendations are produced and acted on.

Delphi reads the local market snapshot and registered model artifacts. An
official run also appends immutable calibration evidence and replaces the
selected recommendation date's operational picks and supporting records.
Merely opening a tab therefore cannot imply permission to run it.

## Decision

Expose Delphi through one host-neutral shared workflow and add a desktop tab
that is read-only until the user deliberately starts an official run.

### Confirmed direction

- Move Delphi orchestration out of `Delphi/Program.cs` into Core. Keep the
  Delphi console project as a thin, first-class adapter over that workflow.
- Make the default tab view load the latest persisted Continuation and
  Breakout recommendations. Loading or refreshing that view never evaluates
  symbols, contacts a broker, or changes the database.
- Run official Delphi only from an explicit control followed by a confirmation
  that describes its data, artifact, and SQL effects.
- Serialize Delphi evaluations within each host process and disable duplicate
  GUI starts while a run is active. Cross-process CLI/GUI overlap remains an
  operator responsibility in this slice.
- Return typed run results to hosts while allowing each host to present the
  same diagnostic output in its own way.
- Keep recommendation generation separate from paper-trade management.
  Delphi publishes the daily thesis; the paper controller later decides and
  records intraday buy, hold, and sell actions from those picks.

### Accepted first-slice defaults

1. The desktop action runs the `OfficialPaper` purpose with Delphi's existing
   capital, scan-limit, top-pick, and persistence defaults.
2. If recommendations already exist for the selected date, confirmation must
   state that the operational rows for that date will be replaced while a new
   immutable calibration run is appended.
3. The tab shows the latest saved recommendation date, save time, both lenses,
   and the result or diagnostics from a run started in that tab.
4. Exploratory replay remains available from the Delphi CLI only in this
   slice. It can be added to the GUI later with separate wording and controls.
5. Opening TraderVI never schedules Delphi, and this tab does not yet create
   paper positions automatically.

## Alternatives considered

- **Copy Delphi into a view model.** Rejected because CLI and GUI behavior,
  reports, and persistence safety would drift.
- **Launch the Delphi executable from WPF and scrape its output.** Rejected
  because text is a fragile integration contract and does not provide typed
  results or reliable in-process concurrency control.
- **Automatically run Delphi when the tab opens or once each morning.**
  Rejected because a tab view is not authorization to write calibration and
  recommendation records.
- **Make the first tab entirely read-only.** Rejected because an explicit,
  well-labelled run gives the operator one useful surface without weakening
  control.

## Consequences

- CLI and GUI now execute the same Delphi implementation.
- The latest published thesis can be inspected safely without re-evaluation.
- An official GUI run remains consequential and may replace same-date
  operational records; confirmation and visible status are required.
- TraderVI prevents ordinary window closure while its in-process official run
  is active. It cannot prevent an external process termination or a separately
  started Delphi CLI run.
- This slice does not choose entries or execute trades. Connecting picks to
  the paper controller remains a later, separately reviewed feature.
- Moving the orchestration into Core increases that assembly's application
  responsibility; if several large workflows accumulate, a dedicated shared
  application-workflows project should be considered.

## Review questions

1. What happens when the Delphi tab is opened or refreshed?
2. Why does starting an official run require a separate confirmation?
3. Which component turns Delphi's thesis into paper buy, hold, and sell
   decisions?
