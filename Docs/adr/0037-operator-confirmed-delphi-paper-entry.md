# ADR-0037: Operator-confirmed Delphi pick to paper position

- **Status:** Accepted
- **Date:** 2026-08-27
- **Domains:** architecture, market-microstructure, risk-management
- **Related:** ADR-0013, ADR-0015, ADR-0029, ADR-0032, ADR-0035

## Context

The immediate problem is that an operator can inspect saved Continuation and
Breakout picks in TraderVI but cannot turn a selected pick into a monitored
paper position without leaving the WPF application. The parent problem is
creating useful symbiosis between Delphi's daily recommendations and the
intraday paper controller. The root goal is to collect faithful, attributable
paper-trading evidence under human control without adding broker execution or
allowing a display action to invent a fill.

The ADR-0029 pilot command is intentionally narrow: today's Continuation lens,
one share, and a TMX-observed entry. It does not represent an operator mirroring
an actual fill with an arbitrary share count. A plain manual ghost buy does not
preserve the selected Delphi `PickId`, so it is not sufficient for this path.

## Decision

Add one shared `PaperTradeEntryWorkflow` used by WPF and the retained CLI.

1. The operator selects a persisted Continuation or Breakout row and supplies a
   positive whole-share count plus the actual per-share fill price.
2. WPF displays the symbol, lens, rank, recommendation date, shares, fill, and
   book cost in a confirmation dialog that explicitly says no broker order can
   be placed.
3. The workflow reloads the selected `PickId`, requires a Buy direction, rejects
   an already-active symbol, and opens a ghost position linked to that exact
   pick. It does not call TMX or infer an execution price.
4. Continuation is labelled the production lens. A manually selected Breakout
   pick is allowed but is labelled exploratory in both the confirmation and
   durable notes.
5. The BUY trade is attached to the new position so entry and later exit rows
   share one lifecycle. The Paper Trading tab refreshes immediately after a
   successful entry.
6. Operator account allocation and total account balance are not persisted;
   they are not required for position monitoring and would add unrelated
   private account context to the calibration ledger.

## Alternatives considered

- **Automatically paper-buy every Delphi pick.** Rejected because it removes
  operator control and confounds deliberate real-fill mirrors with systematic
  shadow-portfolio experiments.
- **Fetch the fill from TMX.** Rejected for operator-reported positions because
  a later quote is not the brokerage execution price.
- **Allow arbitrary symbols without a saved pick.** Rejected for this UI path
  because the monitor and evidence views depend on Delphi provenance. The
  retained generic ghost command remains separate.
- **Restrict the selector to Continuation.** Rejected because a confirmed
  Breakout challenger can be useful evidence when its exploratory status is
  explicit and it remains separate from production activation.

## Consequences

- Saved Delphi recommendations can now flow into the paper controller without
  rerunning Delphi or leaving TraderVI.
- User-entered fills are auditable but depend on accurate operator input.
- Breakout entries must remain separately identifiable in future scorecards.
- This creates SQL trade and position records only; it adds no broker route,
  schema migration, model change, or automatic Delphi activation.

## Review questions

1. Why must operator-entered paper positions retain the exact `PickId`?
2. Why does this workflow require an explicit fill instead of fetching TMX?
3. How are Breakout selections distinguished from production Continuation entries?
