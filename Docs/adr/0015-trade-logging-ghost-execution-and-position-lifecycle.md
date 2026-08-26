# ADR-0015: Manual trade logging with ghost execution and position lifecycle

- **Status:** Accepted
- **Date:** 2026-06-01
- **Domains:** architecture, risk-management, market-microstructure
- **Related:** ADR-0007 (liquidity floor / capital preservation framing),
  ADR-0014 (Continuations lens — the executed pick whose trades we now record).
- **Refined by:** ADR-0028 for the delayed intraday paper-exit challenger; the existing
  operational position stop remains unchanged until a separately approved promotion.

## Context

The system produced daily recommendations (Delphi) but had **no way to record
the trades actually placed**. The author manually bought CS and BLDP on a day
when Delphi's single-position pick was MRE — so realized positions already
diverged from the model's hypothetical book, and that divergence was untracked.

Most persistence already existed but was unwired:

- `[dbo].[TradeLog]` + `TradeLogRepository` (insert, queries, `GetPnLSummary`).
- `[dbo].[ActivePosition]` + `ActivePositionRepository` (open, update, close).
- `TradeManager(bool ghost)` with empty `Buy`/`Sell` stubs and a `WSTrade`
  Wealthsimple client whose `PlaceOrder` needs a `security_id` (not the ticker).

The goal was a minimal, honest "log what I did" path now — with a fake
Wealthsimple call — that also lays groundwork for the planned **Sentinel**
stop-loss monitor, without building speculative infrastructure.

## Decision

**Wire `TradeManager` to log trades directly to the database and manage the
position lifecycle, with a ghost (simulated) execution path. Expose it through
a verb CLI in the `TraderVI` console program.**

1. **No broker abstraction (yet).** `TradeManager` calls the repositories
   directly. A clean `IBrokerClient` seam was explicitly deferred — log-first
   is enough until live routing is actually needed.
2. **Ghost execution is the default.** In ghost mode, `Buy`/`Sell` print a
   `[GHOST] Simulated Wealthsimple ...` line and update only the database — no
   network call. Live routing (`WSTrade.PlaceOrder`) is **not** wired because it
   requires resolving a Wealthsimple `security_id` from the ticker; non-ghost
   mode warns and still logs so the book stays accurate.
3. **A BUY opens a position with a stop.** Each buy inserts a `TradeLog` row
   **and** an `ActivePosition` with `StopLossPrice = entry × 0.90` (the −10%
   capital-preservation stop) and `WarningPrice = entry × 0.92` (−8% early
   warning for Sentinel). Re-buying a symbol that already has an open position
   is refused.
4. **A SELL realizes P&L and closes the position.** Sell looks up the open
   position, computes `RealizedPnL`, `RealizedPnLPct`, and `HoldingDays` from
   the position cost basis, inserts a SELL `TradeLog` row, and closes the
   position.
5. **Manual entry only.** Trades are **not** auto-stamped with Delphi's
   `DailyPick` snapshot (`EntryComposite` / `StrategyVersionId` / `OriginalPickId`
   stay null) — keeps the first cut simple; model-vs-override linkage is a
   later enhancement.

Commission defaults to 0 (Wealthsimple stock trades are commission-free).

## Alternatives considered

- **`IBrokerClient` abstraction with `FakeBrokerClient` + WS adapter now** —
  cleaner ghost/real swap and unit-testable, but premature; live routing is
  blocked on `security_id` resolution anyway. Deferred.
- **`TradeLog` only, no `Position` rows** — simpler, but throws away the stop
  price and open-position state Sentinel needs, forcing rework later.
- **Auto-link each fill to the latest `DailyPick`** — enables override-vs-model
  analysis, but adds coupling and lookup ambiguity (which lens? which date?).
  Deferred until the analysis is actually wanted.
- **Entry point in the WPF app** — heavier and not headless; the console matches
  `TraderVI`'s "execution program" role and is scriptable/testable.

## Consequences

- We can now record real fills (`buy`/`sell`), inspect open risk (`list`), and
  see realized performance (`pnl`) from the command line.
- Every open position carries a concrete −10% stop price, so **Sentinel** can be
  built directly on `GetActivePositions()` without schema changes.
- Because fills are not linked to `DailyPick`, model-vs-discretion attribution
  needs a future pass (re-link by symbol/date, or stamp at entry).
- Live Wealthsimple execution remains a stub; flipping `Ghost` to false logs but
  does not route until `security_id` lookup is added. Signs this was wrong:
  we need broker routing or model linkage sooner than expected, forcing the
  deferred seams in earlier than planned.

## Review questions

1. What two database rows does a single `buy` create, and what does each one
   capture?
2. How are `StopLossPrice` and `WarningPrice` derived, and which planned program
   consumes them?
3. Why is there no `IBrokerClient` abstraction, and what concretely blocks live
   Wealthsimple routing today?
4. Which Delphi-linkage fields are deliberately left null, and what analysis
   does that postpone?
