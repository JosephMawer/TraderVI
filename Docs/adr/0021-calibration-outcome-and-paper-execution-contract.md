# ADR-0021: Calibration outcome and paper-execution contract

- **Status:** Accepted
- **Date:** 2026-08-23
- **Domains:** architecture, math-statistics, market-microstructure, risk-management
- **Related:** ADR-0002, ADR-0007, ADR-0015, ADR-0020
- **Refined by:** ADR-0023 for the primary swing-policy direction, ADR-0024 for coverage/cohort reporting, ADR-0025 for the initial three-session tradeable measure, and ADR-0028 for the delayed intraday paper-exit challenger

## Context

The immediate problem is to define reproducible prediction and tradeable outcomes. The parent problem is separating whether Delphi's forecasts were correct from whether published recommendations were executable and profitable. The root goal is an honest comparison of Continuation, Breakout, and portfolio policies without look-ahead or optimistic fill/path assumptions.

## Decision

Create immutable, versioned outcome definitions. Never rewrite an old outcome when timing, label, fill, cost, exit, benchmark, or ambiguity rules change.

### Prediction contract

Evaluate every calibration candidate at 1, 5, 10, and 20 completed sessions after the observation session. The 10-session model events are computed through the enabled `ILabeler` instances from `ProfitModelRegistry`, so evaluator and training semantics share production code without moving training orchestration into the evaluator. Persist labeler name and outcome-definition version.

The initial events are `BinaryUp10`, `BinaryDown10`, `BreakoutEnhanced`, and `VolExpansionRelative10`. XIU is the benchmark under ADR-0002. Continuation's primary economic metric is the official cohort's top-ranked mean 10-session **net excess return versus XIU**, guarded by lower-tail loss, drawdown, turnover, coverage, and cost. Breakout's primary prediction metric is the 10-session breakout-event Brier score and reliability; 10-session net excess return is a co-primary economic guardrail, because a correct threshold crossing can still be a bad trade.

### Entry and cost contract

Tradeable outcomes exist only for lens rows marked published by Delphi. Enter at the open of the first XIU trading session whose market open is later than both the observation session and the recorded run time. Thus a pre-open run normally enters that session; a run after the open waits until the following session. Session-open comparison uses `America/Toronto` and is persisted in the outcome definition.

If the symbol lacks a bar while XIU has the eligible session, wait up to three XIU sessions. A later fill is marked delayed; no available bar within that window produces `NoEntry`, not a zero return. Halts and gaps use the actual stored open—never a prior close. v1 uses a transparent conservative cost model of 25 basis points per side (10 bps slippage plus a 15 bps half-spread proxy) and zero fixed commission. Persist raw price, adjusted price, and each cost component. A later volume-aware model is a new definition.

### Exit, path, and portfolios

The first policy versions hold for ten completed sessions, apply the strategy version's hard stop, treat the warning threshold as diagnostic, use no profit target, perform no early rank rotation, ignore duplicate recommendations while a symbol is held, and fill vacancies from that day's published rank order. At the terminal session use the close. A stop fill is `min(session open, stop price)` so gaps cannot receive an impossible stop price.

With daily OHLC, if multiple exit barriers could have fired on the same session, use the least favourable feasible ordering and mark `PathAmbiguous`. The affected path-sensitive metric is reported separately/excluded where appropriate; it is never resolved optimistically. In v1 the only intrahorizon exit barrier is the stop, which reduces ambiguity, but the rule is part of the versioned contract for later targets/trailing stops.

Compare `Top1`, `Top3EqualWeight`, `Top5EqualWeight`, and `RankWeighted` where rank weight is `(N - rank + 1) / sum(1..N)`. Selection-quality portfolios allow fractional allocations and normalize each recommendation cohort to one unit. Capital-constrained portfolios use the capital/reserve recorded by the run, integer shares, the same costs, and never spend unavailable cash. Keep the two scoreboards separate.

Launch the evaluator initially as the separate local console application **Athena**, manually via `dotnet run --project Athena`. It reads only local SQL market/evidence data, performs no external calls, writes missing versioned outcomes idempotently, prints coverage/audit summaries, and can export versioned CSV. It is not embedded in Hermes or Delphi and is not automatically scheduled initially.

## Alternatives considered

- **Observation-close entry.** Rejected for tradeability because the close produced the prediction; retained only for label-aligned research outcomes.
- **Always use the next dated bar regardless of run time.** Rejected because a post-open Delphi run could not obtain that open.
- **Zero costs.** Rejected as an optimistic small-account TSX assumption; gross results remain available beside conservative net results.
- **Immediate daily rotation.** Deferred because it adds a second policy hypothesis before selector quality has a stable baseline.
- **Choose target before stop when both occur.** Rejected because daily OHLC cannot justify the favourable path.

## Consequences

- Prediction results reproduce training labels while economic results use executable timing.
- Late runs, gaps, halts, missing bars, and costs are visible rather than silently normalized away.
- Fixed-horizon policies are intentionally simpler than future Sentinel rotation; changing them creates new policy/outcome versions.
- Breakout is not judged solely by terminal return or solely by event accuracy.

## Review questions

1. Why can a label-aligned outcome start at the observation close while a tradeable outcome cannot?
2. What happens when Delphi runs after the next market open?
3. How do normalized and capital-constrained portfolios answer different questions?
4. Why is the Breakout event metric paired with an economic guardrail?
