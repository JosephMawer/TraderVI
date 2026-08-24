# ADR-0026: Three-session swing excursion measures

- **Status:** Accepted
- **Date:** 2026-08-23
- **Domains:** architecture, math-statistics, market-microstructure, risk-management
- **Related:** ADR-0021, ADR-0023, ADR-0025

## Context

The immediate problem is to measure how far a published Delphi pick moves in the favourable and adverse directions after entry. The parent problem is learning what profit-protection and stop behavior might be appropriate for the primary swing policy. The root goal is to tune Delphi and its future trade-management policy from auditable evidence without inventing precision that daily OHLC bars do not contain.

**Maximum favourable excursion (MFE)** is the largest unrealized gain reached during a measured path. **Maximum adverse excursion (MAE)** is the largest unrealized loss reached during that path. They reveal opportunity and risk that a closing return can hide. A stock can, for example, rise materially intraday and still close flat.

Daily OHLC data provides each session's open, high, low, and close, but not the order or exact time of the high and low. Excursion values are therefore observable price bounds, not executable fills or proof that a target and stop would have occurred in a particular order.

## Decision

Add the immutable tradeable definition `SwingExcursion3` version 1. Keep it separate from `SwingMarkToMarket3` so ADR-0025's accepted definition and stored shape never need to be rewritten. Apply the same published-candidate population, next-eligible-open entry, delayed-entry allowance, XIU alignment, `Pending`, `NoEntry`, and terminal-invalid rules.

For a long position entered at raw price `E`, calculate cumulative measures at the 1-, 2-, and 3-session horizons:

- `MFE = max(session highs through the horizon) / E - 1`;
- `MAE = min(session lows through the horizon) / E - 1`.

Persist MFE as a non-negative return and MAE as a signed non-positive return. Validate that every required symbol bar has positive OHLC values, `Low <= min(Open, Close)`, and `High >= max(Open, Close)`. A violation after the three-session XIU path has matured is terminal `Invalid` rather than a silently repaired excursion.

For each cumulative horizon, persist the first session date and session ordinal on which the MFE and MAE extrema occur. Count the entry session as session 1. If the extrema occur on different sessions, persist `FavorableFirst` or `AdverseFirst`. If both occur on the same daily bar, persist `SameSessionUnknown`; never infer their intraday ordering. Ties use the earliest session.

Use raw prices without transaction-cost adjustments for MFE and MAE. They describe the observed price envelope, not an executable liquidation at the exact high or low. ADR-0025's cost-adjusted closing marks remain the economic return measure.

Do not turn an MFE or MAE level into a stop, target, trailing rule, or automatic recommendation change. These measures are evidence for comparing future versioned exit-policy challengers. Any executable policy must specify feasible fills, gap behavior, same-session ambiguity, costs, and human promotion under ADR-0022.

## Alternatives considered

- **Add fields to `SwingMarkToMarket3` version 1.** Rejected because an accepted immutable outcome definition should not change shape after publication.
- **Estimate intraday ordering from the candle direction.** Rejected because a close above or below the open does not reveal whether the high or low occurred first.
- **Apply exit costs at the exact daily high and low.** Rejected because those extrema are not demonstrated executable fills; doing so would give false precision.
- **Store MAE only as a positive loss magnitude.** Rejected because a signed return keeps direction explicit and aligns it with other return fields. Reports may display its absolute magnitude with a clear label.

## Consequences

- Athena can show how much upside was available and how much downside was endured even when closing returns hide the path.
- Session-level ordering can guide later policy hypotheses, while same-session order remains explicitly unknown.
- The excursion definition creates an additional candidate outcome row but requires no new SQL object.
- MFE and MAE alone cannot prove that a stop or profit target would improve realized results.

## Review questions

1. What do MFE and MAE measure for a long position?
2. Why are the excursion values based on raw highs and lows rather than cost-adjusted assumed fills?
3. What does `SameSessionUnknown` protect us from claiming?
4. Why is `SwingExcursion3` separate from `SwingMarkToMarket3`?
