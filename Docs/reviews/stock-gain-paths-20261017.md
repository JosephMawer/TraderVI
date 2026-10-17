# Individual stocks: morning gains, purchase profit and later upside

**Reviewed October 17, 2026; saved paper history September 9–October 16.**

This answers the corrected question: how stocks **within** each portfolio behaved. The operator
accepted showing the day's move and profit since purchase **separately**. Whole-account results
in the [previous review](paper-gain-patterns-20261017.md) remain background, not stock-level findings.
The immediate problem is stock-level giveback; the parent problem is retaining gains while allowing
later opportunities; the root goal is dependable returns. No protection rule or trading behavior changed.

## The two measures

- **During the day:** for a carried holding, compare with its exact 09:35 price. For a stock bought
  that day, start at its actual purchase, never before ownership. The first five minutes/overnight
  move are omitted for carried holdings. A later purchase cannot have an owned morning gain.
- **Since purchase:** compare each price with the position's share-weighted actual purchase cost
  at that time. Existing add-ons update that cost; future purchases never enter an earlier comparison.
  These are price-based paper profit percentages, not a hypothetical sale after new exit costs.

The main comparison requires a start reference, noon price and price at the **actual sale or
16:00 close, whichever came first**, while still owned at noon. “Faded” means that endpoint price
is below noon, not necessarily a losing trade. Noon is a descriptive observation time only.
Stocks sold before noon and afternoon entries are kept separate rather than treated as missing
morning-to-afternoon holds. Re-entries have distinct position identities.

## What the individual holdings show

There are **235 covered position-days across 112 distinct stock/date combinations**. One
position-day is one held position in one portfolio on one date; shared stocks across portfolios
are counted more than once. These are descriptive overlapping observations, not independent trials.

| Observation period | Covered position-days | Rose by noon | Then faded | Then improved |
|---|---:|---:|---:|---:|
| Sep 9–Oct 16 | 235 | 87 | 40 | 47 |
| After the original Sep 18 export | 166 | 53 | 16 | 37 |
| October alone | 83 | 26 | 2 | 24 |

**Giveback occurs, but later weakness is not consistent.** Nineteen of the 87 rising mornings
lose their entire start-to-noon rise by the actual sale/close. The newer covered subset more
often improves after noon. Coverage and shared positions limit generalization.

The distinction between the two gain measures matters: **21 of 57 carried-position mornings
that rose were still below purchase cost at noon**. Those were recovering losses, not profitable
positions yet. Across all covered positions, 79 were profitable since purchase at noon; 41 had
a lower price by sale/close, and ten ended below their then-current purchase cost.

| Account/profile | Covered position-days | Rose by noon | Then faded | Then improved |
|---|---:|---:|---:|---:|
| Breakout Top 3, rules-only | 30 | 10 | 4 | 6 |
| Breakout Top 5, rules-only | 59 | 25 | 11 | 14 |
| Continuation Top 3, rules-only | 34 | 13 | 7 | 6 |
| Continuation Top 5, rules-only | 53 | 20 | 12 | 8 |
| Delphi Live, rules-only | 18 | 10 | 5 | 5 |
| Five normal-gate accounts, combined counts | 18 | 4 | 0 | 4 |
| Five original reference accounts, combined counts | 23 | 5 | 1 | 4 |

The Live normal/reference accounts contribute zero qualifying rows to those grouped counts.
All 15 saved account books were inspected; the table is not a matched strategy/gate ranking.
Normal-gate/original observations are sparse and share stocks/dates with other accounts.

## A concrete saved day

**September 30, Continuation Top 5 rules-only.** This is an illustration of ordinary holdings,
an actual sale and a later purchase, not a claim that this selected day represents the population.
It is the latest covered account-day containing a rising morning that fades, an afternoon
improvement in another holding, and a covered afternoon purchase. All five positions are shown.

| Stock | Morning move from 09:35 | Profit since purchase at noon | Profit since purchase at actual sale/close |
|---|---:|---:|---:|
| DSV | +0.48% | +1.87% | -0.05% at its actual 13:20 sale |
| TWM | -0.80% | -3.62% | -3.34% at close |
| NUAG | -0.11% | -2.21% | -2.76% at close |
| CAS | -1.45% | -2.11% | -2.49% at close |
| STC | Not owned during the morning | Not yet purchased | -0.82% at close after its 13:30 purchase |

DSV is the gain-retention question: a morning rise plus a profitable position became a small
actual loss. TWM improved after noon while remaining below cost; its afternoon recovery is a
different situation. STC was a new decision after the DSV exit and must be measured from its
own purchase. Protecting DSV's gain alone would not determine whether to buy STC or how it performed.
These facts do not assume DSV could have been sold at a retrospectively selected high.

## Later purchases and upside an early exit might miss

Of **86 afternoon-entry position-days**, 84 have purchase and sale/close values: **31 finish
above purchase cost and 53 below**. Sixty-four show at least one supported five-minute close
above purchase by sale/close. That demonstrates paths worth studying; it does not mean
those interim highs were recognized, executable, or an attainable profit under a chosen rule.

For the separate peak question, 75 main-sample position-days have every expected five-minute
price while held. Fifty-one rise to a morning high: **33 end below that high, while 31 later
reach a higher high**. These groups overlap: a stock can set a higher afternoon high and then
fade again. Looking backward at a morning maximum emphasizes giveback and cannot identify an exit.
Stopping observation at the actual sale avoids crediting subsequent moves as account earnings.

The existing export also supports a closing price after 185 actual sales; 121 close above the
recorded sale price and 64 below. This is separate unowned price context, not portfolio profit
or evidence that a protective exit was wrong. Actual bid/adjusted fills versus bar closes include
execution friction, and sale reasons/counterfactual re-entry have not been tested.

The practical next discussion is what **an individual winner** should retain and how much later
upside we would accept missing. Handling later purchases is a separate part of that discussion.
Whole-portfolio protection remains an unresolved background idea; this review selects neither approach,
threshold, time cutoff, exit action nor re-entry rule.

## Coverage and checks

The bounded export contains **606 owned position-days** after activation on reviewed regular TSX
dates: 393 held through noon, 127 sold earlier and 86 entered after noon. Of the 393, 235 have
the exact three required prices. Missing points are omitted, never carried forward. This describes
the usable sample and does not turn repairs into a prerequisite for the ordinary-day discussion.

Shadow uses the already-exported canonical five-minute closes with Valid/Degraded observations
and no unresolved conflicts. Live uses individual exact-time position prices from the latest
retained checkpoint/closing mark at that endpoint; a whole-account incomplete mark can still
contain a valid price for an individual held stock. Its position endpoint must match the mark,
and its quantity/price contract is checked. Opening-book marks are not substituted. These are
different saved sources kept explicit, not new operational evidence qualification. Post-sale context
uses only available exported canonical closes; eleven Live symbols were outside that bar export.
No extra collection, export expansion or gap repair was needed.

All **285 position histories**, **23 additional buy fills** and **248 whole-position sales** replay
without negative holdings or invented lot allocation. Direct checks independently re-read source
prices and weighted buy costs for **all 235 main rows**, confirming morning moves, noon purchase
profit, endpoint purchase profit and afternoon changes. Existing book/close reconciliation is
documented in the background report. Ten main rows include add-ons; their cost changes are preserved.

This reuses the frozen October 17 17:39:27 UTC paper export, SHA-256
`5014652d3b7815b0935cc4b5c97b959beaf3459cbce7d1eb1106f833a5a55e48`.
Reproduction files under ignored `artifacts/morning-gain-review-20261017-expanded/` are
`stock-review.py`, `stock-verify.py`, `stock-days.json`, `stock-summary.json`,
`stock-after-sales.json` and `stock-validation.json`. Source contracts are
[Shadow purchase costs](../../Core/Db/SystemShadowRepository.cs),
[Live position/fill/mark records](../../Core/Trader/DelphiLive/DelphiLiveLedgerContracts.cs),
[Shadow rules](../adr/0051-system-shadow-portfolios.md),
[Live rules](../adr/0053-delphi-live-v1.md) and
[fixed gate accounts](../adr/0067-fixed-gate-paper-comparison-pairs.md).

Retrospective event-time prices are not proof of contemporaneous receipt or an executable exit.
No hypothetical exit strategy, gains-retained versus upside-sacrificed simulation, stock-level
protection policy or holding-duration decision has been selected. No SQL read/write, producer/app
launch, market request, model/broker operation, build or Core test suite ran during this extension;
validation was offline arithmetic/source checks. Trading rules remain unchanged.
