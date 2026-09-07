# ADR-0054: Delphi Live preview and historical replay

- **Status:** Accepted for preview and exploratory replay; execution estimates are provisional
- **Date:** 2026-09-06
- **Domains:** architecture, data-pipeline, market-microstructure

## Context

The inactive Delphi Live screen was difficult to understand and did not show the latest daily picks.
The operator expected to follow those picks and requested a replay of Friday, September 4, showing both
changing signals and simulated trades/profit and loss. They requested the saved System Shadow account
balance as the independent replay's starting cash. The parent problem is explaining daily selection
and intraday decisions; the root goal remains understandable TSX momentum advice with causal evidence
and explicit risk controls.

Historical prices do not establish when a provider originally published them, what executable bid/ask
was available, or whether the host was healthy. The frozen [V1 source](../concepts/delphi-live.md) and
[ADR-0053](0053-delphi-live-v1.md) remain unchanged.

## Decision

1. Show the latest valid saved OfficialPaper run in a read-only daily preview when no live observations
   are available. Combine overlapping published lens picks, retain each source rank, display the source
   date, and identify published rows that fail daily entry gates. Previewing a stale list does not make
   it eligible for a later operational session.
2. Present a Watchlist, a historical Replay account, and Advanced controls. Preserve existing activation,
   experiments and diagnostics under Advanced, sharing the same operational view model. Reading a saved
   replay is local file I/O; it neither starts monitoring nor fetches market data.
   The common Portfolios overview also displays saved Delphi Live operational accounts, including
   queued and ended generations. Show an explicit inactive placeholder before activation, keep daily
   Shadow controls scoped to daily accounts, and keep historical replay artifacts outside that ledger list.
3. Use a SELECT-only projection and an explicitly invoked, bounded acquisition probe for the requested
   September 4 replay. Keep actual historical bars, retrieval times, daily inputs, and results in ignored
   local artifacts. Reuse completed cached symbol requests; fail on invalid bars instead of repairing them.
4. Reuse V1's deterministic signal, ranking, lifecycle, sizing and risk functions inside an isolated
   calculation. Progressively expose five-minute bars at an **assumed** completion-plus-two-minute time.
   Supply only pre-session daily inputs and a valid official run saved by that session's opening cutoff.
5. Name the exploratory execution convention `EstimatedNextMinuteOpenV1`: completed minute closes proxy
   bids for protection; a decision may fill at the exact next minute's open. Missing buy execution evidence
   means no fill; pending protective sells can retry at later exact minutes, retaining failed attempts.
   Exclude commissions, spread and slippage explicitly. Do not force a closing liquidation; value remaining
   holdings only when their exact checkpoint marks exist. Label fills, account values and P/L as estimates.
6. Do not write operational receipts, portfolio generations, assignments, ledgers, training inputs,
   calibration outcomes, or clean-cohort records. This replay does not count toward shakedown or promotion.
   Keep estimated trade recording times so the UI cannot reveal later fills or failures at an earlier frame.

## Alternatives considered

- **Leave the default screen empty until activation:** preserves the operational boundary but does not
  explain the relationship to the existing daily picks.
- **Backfill Friday into live ledgers:** would fabricate historical collection and execution provenance.
- **Use five-minute signal closes as fills:** uses a price already past when the decision becomes known.
- **Claim execution equivalence from minute bars:** cannot recover quote spread, latency or intraminute
  protection. The separate estimated convention is deliberately narrower.

## Consequences

The operator can inspect the daily source and learn the intraday rules without activating a portfolio.
The exploratory account is useful for explanation, but missing intervals and optimistic costs limit its
performance interpretation. Any later cost model, additional replay scope, or operational equivalence
claim needs its own explicit contract and validation. No live threshold or eligibility change is made.

See the [implementation brief](../concepts/delphi-live-watchlist-and-replay.md) and
[first replay review](../reviews/delphi-live-friday-replay-20260906.md).

## Review questions

1. Why can the latest daily preview show Friday picks on Sunday without authorizing Tuesday entries?
2. Which historical assumptions prevent these results from counting as clean live evidence?
3. Why must an early replay frame hide even a later failed execution attempt?
