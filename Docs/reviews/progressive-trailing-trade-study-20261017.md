# Progressive trailing bands: saved-stock comparisons

**October 17, 2026. Offline model comparison; trading rules unchanged.**

The proposed bands improve the recorded trade return in seven of **53 distinct other stock trades**
with complete saved histories; 46 are unchanged. Six earlier modeled exits retain a profit, two
recorded losses become modeled profits, and one remaining loss is reduced. Across the seven affected
trades, median modeled return is **+0.53%**, versus **+0.14%** recorded. This is conditional paper-fill
evidence, not seven new actual sales or proof of a generally superior strategy.

The operator prioritizes protected profit over later gains after selling. The immediate question is
whether the proposed individual-stock bands help beyond DSV; the parent problem is reducing winner
giveback; the root goal is dependable returns. The bands were held fixed throughout this comparison.

The [larger-winner extension](#larger-winner-extension-3-and-5-bands) below adds entry-based ARE/AGI
examples and separately labeled illustrations of protecting already-held large winners. It does not
add those illustrations to the 53-trade result above or imply that tighter gaps always retain more profit.
The later [time-of-day comparison](#time-of-day-gap-comparison) tests the operator's morning/noon idea
and nearby schedules. Its mixed results support keeping the original bands as the baseline candidate.
The [saved add-on comparison](#saved-add-on-trade-comparison) then tests the agreed combined-holding
contract: ARE retains profit after extra shares, while an earlier exit can prevent a later add-on.

## Ordinary trades first

The study inspects the frozen paper records through October 16. Its usable sample comprises simple
**single-purchase, fully sold Shadow positions**, with every expected five-minute price from entry
until the recorded exit. No add-ons, missing-price interpolation, historical repair or new entry
decisions are invented. These qualify as ordinary record paths; they are not the `NormalV1` gate cohort.
All eligible cases happen to come from the four **portfolio-rules-only Shadow accounts**.

There are 75 qualifying portfolio-position records, representing 55 distinct stock/entry/exit paths.
Copies of the same trade across accounts produce identical outcomes and count once in the main results.
Excluding both DSV paths leaves **53 distinct trades**. Distinct paths can still share stocks/dates;
they are not statistically independent observations or matched strategy-promotion cohorts.

| Result, excluding DSV and cross-account duplicates | Count/value |
|---|---:|
| Distinct complete closed trades | 53 |
| Protection activates | 8 |
| Earlier modeled exit | 7 |
| Recorded exit retained | 46 |
| Earlier modeled exits with a profit | 6 |
| Recorded losses changed to modeled profits | 2 |
| Median modeled return, same seven affected trades | +0.53% |
| Median recorded return, same seven affected trades | +0.14% |

All seven affected returns improve in this sample. That does not make all 53 trades profitable:
many never reach the activation level and keep their recorded result. Twenty-seven distinct trades
were bought after noon; four receive earlier modeled exits. Their measurement begins at their actual
purchase, without credit for earlier unowned moves. Thirty distinct trades were entered after September 18;
two receive earlier modeled exits. No exception-repair work is a prerequisite to these results.

Across the seven earlier exits, the median modeled net profit retains about **31% of the highest
marked purchase profit observed before the exit signal**. This accounts for the later fill and sell
adjustment; it does not credit any later unowned high. Positive protection here does not mean that
most of an observed profit spike would necessarily be retained.

## The seven changed paths

Returns below include the actual adjusted purchase price and the existing modeled sell adjustment.
They are individual trade returns, not sums of independent paper accounts or portfolio performance.

| Stock | Entry date | Recorded sale date | Recorded return | Earlier modeled return |
|---|---|---|---:|---:|
| ARE | Sep 10 | Sep 11 | +0.36% | +1.25% |
| TLG | Sep 9 | Sep 9 | +0.14% | +0.35% |
| SDE | Sep 15 | Sep 15 | +0.34% | +1.25% |
| MFC | Sep 17 | Sep 22 | -0.10% | +0.11% |
| ARG | Sep 18 | Sep 21 | -1.86% | -1.52% |
| CS | Sep 29 | Sep 30 | -0.53% | +1.25% |
| OBE | Oct 15 | Oct 16 | +0.41% | +0.53% |

This ARE trade is a different holding interval from the September 22 ARE walkthrough. A positive stop
threshold does not guarantee a profitable eventual fill, as the ARG row demonstrates. That limitation
is recorded rather than used to expand this work into an exception-handling project.

## What was actually compared

The schedule is unchanged from the [unapproved ADR-0090 recommendation](../adr/0090-progressive-individual-profit-trailing.md#recommended-first-paper-study-schedule--not-accepted):
activate extra tightening at +1.5% highest purchase profit with a 1% price gap, tighten to 0.75% at
+3%, and 0.50% at +5%. The highest attained band and stop persist across sessions; neither is loosened.
No parameter was retuned after seeing these results.

This is an **additional close-based exit overlay on each recorded position**, not a complete replay
of either engine. Actual entries/costs remain fixed; the original recorded exit is retained whenever
the additional exit cannot be supported earlier. It cannot delay that recorded sale or replace an
already committed exit. Existing protection is represented by that actual exit cap, not recomputed
from altered hypothetical state. Cash reuse, new purchases, missed entries and hypothetical re-entry
after an earlier sale are not replayed or credited.

Five-minute closes are processed only after their first saved receipt. When one receipt reveals
multiple historical bars, only its newest completed close can act; subsequently arriving older bars
cannot backdate an action. The previous threshold is tested before a new high can raise it. An earlier
signal must precede the original exit-order commitment.

The modeled sale uses the saved **open of the next regular-session five-minute bar starting strictly
after receipt**, reduced by the existing **0.25% Shadow sell adjustment**, rounded to six decimals.
That fill bar must itself have been observed before the recorded sale. This estimates the ordinary
Shadow paper-fill convention under continued monitoring; it is not a guaranteed real execution.
Intrabar lows, the existing fifteen-minute high update policy and actual host continuity are not replayed.

All 75 recorded sales independently match their saved bar opens with that same sell adjustment.
Including DSV and duplicated accounts, the model produces 11 earlier exits; 10 are profitable.
The primary table removes those duplicate outcomes and DSV rather than presenting them as extra proof.

## What the bands have and have not shown

Of the eight activated distinct non-DSV paths, **seven exercise only the 1% gap**, one reaches the
**0.75% gap**, and **none reach the 0.50% gap** before the evaluated exit. The first band has useful
supporting examples. The higher bands remain thinly tested; this is not evidence to adopt or retune them.

The bounded scope covers 75 of 175 in-scope, closed single-purchase Shadow records. One hundred have
incomplete full-trade paths and are omitted without repair. Open trades and add-ons remain outside
this first comparison. Normal-gate/original accounts supplied no qualifying completed paths here.
Live's quote-fill contract is different and is not assigned a Shadow bar-open return estimate.
Those coverage boundaries limit generalization and do not block continuing ordinary-case design.

That first sample motivated the larger-winner extension below. Exact operating input timing,
policy/version scope, adoption and any implementation remain separate decisions.

## Larger-winner extension: +3% and +5% bands

**October 17, authorized offline extension. The proposed numbers remain unchanged.**

The +3% band gives positive modeled sale profits in ARE and AGI. The +5% band also retains substantial
profit in two conditional examples of already-held winners, ARE and CAS. These results demonstrate
the proposed mechanism; they do not establish that 0.75% and 0.50% are the best gaps.

### Follow ownership from entry first

Five selected complete entry histories are followed only until the first additional exit, an actual
sale/add-on order commitment, or the end of saved evidence. The model never continues owning a stock
after its hypothetical sale to claim a later +5% result. No add-on behavior is invented: ARE's tested
exit occurs before its next actual add-on commitment. Open AGI/FVI holdings can be tested through an
earlier supported exit without pretending their recorded eventual sale is known.

All five cases are individual positions in portfolio-rules-only Shadow accounts, not whole-account
returns or a new all-trades sample. CS is the previously reported case, included as a reference.
The two AGI entries have different costs but share subsequent prices; they are not independent proof.

| Stock / saved account | Actual entry, Toronto | Highest purchase profit before signal | Gap at signal | Modeled sale, Toronto | Modeled profit since purchase |
|---|---|---:|---:|---|---:|
| ARE / Breakout Top-5 | Sep 17, 09:55 | +3.79% | 0.75% | Sep 18, 10:10 | **+2.64%** |
| AGI / Breakout Top-3 | Oct 15, 10:15 | +4.58% | 0.75% | Oct 16, 11:15 | **+3.06%** |
| AGI / Breakout Top-5 | Oct 15, 10:35 | +3.62% | 0.75% | Oct 16, 11:15 | **+2.10%** |
| CS / Breakout Top-5 | Sep 29, 11:00 | +3.19% | 0.75% | Sep 30, 10:35 | **+1.25%** |
| FVI / Breakout Top-5 | Oct 15, 10:30 | +2.15% | 1.00% | Oct 15, 14:40 | **+0.94%** |

The second AGI entry is an actual saved re-entry, treated as a separate position from its own
purchase. No earlier unowned gains or hypothetical re-entry after the modeled exit are credited.
FVI's actual holding later rises above +5%, but this entry-based overlay would already have sold it
under the starting band. It therefore supplies no entry-based test of the 0.50% gap.

As a check on what tightening itself contributes, retain activation at +1.5% but leave the gap at
1% throughout. That models ARE +2.90%, the two AGI entries +3.08% and +2.13%, CS +1.25% and FVI +0.94%.
The +3% tightening exits earlier in ARE/AGI and still protects profit, but does not increase retained
profit over that wider-gap comparison in these selected paths. This is a comparison of sale profits,
not a requirement to capture every subsequent rally, and it does not select a replacement setting.

### Illustrate the upper band on already-held large winners

The next table asks a narrower question: **if this actual stock were still held at 09:35 on the stated
day, what would the proposed bands do from that point?** ARE uses the same Continuation Top-5 holding
as the earlier walkthrough. CAS uses its first qualifying complete day in the large-winner inventory;
FVI shows why even a larger winner can exit before reaching the tightest band within the day.

Each window has all 78 saved five-minute closes, no same-day add-on or sale, and uses the actual
weighted purchase cost including earlier buys. Monitoring begins with the first received 09:35 close,
with no invented earlier high. These are conditional day illustrations, **not entry-to-exit results,
daily stop resets, or proof the full schedule would still own those stocks**. In particular, prior
add-on handling is unresolved for the long-held ARE case, and the FVI entry test above already exited.
The windows are not added to the entry-based trade counts or combined into portfolio performance.

| Held stock / day | Purchase profit at 09:35 | Day move at highest observed price before signal, from 09:35 | Purchase profit at that high | Gap at signal | Modeled sale | Modeled profit since purchase |
|---|---:|---:|---:|---:|---|---:|
| ARE / Sep 22 | +11.52% | +0.86% | +12.48% | 0.50% | 10:00 | **+12.35%** |
| CAS / Sep 25 | +2.80% | +2.30% | +5.16% | 0.50% | 10:50 | **+4.55%** |
| FVI / Oct 16 | +2.43% | +2.01% | +4.48% | 0.75% | 10:10 | **+3.85%** |

ARE's +12.48% is profit since purchase; its rise within this morning window is only +0.86%.
The 09:55 close crosses the 0.50% trailing threshold, becomes known about 09:57, and gives a modeled
sale at the next bar starting 10:00. The saved opening price rebounds before that sale, producing
+12.35% after the sell adjustment. This favorable fill is not a guaranteed stop floor.

CAS crosses +5% within its window, tightens to 0.50%, and models a sale retaining +4.55%. Its starting
+2.80% profit belongs to earlier ownership; it is not all earned that morning. FVI's overlay exits
at the +3% band before its later +5.10% saved high, so that later high cannot activate a band after sale.

To isolate the +5% tightening, keep the same windows and earlier bands but cap the gap at 0.75%:
ARE models +13.58% and CAS +4.67%, both later exits. FVI is unchanged. Thus the 0.50% gap protects a
substantial profit sooner in these examples, but retains less than 0.75% in these particular paths.
Against a fixed 1% gap, CAS retains more (+4.55% versus +3.92%); ARE is slightly lower (+12.35% versus
+12.48%). These small, selected illustrations support the profit-first idea without proving an
optimal gap or requiring us to pursue later upside.

### What this adds, and what remains open

We now have ordinary larger-winner examples for the +3% band and clearly conditional illustrations
for +5%. No new missing-data repair was needed. The +5% gap still lacks a clean entry-based example
that survives all lower bands to that point; these illustrations do not close that evidence limit.
Before implementation, review the exact gaps and settle observation timing, prospective policy/version
scope and how prior add-ons affect cost/high references. No add-on rule or daily reset is adopted here.

The extension reuses the same three frozen evidence files and their hashes below, without SQL or
provider calls. An independent Decimal verifier checks **24 first crossings and adjusted fills**:
five entry cases and three conditional windows, each under the proposed schedule and two comparisons.
It derives the prior stop from the highest previously available price, checks first-receipt order,
the first subsequent regular-session bar opening, the 0.25% sell adjustment, purchase-cost ownership,
complete window coverage, and actual order-commitment boundaries. These 24 are scenario checks,
not 24 independent stock trades. Reproduction: ignored `upper-band-study.py`, `upper-band-results.json`,
`verify-upper-band-study.py` and `upper-band-validation.json` beside the first study's artifacts.

## Time-of-day gap comparison

**October 17. Offline research only; no operating policy adopted.**

The operator proposed more room during the volatile opening period, tightening around **10 a.m.
Toronto time**, then further at **noon**. The operator clarified that "10pm" meant 10 a.m., authorized
the proposed comparison schedule and invited a few nearby alternatives. Activation remains +1.5%;
all bands refer to highest profit since actual purchase. No stop is lowered, including next morning.

The immediate question is whether clock-based gaps protect individual-stock profits better; the
parent problem is avoiding winner giveback; the root goal remains dependable returns. Morning
volatility makes the hypothesis plausible: [NYSE research](https://www.nyse.com/data-insights/opening-auction-filling-behaviors-opportunities-early-in-the-day)
discusses higher volatility after the US open. It does not establish these TSX settings or clock times.

### Fixed test schedules

Every cell below lists gaps for the **+1.5% / +3% / +5% purchase-profit bands**, in that order.
The variants were set before evaluating them; there was no parameter search or further retuning.

| Schedule | Before 10 a.m. | 10 a.m. to noon | Noon onward |
|---|---|---|---|
| Original | 1% / 0.75% / 0.50% | 1% / 0.75% / 0.50% | 1% / 0.75% / 0.50% |
| Agreed time proposal | 1% / 1% / 0.75% | 1% / 0.75% / 0.50% | 0.75% / 0.50% / 0.50% |
| Gentler time changes | 1% / 0.875% / 0.625% | 1% / 0.75% / 0.50% | 0.875% / 0.625% / 0.50% |
| More morning room | 1% / 1.25% / 1% | 1% / 1% / 0.75% | 1% / 0.75% / 0.50% |
| Tighter all day, comparison | 0.75% / 0.50% / 0.50% | 0.75% / 0.50% / 0.50% | 0.75% / 0.50% / 0.50% |

The tighter all-day comparison helps distinguish clock timing from simply tighter protection.
At each newly available completed close, update the high and calculate that receipt time's candidate
stop, then test the close against the greatest stop established so far. A change at 10/noon takes
effect on the first new eligible price received afterward, not retroactively or through an invented
exact-clock quote. The current close may immediately be below a newly tightened threshold. No
separate clock-only order is invented. The original constant schedule reproduces all prior outcomes.

First-receipt grouping, actual purchase cost, pending original-order priority, the next strictly later
regular-session bar open and 0.25% sell adjustment remain as in the preceding studies. A later morning's
wider candidate cannot reduce yesterday's stop. Hypothetical sales end ownership; no subsequent rally,
new purchase, cash reuse or hypothetical re-entry is credited. Actual later entries begin afresh at
their own recorded cost. Original exits still represent existing protection rather than its engine replay.

### Completed individual-stock trades

The same 75 complete closed position records represent **55 distinct stock/entry/exit paths**, now
including both DSV paths. The prior non-DSV count is still 53. All are rules-only Shadow records;
copies across accounts count once, and this is not a portfolio return or matched policy qualification.

| Compared with original proposed bands | Higher sale profit | Lower sale profit | Same sale profit |
|---|---:|---:|---:|
| Agreed time proposal | 3 | 1 | 51 |
| Gentler time changes | 3 | 0 | 52 |
| More morning room | 0 | 0 | 55 |
| Tighter all day | 4 | 1 | 50 |

All values below are **profit since purchase after the modeled sell adjustment**, not the day's move.

| Stock / entry | Original bands | Agreed time proposal | Gentler time changes |
|---|---:|---:|---:|
| ARE / Sep 10 | +1.25% | +1.63% | +1.27% |
| BTE / Sep 15 | -1.41% | **+0.48%** | **+0.48%** |
| SDE / Sep 15 | +1.25% | +1.32% | +1.32% |
| DSV / Sep 29, 11:15 | +1.29% | **+0.64%** | +1.29% |

The agreed schedule adds one earlier exit, turning BTE's modeled loss into profit. It has nine earlier
exits, eight profitable, versus eight/seven under the original bands. It reduces DSV's retained profit
without turning it into a loss. The tighter all-day comparison also improves ARE/BTE/SDE and adds an
MFC improvement, so the completed-trade gains do not establish a distinct benefit from morning room.

The three improvements occur among entries before September 19. Of 32 distinct later entries, the
agreed schedule improves none, reduces DSV and leaves 31 unchanged. The gentler version leaves all
32 unchanged. All 27 distinct after-noon entries have the same sale return under the three time
variants as under the original. These are useful boundaries on the result, not repair prerequisites.

### Larger winners and conditional day illustrations

The previously selected larger-winner entry tests remain separate; CS overlaps the closed sample.

| Actual entry-based test | Original | Agreed time proposal | Gentler time changes | More morning room |
|---|---:|---:|---:|---:|
| ARE / Sep 17, Breakout Top-5 | +2.64% | **+0.92%** | **+0.92%** | +2.90% |
| AGI / Oct 15, Breakout Top-3 | +3.06% | +3.06% | +3.06% | +3.08% |
| AGI / Oct 15, Breakout Top-5 | +2.10% | +2.10% | +2.10% | +2.13% |
| FVI / Oct 15 | +0.94% | +1.11% | +1.08% | +0.94% |

The larger ARE entry illustrates why the gentler version is not a universal upgrade despite its
three/no-lower result in the closed subset. It takes profit on September 17 rather than September 18;
both are profits, but the amount retained is smaller. DSV also illustrates persistence: an afternoon
tightening on September 29 remains in force the next morning, causing a 09:50 rather than 09:55 sale.
Morning room does not erase a previously raised stop. These comparisons concern sale profits, without
requiring either position to capture gains after it has sold.

The three **conditional already-held 09:35 windows** retain their earlier limitations; they do not
prove ownership would survive from purchase under the lower bands or resolve prior add-ons.

| Conditional held-stock window | Original | Agreed time proposal | Gentler time changes | More morning room |
|---|---:|---:|---:|---:|
| ARE / Sep 22 | +12.35% | **+13.79%** | +12.35% | +13.58% |
| CAS / Sep 25 | +4.55% | +4.55% | +4.55% | +4.67% |
| FVI / Oct 16 | +3.85% | +3.85% | +3.85% | +3.81% |

Under the agreed schedule, already-held ARE reaches a +2.99% day-window rise from 09:35, corresponding
to +14.86% profit since purchase, before a modeled 10:35 sale retaining +13.79%. The opening observation
already carried +11.52% purchase profit. Those three gain references are distinct; the +13.79% is not
all earned that morning. This is a useful illustration of morning room, with no claim of entry-based survival.

### Recommendation and checks

**Keep the original activation/profit bands and gaps as the baseline research candidate for now.**
The time idea is worth preserving, but automatic noon tightening is not a consistently better
profit-protection result across the ordinary completed and larger-winner tests. The gentler time
version is a reasonable candidate for a future comparison, not a selected operating policy. Do not
pick it solely from its favorable closed subset or optimize around ARE/DSV. No new rule is implemented.

The same three frozen source files are reused, without SQL, provider or application calls. All 83
original-schedule cases reproduce: 75 underlying closed records, five larger entry tests and three
conditional windows. An independent verifier checks **415 scenario outcomes**, including 101
supported modeled fills and 314 retained original exits/window ends, plus all 75 recorded sales and
five paired summary tables. It recomputes the maximum of the full threshold history, independently
checks receipt-time phase boundaries, purchase ownership, first crossings, later fills and costs, and
checks duplicates. Three synthetic boundary/overnight cases validate 10/noon tightening and preservation
of an earlier day's stop. Scenario counts include duplicated account paths and repeated illustrations;
they are not counts of independent trades.

Reproduction beside the previous artifacts: `time-gap-study.py`, `time-gap-results.json`,
`time-gap-summary.json`, `verify-time-gap-study.py` and `time-gap-validation.json`. Raw evidence stays
ignored. Trading rules, settings, database records, models, brokers and running applications are unchanged.

## Saved add-on trade comparison

**October 17. Authorized offline test of the agreed combined-holding design; no trading-rule change.**

ARE retains about **+1.58% to +1.93% after the morning add-on**, with the original stop and earned
0.75% gap preserved. Its newly added shares individually lose about **1.48%**, but the combined
holding remains profitable. In the account scheduled to add at noon, protection sells first for
**+2.64%**, so that add-on never occurs. This tests the contract from initial entry rather than
initializing protection on an already-profitable later day.

### Ordinary supported cases

The frozen inventory contains 23 add-on position records. Seven have a complete relevant path to
either a supported earlier exit or their unchanged original exit: four ARE, two MATR and one CAS,
all portfolio-rules-only Shadow positions. They represent six distinct stock/cost histories across
three symbols; identical ARE Top-3 histories count once when describing distinct histories. The
different weights of extra shares in some accounts produce different combined average costs.

Six records actually include their add-on under the overlay. One exits before the add-on order.
Three ARE records have earlier exits after adding, and the fourth exits before adding; all four
retain a profit. Three other records keep their original exits. These seven records are a small,
selected subset, not seven independent stocks or a whole-portfolio simulation.

Percentages below are net purchase returns using adjusted buy costs and the modeled sell adjustment.
Recorded ARE exits were September 24; all earlier modeled ARE exits are September 18 at 10:10 Toronto.

| Stock / account | Actual add-on | Add-on in modeled holding? | Modeled purchase return | Recorded purchase return |
|---|---|---|---:|---:|
| ARE / Breakout Top-3 and Continuation Top-3 | Sep 18, 10:00 | Yes | **+1.93%** | +12.00% |
| ARE / Continuation Top-5 | Sep 18, 10:00 | Yes | **+1.58%** | +11.62% |
| ARE / Breakout Top-5 | Sep 18, 12:00 | No; sold before its order | **+2.64%** | +11.87% |
| MATR / Continuation Top-5 | Sep 10, 09:55 | Yes | -0.58%, unchanged | -0.58% |
| MATR / Continuation Top-3 | Sep 10, 09:55 | Yes | -0.61%, unchanged | -0.61% |
| CAS / Continuation Top-5 | Sep 29, 09:55 | Yes | -0.17%, unchanged | -0.17% |

The ARE result protects a smaller profit earlier; it does **not** improve these recorded ARE sale
returns. That remains consistent with the operator's profit-first goal, without proving optimal gaps.
MATR/CAS never reach +1.5% activation in these supported histories, so the extra protection does not
change their existing exits. No lower activation level is selected to make those trades profitable.

For the noon-add-on ARE row, modeled and recorded returns use different invested capital because
only the recorded path purchases the extra shares. That percentage pair is context, not a comparison
of identical outlays. Saved cash is not counted as trading profit or hypothetically reinvested.
The after-add-on ARE rows use the same two purchases/capital in both paths.

### What happens at the ARE add-on

The initial September 17 purchase earns a 1% gap that afternoon. The gap tightens to 0.75% on
September 18, before the morning add-on. Applying that add-on raises the weighted average cost,
while the established absolute stop and 0.75% gap remain unchanged.

For Continuation Top-5, at the same newly known price, applying the extra shares changes current
purchase profit from about **+2.98% to +1.92%**. That lower percentage does not revert the gap to 1%.
The 10:05 completed close is first received about 10:07; the extra purchase is also first supportable
in that receipt and is applied before the protection check. The close breaches the preserved stop,
giving a modeled full-position sale at the later 10:10 bar open, retaining +1.58% after costs.

The Top-3 accounts retain +1.93% because their add-on weight, and hence combined average cost, differs.
The delayed Breakout Top-5 add-on was not yet ordered when the model signaled the same earlier sale;
the model owns only the original shares and retains the previously verified +2.64%. None of these
paths reaches the +5%/0.50% band before the modeled exit. This is not empirical validation of that
tightest band after an add-on.

### Method and validation limits

The additional close-based overlay keeps actual entry/add-on quantities and adjusted prices only
while ownership survives. It keeps the actual price high, tightest earned band and highest stop
across purchases/dates. Current/future qualification uses updated weighted average cost, rounded to
six decimals for policy state as in the Shadow store; monetary return uses the actual cost outlay.
When active, a higher cost-aware break-even candidate may raise protection. Old prices are not
reinterpreted as gains earned by newly purchased shares.

Filled purchases become usable when their saved fill bar was first received, not retrospectively at
the bar's opening timestamp. The controller's pending-fill-before-protection order is respected for
the same receipt. Existing sell creation cancels pending buys, but no supported case requires a
new cancellation assumption: the skipped add-on had not yet been ordered. An additional sell cannot
replace an already committed original exit. Its modeled fill is the first regular-session bar
starting strictly after receipt, with the existing 0.25% sell adjustment and six-decimal rounding.

The actual recorded sale remains the cap representing existing protection; this is not a full
replay of its fifteen-minute high/intrabar-low rules or a deployed policy. No new entries, cash reuse,
later re-entry or alternative add-on eligibility is modeled. No market service, SQL or producer runs.

Fifteen other closed records lack a complete prefix before any supportable extra exit, and one
position is still open; these are omitted without repair. ARE's missing September 21 close is after
the modeled September 18 sale, so it does not invalidate the complete pre-sale prefix. Later market
history cannot change a hypothetical holding already closed. Original recorded return is separately
supported by its actual purchase/sale ledger and checked adjusted sale price.

An independent verifier checks all seven supported recorded sale returns, four hypothetical first
crossings/fills, six included add-on cost/stop/band transitions, three complete unchanged histories,
ownership timing and duplicate equivalence. All 22 closed inventory sales and their 44 purchases
match saved opens with existing buy/sell adjustments before path exclusions. The same three frozen
source hashes below match; no new export is needed. Reproduction under the existing ignored artifact
directory: `addon-inventory.json`, `addon-trailing-study.py`, `addon-trailing-results.json`,
`addon-trailing-summary.json`, `verify-addon-trailing-study.py` and `addon-trailing-validation.json`.
Only normalized paper returns are committed. Observation/high timing, version scope and operational
adoption still need agreement; no implementation is performed or implied by these results.

## Evidence and validation

The original frozen export SHA-256 is
`5014652d3b7815b0935cc4b5c97b959beaf3459cbce7d1eb1106f833a5a55e48`.
Two bounded SELECT-only supplements read already-saved bar OHLC values and actual Shadow order
commitment timestamps. All **32,783** original bar references match their close/receipt/audit/conflict
fields, and all **489** original fill records match the order supplement. No market data was collected.
The bar supplement was captured October 17 at 19:04:05 UTC; its SHA-256 is
`f5de59cf6eab99d7d548cc01b9db45cff350b1a7b4c27c52b109837310d98487`.
The order supplement SHA-256 is `0bee60cad3bb3d219950fafa89bd997ac6bd4887813936fe6199746032a27951`.

An independent offline verifier checks all 75 recorded fill returns, all 11 hypothetical first
crossings and adjusted fills, chronological signal/fill/commitment ordering, and identical outcomes
before deduplication. It recomputes the previous threshold directly from previously available prices
rather than trusting the analyzer's saved stop state. Decimal arithmetic and reviewed TSX session
dates are used throughout.

Reproduction stays under ignored `artifacts/morning-gain-review-20261017-expanded/`:
`export-fill-bars.sql`, `fill-bars.json`, `export-order-timing.sql`, `order-timing.json`,
`compare-trailing-trades.py`, `trade-comparison-rows.json`, `trade-comparison-summary.json`,
`verify-trade-comparison.py` and `trade-comparison-validation.json`. Raw order/account records and
position amounts are not committed. Relevant contracts are [Shadow execution](../adr/0051-system-shadow-portfolios.md),
[fixed profiles](../adr/0067-fixed-gate-paper-comparison-pairs.md),
[pending fill boundaries](../../Core/Trader/SystemShadowPolicy.cs) and
[pending sell execution](../../Core/Trader/SystemShadowController.cs).

No trading rule, setting, database record, model, broker order or running application changed.
No producer, backfill, market service, build or Core test suite ran. Validation consists of the
SELECT-only evidence reads and offline arithmetic/source checks; this is not operational qualification.
