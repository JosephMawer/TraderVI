# Design Rules (TraderVI)

> This file is referenced by `.github/copilot-instructions.md`.
> Rules here are authoritative for code generation.

## Rule-Based vs ML-Based Components

### Rule-Based (non-ML) — DO NOT feed into feature vectors

| Component | Purpose | Where |
|-----------|---------|-------|
| **Advance-Decline Line** | Market breadth / regime gate | `AdvanceDeclineCalculator` → `BreadthScore` |
| **Granville 56 Indicators** | Market condition scoring / soft gate + composite adjustment | `Core.Indicators.Granville.*` → `GranvilleComposite` → `GranvilleGate` |
| **XIU Regime Filter** | TSX benchmark uptrend / 20d return gate | `TradeDecisionEngine.ComputeRegime` |
| **SPY Regime Filter** | S&P 500 cross-market confirmation | `TradeDecisionEngine.ComputeRegime` |
| **Stop-Loss (-10%)** | Hard exit override | `TradeDecisionEngine` / Sentinel (planned) |
| **Drawdown Warning (-5%)** | Alert / tighter monitoring | Sentinel (planned) |
| **Down-Probability Veto** | Block longs when P(down) ≥ 20% | `AggregateAllSignals` |
| **Down-Probability Penalty** | P(down) reduces composite score continuously | `GetCompositeScoreWithBreakdown` |
| **Rotation Threshold** | Prevent churn; require sufficient edge delta | `PositionSizer` / future Sentinel |
| **Pattern Confirmation** | Require at least 1 pattern Buy when patterns exist | `AggregateAllSignals` |

These components act as **gates or modifiers** on the ML-driven ranking. They are never encoded as ML features unless explicitly revisited.

### Granville's 56 Day-to-Day Indicators

Market-level rule-based indicators from Granville's "A Strategy of Daily Stock Market Timing."
Each category is implemented as an `IGranvilleIndicatorGroup` in `Core/Indicators/Granville/`.

| Category | Indicators | Status | Implementation |
|----------|-----------|--------|----------------|
| **Plurality** | #1–#4 | ✅ Active | `PluralityIndicators.cs` |
| **Disparity** | #5–#6 | ✅ Active | `DisparityIndicators.cs` |
| Leadership | — | 🔲 Planned | — |
| Features | — | 🔲 Planned | — |
| Weighting | — | 🔲 Planned | — |
| Genuity | — | 🔲 Planned | — |
| Dullness | — | 🔲 Planned | — |
| Overdueness | — | 🔲 Planned | — |
| Light Volume | — | 🔲 Planned | — |
| Heavy Volume | — | 🔲 Planned | — |
| Reversals | — | 🔲 Planned | — |
| Gold Indicator | — | 🔲 Planned | — |
| 3-Day Rule | — | 🔲 Planned | — |
| Churning | — | 🔲 Planned | — |
| News | — | 🔲 Planned | — |
| Erratic Price Movement | — | 🔲 Planned | — |
| General Motors Indicator | — | 🔲 Planned | — |
| The Closing | — | 🔲 Planned | — |
| Odd Lots | — | 🔲 Planned | — |
| Rebounds and Declines | — | 🔲 Planned | — |
| Highs and Lows | — | 🔲 Planned | — |

### Disparity adaptation for TSX

Granville's original Disparity logic compares **transportation average vs industrial average**.
That mapping does not transfer cleanly to the TSX because there is no dedicated TSX transportation
sub-index equivalent to the Dow Transports.

**TraderVI adaptation**:
- Use a **cyclical basket** of TSX sector indices: **Energy + Industrials + Materials**
- Compare that basket to **XIU** as the benchmark proxy
- Evaluate on:
  - **1-day percent change**
  - **5-day rolling return**

**Signals**:
- **Disparity #5**: cyclical basket underperforms `XIU` → near-term decline signal
- **Disparity #6**: cyclical basket outperforms `XIU` → near-term advance signal

**Important implementation note**:
- `XIU` is used for now instead of the raw TSX 60 index symbol because `XIU` is already stored and
  used throughout the system. This is a deliberate simplification and should be documented in code comments.
- Basket weighting is currently **equal-weighted**. This may later be upgraded to market-cap weighting.

### TSX sector map rules

Sector membership is **not** inferred from a TMX "index membership" endpoint.
Instead, TraderVI owns an internal stock → sector-index mapping:

1. Pull stock metadata from `TmxClient.GetQuoteDetailAsync()`
2. Read TMX `sector` / `industry`
3. Normalize the sector string through `TsxSectorMap`
4. Store the result in `[dbo].[StockSectorMap]`

This mapping is authoritative for app logic such as:
- sector-relative strength
- sector concentration checks
- future Granville leadership/weighting work
- fallback sector aggregation if TMX index symbols change

Hermes refreshes the stock-sector map on a **staleness schedule** rather than every daily run.

Where Granville_adjustment ∈ [−0.10, +0.10], derived from net Granville points normalized
across all implemented indicator groups. Currently **Plurality (#1–#4) and Disparity (#5–#6)** contribute.