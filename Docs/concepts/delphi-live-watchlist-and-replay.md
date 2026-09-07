# Delphi Live watchlist and historical replay

- **Status:** Implementation brief following the operator's 2026-09-06 request; historical execution assumptions are explicitly provisional.
- **Parent contract:** [Frozen Delphi Live V1](delphi-live.md), [ADR-0053](../adr/0053-delphi-live-v1.md).
- **Immediate problem:** the inactive screen hides the saved Delphi picks and exposes setup and experiments before users can understand their watchlist.
- **Parent problem:** make daily-to-intraday selection and simulated portfolio behavior understandable.
- **Root goal:** learn which daily candidates develop strength while preserving capital and causal evidence.

## Accepted direction

The user requested a simpler screen and a Friday, 2026-09-04 replay showing both changing signals/rankings and simulated trades/profit and loss. The replay uses the exact capital entered for the current System Shadow account snapshot, verified read-only on 2026-09-06 and retained in private local artifacts. This is an independent cash-only replay account, not a transfer or reset of the existing account. The accepted boundary is recorded in [ADR-0054](../adr/0054-delphi-live-preview-and-historical-replay.md).

The default view shows the latest saved published Delphi picks even when live monitoring is inactive. Display their recommendation date, source lenses/ranks and daily eligibility. A preview is not a frozen operational session; it must not make an old Friday list eligible for a later live session. Actual monitoring continues to use ADR-0053's session source cutoff and activation requirements.

Keep an obvious Watchlist surface, a historical simulated-account surface, and an Advanced area for existing activation, experiments, research and raw diagnostics. Row selection should explain the visible state in plain language. A replay checkpoint control must make its date/time and historical mode unmistakable.

## Verified Friday inputs

- Latest saved run is `Valid` / `OfficialPaper`, recommendation date September 4, market-data date September 3, durably created at 00:36:04 Toronto before Friday's 09:30 cutoff.
- Each lens published 25 rows, overlapping into 28 symbols. Published rows can have a non-Buy daily direction: 22 Continuation and 23 Breakout rows are eligible; the existing live eligibility filter admits 23 unique symbols.
- Show all 28 published symbols, with excluded daily candidates explicitly marked. Preserve the existing operational eligibility filter. Do not silently make all published rows Buy-eligible.
- For the 23 eligible symbols plus XIU, all 21 prior daily bars exist through September 3 and were recorded before Friday's open.
- Only 606 of the 1,872 expected five-minute slots for that 24-symbol set are already saved locally; three symbols have all 78 bars. Historical source acquisition must report exact coverage and retain missingness.
- There are no Delphi Live operational session records. The earlier nine-request source trial did not create any.

## Historical replay boundary

Replay is a separate, explicitly named research artifact. No writes to live sessions, policy assignments, trade ledgers, experiment cohorts or current Shadow portfolios. It cannot count toward engineering shakedown, promotion evidence, or actual live execution performance.

Reuse the frozen deterministic family, ranking, lifecycle, sizing and risk calculations. Historical five-minute bars are supplied progressively at their assumed two-minute publication offset. Preserve actual retrieval timestamps separately; simulated availability is an assumption, never proof of original on-time receipt. No calculation may read a later bar, baseline or daily run.

Provisional execution convention: obtain historical one-minute bars and estimate fills from the exact next minute's open after a simulated decision. Label each fill and every profit/loss total as estimated. No substitution with the signal bar or a later convenient interval. Missing execution evidence means no estimated fill. Quote-based protection must disclose its minute-bar proxy and sampling limitation. The run records zero commissions/spread/slippage unless a separately explicit cost model is provided; its gross results cannot be called achievable returns.

Keep actual fetch metadata and raw historical input bars in the ignored local replay artifact directory. The UI reads saved replay results without calling market services. Historical source requests are bounded to the selected session and symbol set; no training, migrations, broker operations or live activation are part of this work.

## Acceptance and implementation order

1. Read-only daily-picks projection, including inactive and non-session dates; preserve source attribution and eligibility.
2. Isolated historical data acquisition and deterministic replay with an independently identifiable estimated execution contract.
3. Focused tests for source cutoff, future-data exclusion, missing-bar handling, causal fills, cash/position limits and separation from operational storage.
4. Simpler WPF watchlist, saved replay timeline and simulated-account view; existing controls remain available under Advanced.
5. Run Friday's authorized replay, report coverage and limitations, and visually inspect the UI using an isolated markup renderer.

## Open and deferred

- Full replay equivalence to live bid/ask fills, provider latency and host gaps cannot be inferred from historical bars.
- Published-but-ineligible source rows expose a difference between the pure frozen-source selector's validation and the SQL selection path. Review that separately without changing live eligibility as a side effect of this UI work.
- Cost/slippage sensitivity, additional days, replay research calibration and operational observation without a paper portfolio are deferred.

## Review questions

1. Why should an inactive daily-picks preview remain separate from the session's frozen operational watchlist?
2. Why can historical prices support an estimated replay without proving live execution or clean collection coverage?
