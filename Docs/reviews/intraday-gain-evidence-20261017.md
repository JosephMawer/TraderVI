# Intraday gain retention: first saved-evidence review

**Reviewed October 17, 2026. Data captured September 18 at 10:42 Toronto.**

The saved paths show giveback worth investigating, with two different causes: existing holdings
lost value, and some later purchases lost money. They do not establish a general morning advantage
or tell us which protection rule would improve results. Both portfolio-wide and individual-position
approaches remain open. No trading rule, threshold, data-acceptance rule or execution behavior changed.

The immediate question is whether apparent morning gains fade; the parent problem is retaining
useful gains without sacrificing better opportunities later; the root goal is dependable portfolio
returns supported by trustworthy observations.

## What this sample covers

- One **Breakout Top 3, portfolio-rules-only paper account**, not the real TFSA or all portfolios.
- The frozen [September 18 investigation](../../artifacts/portfolio-history-20260918/report.md)
  contains eight session dates, 22 filled orders and 2,372 relevant five-minute bars.
- September 9 began during the afternoon; September 18 was still in progress. Excluding those leaves
  **six full-session dates: September 10, 11, 14, 15, 16 and 17**. A full date in the export is not a
  complete-data session or a clean promotion cohort.
- This is a retrospective accounting study. It does not evaluate hypothetical sell rules, rerun
  trading decisions or qualify current collection after October's fixes.

## What the saved paths show

All six reconstructed closes were below their best supported value through noon. However, only
three dates had their highest supported full-day value by noon; the other three peaked later.
Two dates were already at their highest supported value at 09:35, with no subsequent morning rise.
Four dates showed a morning increase from that first complete five-minute checkpoint.

The table uses each date's **09:35 reconstructed value** as its reference. This avoids calling an
old saved opening mark a verified 09:30 value. It deliberately omits the overnight move and first
five minutes. A percentage here describes an account-value change, not a hypothetical achievable exit.
Times are Toronto. “Morning” ends at noon solely to describe the operator's observation.

| Date | Best supported value through noon, versus 09:35 | Close versus 09:35 | Time of highest supported full-day value | Missing five-minute checkpoints |
|---|---:|---:|---|---:|
| Sep 10 | +1.074% | +0.880% | 13:35 | 10 / 78 |
| Sep 11 | 0.000% | -1.945% | 09:35 | 24 / 78 |
| Sep 14 | +0.020% | -0.716% | 10:05 | 6 / 78 |
| Sep 15 | +0.287% | +0.075% | 14:55 | 0 / 78 |
| Sep 16 | 0.000% | -1.405% | 09:35 | 0 / 78 |
| Sep 17 | +0.112% | -0.074% | 13:05 | 20 / 78 |

Selecting a period's best point after seeing the path naturally emphasizes subsequent giveback.
The six-of-six count is an observation, not a statistical edge or a signal that could have been
recognized at that peak. Missing checkpoints can hide earlier or later highs.

## Two different causes of giveback

Accounting from the best supported morning checkpoint to the close separates positions already
held there from positions first bought afterward. “Later” means after that checkpoint, not
necessarily after noon. Saved fill prices and their existing adjustments are preserved unchanged.

- **September 11:** about 0.532 percentage points of the decline came from positions already held,
  and 1.413 points from positions opened afterward. Protecting existing winners alone would not
  address the larger later-position loss in that observed path.
- **September 15:** the positions already held contributed **+0.035 points** after the morning
  checkpoint, while later positions contributed **-0.247 points**. The account also reached a higher
  supported value at 14:55. These facts distinguish new-risk management from selling morning winners.
- **September 10, 14 and 16:** no new positions or add-ons were bought after the selected morning
  checkpoint; the change belongs to the already-held position group, including actual subsequent sales.
- **September 17:** both groups contributed to the decline.

These are explanations of the recorded path, not forecasts of what would happen under a different
strategy. Selling, pausing buys or changing re-entry would change that path and needs a separate study.

## What is missing, uncertain or outside this export

1. **Complete simultaneous prices:** 60 of 468 full-date checkpoints cannot be valued because at
   least one holding lacks an eligible exact-time bar. Coverage is **87.18%**; only two full dates
   have all 78 checkpoints. Missing does not distinguish quiet trading from collection failure.
2. **Verified opening references:** the saved opening value is not evidence of fresh opening prices.
   For example, September 10 ends below its saved opening baseline but above its reconstructed 09:35
   value. Baseline choice can reverse the apparent daily gain/loss sign. The current Shadow initializer
   still calculates its opening value from saved cash and position marks; this source inspection is not
   a fresh check of deployed data or proof that every opening is stale.
3. **Durable closing records:** three of the six full dates lack a saved session close. All six closes
   can be reconstructed for this particular frozen book, including the cash-only ending. That does not
   repair stored sessions or prove reliable end-of-day capture.
4. **What was actually knowable:** price receipt lag across valued, invested full-date checkpoints has
   a median of about 3.2 minutes and a maximum of about 155.5 minutes. September 17's supported 11:50
   morning peak needs a price first received at 12:55:27. Historical bar time cannot be treated as the
   time the UI or an exit rule knew the account value. Later receipts can also span intervening trades.
5. **Broader comparisons:** this export contains no equivalent results for the other portfolios,
   their normal-gate counterparts or later October sessions. Those records are **not examined here**,
   rather than assumed absent. Live has its own revision/snapshot history and must be read through
   its saved contracts; Shadow's overwritten latest-position marks alone are not an intraday history.

## Sensible next evidence step

Extend this read-only comparison to other saved paper accounts and more sessions, keeping strategy,
gate profile and coverage separate. First distinguish genuinely missing records from information
that exists but was not included in the old export. Reconcile event time, receipt time, trades and
complete account values before interpreting a morning peak as an actionable opportunity.

If existing records cannot supply that history, agree the needed valuation capture/recovery work
with the operator. Required facts include cash and positions, exact price references and their
arrival times, completeness and missing reasons, policy identity, and dependable opening/closing
records. No schema or host change is implemented by this review.

Then frame comparisons around both retained gains and later gains/opportunities sacrificed.
Whole-portfolio protection and protection of individual winners remain undecided; no noon exit,
giveback threshold or new-entry pause is selected.

## Reproduction and checks

Source-only record checks: [Shadow session storage](../../TraderDB/dbo/Tables/ShadowPortfolioSession.sql),
[Shadow opening/mark writes](../../Core/Db/SystemShadowRepository.cs) (opening calculation near line 667,
latest-mark replacement near line 1263), and
[Live revision storage](../../TraderDB/dbo/Tables/DelphiLivePortfolioRevision.sql). These identify
different existing record contracts; no operational database census was performed.

Private working evidence stays under ignored `artifacts/morning-gain-review-20261017/`:
`analyze.py`, `summary.json` and `validation.json`. The script reads only the frozen September export
and prior offline results; it has no database, provider, broker or trading-engine calls. Source-file
SHA-256 hashes are saved in the summary. SQLCMD-wrapped JSON is joined as in the original analyzer.

Decimal replay reproduces all **446** previously reconstructed checkpoints, the export's current
cash/open share quantities and all **four** saved closes, including the activation date. Morning-to-close
position-group contributions sum exactly to each reconstructed account-value change. Capital history
contains only the initial snapshot; unexpected cash-event contracts cause the analysis to stop.

No app/build/test workflow, SQL read/write, market request, model operation, paid AI call or trade ran.
These are offline evidence checks, not a new operational qualification or a strategy-performance verdict.
