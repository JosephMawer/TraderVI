# ADR-0033: Tabbed desktop shell and shared application workflows

- **Status:** Accepted
- **Date:** 2026-08-26
- **Domains:** architecture, data-pipeline, risk-management
- **Related:** ADR-0017, ADR-0032
- **Extends:** ADR-0032's WPF dashboard decision.
- **Supersedes in part:** ADR-0032's five-second SQL display-refresh default;
  the accepted default is now thirty seconds.

## Context

The immediate problem is adding Hermes, Delphi, Athena, and Data Audit views to
TraderVI without losing their command-line interfaces or copying workflow logic
into WPF. The parent problem is giving consequential workflows consistent
behavior, progress, results, and safety boundaries across interactive and
headless hosts. The root goal is one understandable operating surface for the
system while preserving reproducibility and strategic human control.

The paper monitor already demonstrates the desired shape: Core owns monitoring
and policy behavior, while the CLI and WPF window are adapters. Data Audit is
the safest first additional tab because its existing workflow is read-only,
local, and makes no external market calls.

## Decision

Evolve `TraderVI.WPF` into a tabbed desktop shell. Expose each capability as a
host-neutral shared workflow returning structured results. Keep existing
console projects as first-class adapters over those same workflows.

### Confirmed direction

- Preserve Hermes, Delphi, Athena, Data Audit, and TraderVI command-line entry
  points for automation, recovery, diagnostics, and focused operation.
- Never copy a workflow into a view model and never launch a console executable
  behind a WPF tab as the normal integration mechanism.
- Shared workflows own orchestration and return structured results. Console
  hosts own textual formatting and exit codes; WPF owns presentation and user
  interaction.
- Each tab must state its external, database, artifact, and trading side
  effects before an operation begins.
- Consequential workflows remain deliberate actions with appropriate
  confirmation. Opening the shell must not run Hermes, Delphi, Athena, model
  training, or another database writer.
- Keep the paper controller, trade persistence, and dashboard display separate:
  the controller decides, the Trade Manager records, and the UI presents.

### Accepted first-slice defaults

1. Add Paper Trading and Data Audit tabs to the existing WPF startup window.
2. Extract a reusable `MarketDataAuditWorkflow` that loads the local snapshot
   and runs the deterministic auditor. Both DataAudit CLI and WPF call it.
3. Run Data Audit only from an explicit read-only button. Display summary
   counts and the structured finding list; make no database writes or external
   calls.
4. Refresh the paper dashboard from SQL every thirty seconds and immediately
   after a monitor or trade action. TMX collection and fifteen-minute policy
   evaluation remain independent of this display refresh.
5. Add other tabs incrementally after this shared-workflow boundary is proven;
   do not perform a broad rewrite of all console applications at once.

## Alternatives considered

- **Remove the console applications.** Rejected because headless operation,
  diagnostics, recovery, scripting, and focused build/run boundaries remain
  valuable.
- **Duplicate each `Program.cs` inside WPF.** Rejected because behavior and
  safety checks would drift between hosts.
- **Spawn console processes and scrape their output.** Rejected as the normal
  architecture because plain text is a fragile integration contract and does
  not expose typed progress, cancellation, or results.
- **Extract every workflow before showing the first tab.** Rejected because a
  read-only vertical slice can validate the boundary with much less risk.

## Consequences

- CLI and GUI behavior can evolve together around one implementation.
- Workflow services become independently testable without XAML or console I/O.
- Adding each later tab requires deliberate extraction from its current entry
  point, especially for long-running or mutating operations.
- The WPF shell is an operator surface, not a new scheduler or authorization
  bypass.
- Thirty-second SQL refresh reduces unnecessary reads without changing the
  five-minute evidence cadence or fifteen-minute decision cadence.

## Review questions

1. Which layer owns workflow behavior, and which layers own presentation?
2. Why does opening the WPF shell not automatically run Data Audit or Delphi?
3. Why should WPF not launch and scrape the console executable as its normal
   integration contract?

