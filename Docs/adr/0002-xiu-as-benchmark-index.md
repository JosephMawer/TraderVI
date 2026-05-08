# ADR-0002: XIU as the system benchmark index

- **Status:** Accepted
- **Date:** 2026-05-07 *(backfilled)*
- **Domains:** finance-fundamentals, decision-engine

## Context
TraderVI trades the TSX. Several subsystems need a single "the market" proxy:
- The market regime filter (require benchmark uptrend before going long).
- Plurality indicators (#1–#4) compare breadth against benchmark direction.
- Leadership indicators reference benchmark moves.
- Future Weighting indicators (#15–#16) need an index whose constituents
  we can decompose.

The TSX has multiple plausible benchmarks: XIU (iShares S&P/TSX 60),
XIC (broader Composite ETF), or the raw S&P/TSX Composite index.

## Decision
Use **XIU** (iShares S&P/TSX 60 ETF) as the single benchmark across the
entire system. Store its daily close on `GranvilleMarketContext` and reuse
it from every group that needs "the market."

## Alternatives considered
- **S&P/TSX Composite (raw index).** Rejected: not directly tradable, and
  TMX import path is less reliable than ETF data.
- **XIC (Composite ETF).** Rejected for *now*: broader (~240 names) but the
  S&P/TSX 60 captures the high-volume, high-liquidity core of the TSX where
  our momentum strategy actually operates. Revisit if we expand to small-caps.
- **Multi-benchmark (different index per subsystem).** Rejected: introduces
  inconsistency — the regime filter and the Granville signals could disagree
  about whether the market is up.

## Consequences
- One ETF symbol to maintain in Hermes; one column on the daily context.
- All Granville indicators that reference "the market" implicitly inherit
  the S&P/TSX 60 lens, including its bias toward financials/energy.
- Sector concentration in the S&P/TSX 60 (banks + energy ≈ 50%) means our
  benchmark is structurally less diversified than e.g. the S&P 500. Worth
  noting for future risk-management decisions.
- When implementing Weighting indicators (#15–#16), "constituents" means
  S&P/TSX 60 names — we are committed to maintaining a constituent list.

## Review questions
1. Why XIU and not the raw S&P/TSX Composite or XIC?
2. What does the TSX 60's sector concentration imply about our regime filter?
3. What would force us to revisit this decision (i.e., what change to the
   strategy would make XIU the wrong benchmark)?
