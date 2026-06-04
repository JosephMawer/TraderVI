# ADR-0009: Exclude leveraged/inverse ETPs from Delphi's ranking universe

- **Status:** Accepted
- **Date:** 2025-11-21
- **Domains:** decision-engine, data-pipeline, risk-management, market-microstructure

## Context

A fresh Delphi run ranked **NRGU** (`BetaProS&PTSX Engy2x`) as the #1 pick.
NRGU is a **leveraged ETP (Exchange-Traded Product)** — specifically, BetaPro's
S&P/TSX Capped Energy 2× Daily Bull ETF: a fund engineered to deliver **2× the
daily return** of an underlying sector index, with the leverage **reset every
trading day**.

This is structurally different from a common stock in three ways that matter
for our system:

1. **Volatility decay (daily compounding loss)** — Because leverage is reset
   each session, an LETF (leveraged ETF) held across a flat-but-choppy index
   loses value over time. The ML models (BinaryUp10, BinaryDown10,
   BreakoutEnhanced, etc.) were trained on common-stock return distributions
   that do **not** exhibit this path-dependence. The signals are therefore
   **out-of-distribution** for LETFs.
2. **Sector-beta, not single-name edge** — NRGU is a 2× bet on TSX energy.
   Our ranking pipeline assumes single-name idiosyncratic edge layered onto a
   market-regime context. Ranking LETFs alongside common stocks blends those
   two regimes and contaminates the leaderboard.
3. **Liquidity quality is worse than raw volume suggests** — Bid/ask spreads
   and underlying-derivative slippage on LETFs are structurally wider than
   common stocks at the same share-volume level.

An audit of `dbo.Symbols WHERE SecurityType = 'Stock'` surfaced **65 candidate
rows** matching leveraged/inverse naming patterns (BetaPro, MegaLong/MegaShort
3×, SavvyLong/SavvyShort 2×, LFG Daily 2×, etc.). The data source classifies
them as `Stock`, so the existing `SecurityType = 'Stock'` filter in
`GetEquitiesAsync` could not exclude them.

## Decision

**Add a dedicated `IsLeveragedOrInverseEtp BIT` column to `dbo.Symbols`** and
exclude flagged rows from `GetEquitiesAsync`. Keep `SecurityType` untouched
so other consumers (Hermes import, Hercules training-data labeling, future
ETF-aware code paths) see the original source classification unchanged.

Concretely:

1. **Schema** — `dbo.Symbols.IsLeveragedOrInverseEtp BIT NOT NULL DEFAULT(0)`.
2. **Data** — Flag the curated list of 62 leveraged/inverse ETPs identified
   in the audit (BetaPro/BtaPro 2×/-2×/-3×, MegaLong/MegaShort 3×,
   SavvyLong/SavvyShort/SavvyLg/SavvyLng 2×, LFG Daily 2×, BetaPro daily
   inverse). False positives (`SBR`, `SVB` — mining companies whose names
   contain "Bear"/"Bull"; `VALT` — unleveraged gold-bullion ETF named with
   "Bullion") were manually un-flagged.
3. **Query** — `GetEquitiesAsync` adds `AND IsLeveragedOrInverseEtp = 0` and
   now also projects `ShortName` so downstream defense-in-depth checks can
   reason about the human-readable name.
4. **Defense-in-depth** — A Delphi-side `IsLeveragedOrInverseByName` guard
   inspects `ShortName` against a marker list (`2x`, `3x`, `(2X)`, `(3X)`,
   `BetaPro`, `BtaPro`, `MegaLong`, `MegaShort`, `SavvyLong`/`SavvyShort`/
   `SavvyLg`/`SavvyLng`/`SavvyShrt`, `LFG Daily`, `Inverse`/`Invrs`,
   `Leveraged`, `DlyBl`, `DlyBr`, `DailyInvrs`) so any **future un-flagged
   import** is still rejected at runtime and counted.
5. **Reporting** — `DelphiReportBuilder` gains a `SkippedLeveragedEtp`
   counter shown in both the diagnostic Universe block and the human-readable
   summary line.

## Alternatives considered

- **Reclassify `SecurityType` from `Stock` to `LeveragedETF`.**
  Simpler — `GetEquitiesAsync`'s existing `= 'Stock'` filter would do the job
  with no schema change. *Rejected* because it would overwrite source-of-truth
  import data and silently break any other consumer that relies on the
  original classification. A dedicated flag is reversible, additive, and
  explicit about *why* the row is excluded.

- **Delete leveraged ETP rows from `dbo.Symbols`.**
  Removes the noise entirely. *Rejected* — these rows still need quotes for
  other potential uses (e.g., reading sector sentiment from LETF flows in a
  future feature) and removing them loses provenance.

- **Pure runtime keyword filter in Delphi only (no DB column).**
  No schema change. *Rejected* — relying on fuzzy keyword matching as the
  *primary* gate is brittle (e.g., `SBR` Silver Bear Resources is a mining
  company, not an inverse ETP). The DB flag is the authoritative gate; the
  keyword guard is only the second line of defense for new imports.

- **Train a separate ML model for LETFs.**
  Eventually possible. *Rejected for now* — the daily-reset path dependency
  makes momentum/breakout signals fundamentally unreliable on LETFs, and the
  universe is far too small (~62 names) to support its own model. The
  business value is not there yet.

## Consequences

- The Delphi ranking universe contracts by ~62 instruments. Top picks are now
  restricted to common stocks where ML signals are in-distribution.
- `NRGU`-style structural outliers will no longer surface in `TopPicks`.
- New leveraged ETPs imported in the future will not be silently included:
  - If `IsLeveragedOrInverseEtp = 1` at import time → excluded by the DB query.
  - If un-flagged but the name contains a known marker → excluded by the
	runtime guard and counted under `SkippedLeveragedEtp`.
  - If un-flagged **and** name doesn't match any marker → it will pass, which
	is a known residual risk (see "review questions").
- ETF rows that are *not* leveraged/inverse (plain index ETFs, covered-call
  ETFs, bond ETFs) remain a separate question — they are still in
  `dbo.Symbols`, still marked `Stock` in many cases, and still excluded by
  the `SecurityType = 'Stock'` filter only when their classification is
  correct. Cleaning that up is a follow-on data-quality task, not part of
  this ADR.
- This decision is **reversible**: setting `IsLeveragedOrInverseEtp = 0`
  for any row re-admits it to the universe with no code change.

## Review questions

1. Why is a *separate column* on `dbo.Symbols` preferred over reclassifying
   `SecurityType`?
2. What is "volatility decay" / daily-reset path dependency, and why does it
   make ML probabilities trained on common stocks unreliable for LETFs?
3. The defense-in-depth keyword guard could in principle false-positive on a
   regular stock with "Bear"/"Bull" in its name. Why is that acceptable here
   (and what did we do about the cases we found)?
4. If a brand-new BetaPro product is imported tomorrow and arrives with the
   flag unset, what catches it — and what residual risk remains if the
   marker list doesn't cover its naming convention?
