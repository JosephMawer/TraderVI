# Morning gains across the saved paper portfolios

**Reviewed October 17, 2026; saved history through October 16.**

**Scope correction from the operator, October 17:** the intended question concerns how individual
stocks within a portfolio gain and give back gains. This report measures whole-account values;
its counts do not answer that stock-level question. Retain it as background accounting evidence.
The next review must follow individual holdings, later purchases and subsequent upside. The gain
reference (since morning, since purchase, or both separately) awaits clarification. No stock-level
finding or protection decision follows from this report.

Giveback occurs, but the broader evidence does **not** show a consistent morning advantage.
Among 15 usable portfolio-days that rose from 09:35 to noon, eight finished below noon,
six improved further and one stayed flat. In the newer portion, improvement was more common
than giveback. Continuation's rules-only accounts show the clearest giveback; Breakout and
Delphi Live are mixed. These are small, overlapping observations, not independent trials.

The immediate question is whether the original observation repeats across accounts and dates.
The parent problem is retaining gains without sacrificing later opportunities; the root goal
is dependable portfolio returns. Both whole-account and individual-winner protection remain open.
No trading rule, threshold, gate, collection behavior or execution setting changed.

## Ordinary-session comparison

The review covers all **15 saved paper accounts**: Continuation Top 3/5, Breakout Top 3/5
and Delphi Live, each with its normal-gate, portfolio-rules-only and original reference account.
Rules-only means daily admission gates are bypassed under ADR-0067; portfolio protections remain.
Original accounts retain their different starting histories and are not matched controls.

The main comparison uses exact, complete account values at **09:35, noon and 16:00 Toronto**
on regular TSX dates from September 9 through October 16, after account activation.
Noon is a descriptive boundary, not a proposed exit time. This fixed-time comparison avoids
requiring an entire five-minute path just to answer whether the afternoon added or lost value.
It also avoids selecting the morning's best value after seeing the result.

There are **257 usable account-days**, including **74 invested/traded account-days across
24 dates**. The other 183 stayed entirely in cash and do not enter the pattern counts.
An account-day counts once per account and date; several accounts can share stocks and dates.

| Account/profile | Usable invested/traded days | Up by noon | Of those: faded by close | Improved further | Unchanged |
|---|---:|---:|---:|---:|---:|
| Breakout Top 3, rules-only | 12 | 3 | 1 | 1 | 1 |
| Breakout Top 5, rules-only | 16 | 4 | 1 | 3 | 0 |
| Continuation Top 3, rules-only | 14 | 3 | 2 | 1 | 0 |
| Continuation Top 5, rules-only | 13 | 3 | 3 | 0 | 0 |
| Delphi Live, rules-only | 4 | 2 | 1 | 1 | 0 |
| Five normal-gate accounts, combined counts | 5 | 0 | 0 | 0 | 0 |
| Five original reference accounts, combined counts | 10 | 0 | 0 | 0 | 0 |
| **Total** | **74** | **15** | **8** | **6** | **1** |

Normal-gate and original accounts contribute few usable invested days, with no positive
09:35-to-noon move in this subset. That does not establish that their gains never fade.
These counts do not compare matched periods or rank strategies/gate profiles by performance.

## Newer dates and the earlier peak observation

For **September 19 through October 16**, 50 usable invested/traded account-days include
nine rising mornings: **three faded, five improved and one stayed flat**. Across all 50,
the afternoon rose on 30, fell on 14 and was flat on six, including mornings that had not risen.
For **October alone**, there are 25 usable invested/traded account-days; just two rose by noon,
both on October 6. One faded and one improved. October does not yet supply many rising mornings.

The [first review](intraday-gain-evidence-20261017.md) measured each morning's best supported
value, rather than its noon value. Its finding remains relevant, but answers a different question.
In the expanded export, only 13 invested/traded account-days have all 78 five-minute values.
Five show a rise to a morning peak and all five close below that peak; two also make a higher
peak after noon. Those five observations span only three dates. A noon value can improve later
even when the final close remains below an earlier morning spike.

The three qualifying Shadow morning-peak givebacks separate into two cases where the positions
already held lost value and one where later positions lost value while the earlier group gained.
That supports retaining both protection ideas for discussion, without choosing either from this sample.

## Missing data, kept in proportion

- The export supplies the three exact values for **74 of 177 invested/traded account-days**;
  it cannot characterize every saved day or most full peak paths. Missing dates are excluded,
  without filling prices forward, repairing records or inferring a trading result.
- These are whole-account observations. They do not yet compare protection of individual winners
  with protection of the whole account, or measure gains retained versus recoveries/re-entry missed.
- A bar's market timestamp is not its receipt time. Shadow is reconstructed retrospectively;
  Live uses recorded complete account marks, which can reflect trades processed after a labelled
  bar endpoint. Execution/receipt chronology must be aligned before testing an actionable exit.
  This review does not claim that any displayed peak was available to trade at that time.

No gap repair or capture redesign is proposed as a prerequisite to the next discussion.
The useful next step is to define the ordinary-day behavior we want to compare: what gains
would be protected, whether new purchases continue, and how later upside counts. Keep both
whole-account and individual-winner approaches undecided until the operator chooses the scope.
The evidence favors that broader gain-retention question over assuming a morning-only solution.

## Evidence and verification

One bounded **SELECT-only local SQL export**, captured October 17 at 17:39:27 UTC, contains
348 Shadow session rows, 489 Shadow fills, 32,783 relevant canonical five-minute bars,
4,581 Live marks and 67 Live fills. The analysis uses regular dates from the existing
[reviewed calendar](../../Operations/Calendars/tsx-2026-through-20261223-v1.json), excluding
September 4/8 engineering starts, partial activation dates and non-session records.

Shadow account values replay saved fills against fixed capital and exact same-time bar closes;
only Valid/Degraded source observations without unresolved conflicts enter that retrospective
calculation. This is not fresh evidence qualification. Live collections follow their selected
ledger revision/counts and latest retained item per ordinal. Opening marks value the opening
book, so the analysis instead uses the last retained current-book checkpoint at 09:35/noon
and closing mark at 16:00; each must be complete. It does not substitute an earlier complete
mark when the latest mark at that endpoint is incomplete. No methods are silently interchanged.

All **15 current cash books reconcile** to saved fills and fixed capital; **192 available
Shadow saved closes match** their reconstructed values. Position-group contributions balance
on all 184 complete Shadow paths, including cash days. A separate offline verifier checks
account/date uniqueness, the fixed-time arithmetic and reported counts/periods. These checks
validate this accounting analysis, not an alternative strategy or continuous operation.

Reproduction files stay under ignored `artifacts/morning-gain-review-20261017-expanded/`:
`census.sql`, `export.sql`, `evidence.json`, `analyze.py`, `verify.py`, `summary.json`,
`days.json` and `validation.json`. SQLCMD-wrapped JSON lines are joined before parsing.
The export SHA-256 is `5014652d3b7815b0935cc4b5c97b959beaf3459cbce7d1eb1106f833a5a55e48`;
the reviewed calendar hash is retained in the summary. No private raw records are committed.

Applicable contracts: [Shadow](../adr/0051-system-shadow-portfolios.md),
[Live](../adr/0053-delphi-live-v1.md), [manual recommendation approval](../adr/0062-complete-strategy-recommendation-promotion.md),
[fixed gate profiles](../adr/0067-fixed-gate-paper-comparison-pairs.md),
[compact snapshot reader](../../Core/Trader/DelphiLive/DelphiLiveSnapshotStorage.cs) and
[Live mark construction](../../Core/Trader/DelphiLive/DelphiLiveActionWorkflow.cs).
No database write, producer/app launch, market-service request, model run, broker operation,
build or Core test suite ran. Validation was limited to the read-only export and offline calculations.
