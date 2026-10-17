---
cssclasses:
  - executive-brief
---
# Progressive profit trailing — design draft

**October 17, 2026. Draft, not an accepted operating policy or implementation authorization.**
Domains: risk-management, decision-engine.

A trailing stop follows a stock's highest observed price. Its gap is the percentage pullback
allowed below that high. With a fixed gap, the stop rises as the stock rises, but the same
percentage pullback remains allowed. The operator confirmed a stronger direction: **as profit
since purchase grows, that percentage gap should shrink**. No numerical schedule is chosen.

This would give an individual winner less room to surrender profit as it becomes more profitable.
It also makes an ordinary pullback more likely to end ownership before a later advance.
The operator explicitly prioritizes protecting profit over those later gains; missing a subsequent
advance does not by itself make a profitable protective exit unsuccessful.
The [saved DSV/ARE comparison](../reviews/stock-gain-paths-20261017.md#dsv-versus-a-winner-that-kept-climbing)
shows both concerns. It does not establish a setting that improves either trade or the portfolio.

The day's price move and profit since purchase remain separate. A recovery today can still be
below purchase cost; a stock bought later starts its own history at purchase. Tightening one
winner's stop does not decide whether to make that later purchase.

Existing protection must never be loosened or moved under a competing owner. Activation, profit
bands or curve, smallest gap, price-update timing and the precise high reference remain open.
Current rules and saved histories stay unchanged. [ADR-0090](../adr/0090-progressive-individual-profit-trailing.md)
records confirmed direction and the boundary of the proposal.

## Review questions

1. Why does a stop moving upward not necessarily mean its percentage gap is tightening?
2. What can be lost by protecting a larger share of a winner's profit?
