# Connected trading workspace — design and implementation

- **Status:** Accepted and implemented in WPF, 2026-09-09; original workspace observed running. Unified portfolio grid and collapsible navigation verified offline; runtime rebuild/restart pending.
- **September 10 follow-up:** the manual market-buy section below was accepted and implemented under [ADR-0075](../adr/0075-manual-market-buy-ticket.md), with offline verification; native live submission remains unverified.
- **October 18 redesign / implementation:** Workspace, the Trading arrangement, clear decision explanations, panel Buy/Sell and **Performance first** are accepted. **Today** is default with **Overall** selectable; redundant headings/net deposits are removed. The operator now authorizes Trading-only WPF implementation, built and offline-tested below. Actual portfolio choice, daily-return qualification and execution integration remain open; operational rollout is not authorized by the UI request.
- **Domains:** desktop, explainability, portfolios
- **Related ADRs:** [0055](../adr/0055-independent-strategies-to-approved-live-execution.md),
  [0061](../adr/0061-portfolio-comparison-workspace.md),
  [0062](../adr/0062-complete-strategy-recommendation-promotion.md),
  [0066](../adr/0066-operational-paper-daily-source-adoption.md),
  [0067](../adr/0067-fixed-gate-paper-comparison-pairs.md),
  [0068](../adr/0068-main-account-trading-overview.md)

The operator accepted the main-account direction and explicitly requested implementation. This record
now covers the implemented WPF presentation and its remaining evidence/integration work under ADR-0068.
The request did not authorize changes to trading rules, broker execution, model training or schema
deployment. Conversation concepts remain illustrative; the WPF implementation reads existing records.

## October 18 visual redesign — draft

**Immediate problem:** establish a fresh, clear visual layout across the four requested screens.
**Parent problem:** growing account, strategy and engine information competes for attention.
**Root goal:** understand the main account and independent paper strategies, inspect their evidence,
and act deliberately with the existing account, risk and execution boundaries intact.

This section preserves the design record. The later explicit request authorizes Trading-only UI
implementation as recorded below; it does not authorize deploy/migration, training, collection,
broker operations or trading. Earlier accepted product behavior remains authoritative.

### Current decision record

| State | Direction |
| --- | --- |
| Accepted October 18 | Explore Trading, Portfolios, Delphi and Delphi Live together. |
| Accepted October 18 clarification | Prioritize clearer hierarchy/less clutter and a fresh visual style; design detailed drill-downs after establishing the main layout. |
| Existing accepted direction | Trading leads with the main account, holdings/results and actual assigned strategy; attention supports that overview (ADR-0068). |
| Provisional | Use shared navigation, typography, spacing, account/mode labels and source-time treatment across the four screens. |
| Accepted October 18 follow-up | Choose **Workspace**: holdings/table and a persistent selection panel side by side. Focus remains a preserved initial alternative. |
| Accepted October 18 follow-up | Refine Trading and agree its main layout before moving to Portfolios, Delphi or Delphi Live. The order among those remaining screens is open. |
| Accepted October 18 control placement | Both Buy and Sell belong in the holding panel; no top-toolbar trade buttons. |
| Accepted Trading hierarchy | Account overview → assigned strategy → holdings beside the selected-stock panel; compact recent account activity below holdings. Supporting data issues stay beside the affected holding. The operator says “I like this arrangement.” |
| Accepted overview | **Performance first**: selected-period dollar gain and percentage lead, with its period underneath; **Today** is the default and **Overall** is selectable. Account value/cash and separately scoped recorded gains follow. Remove net deposits from this overview. Account value first remains a preserved alternative. |
| Accepted header cleanup | Remove the entire Main account / Example TFSA / Canadian dollars heading block, following the earlier Trading-title/right-hand-badge removal. This single-account view does not need a repeated account title; retain currency beside figures and source/authority context. |
| Accepted density refinement | Reduce the performance amount/percentage and tighten overview spacing so the numbers occupy less space. |
| Accepted holdings columns | Stock, position value, open gain and strategy status. Shares belong in the selected holding panel only, alongside average cost. |
| Accepted panel grouping | Stock identity → position value/open gain → shares, average cost and saved price/time → strategy status/explanation → Buy/Sell. The operator says this looks good. |
| Accepted explanation direction | Clearly explain why Hold or Sell; remove repeated “saved strategy evaluation” phrasing. Retain a compact actual check time and missing-data state. |
| Accepted strategy control placement | One selected account strategy, shown as a dropdown where “Portfolio to be selected” was, as the entry point for manual portfolio/strategy switching. Exact portfolio choice remains deferred. |
| Accepted explanation treatment | Decision → concrete reason/evidence → next exit trigger or next step; show order state separately from Sell. Mock examples/thresholds remain fictional, not selected rules. |
| Accepted switch clarification | Queue old-strategy-only exits as soon as existing trading checks allow, even at a loss; do not wait for the old strategy's normal signal. Shared holdings/protection remain; confirmed required exits reconcile before new buys. Only holdings managed by the old strategy are in this scope. |
| Accepted implementation scope | Implement the agreed Trading UI only; leave other tabs/views/pages alone. This starts UI delivery, not automatic execution or operational rollout. |
| Deferred by operator | Exact portfolio choice. The earlier tentative Delphi Live preference is context only; no assignment, changed comparison eligibility or broader family/AI scope change is agreed. |
| Open | Daily performance source/baseline/cash-flow/percentage qualification, visual styling, target window dimensions and remaining screens' layouts/order. |
| Deferred by operator | Detailed drill-down layouts until the fresh main layout is established. |

### Screen roles and source inspection

- **Trading:** main real-account overview, holdings and assigned strategy, with compact attention
  beside affected facts. Broker snapshots, operator-reported records and saved holding prices retain
  their own sources/times. Open-position gains are not complete account earnings.
- **Portfolios:** independently running paper accounts, descriptive comparisons and selected-account
  context. Preserve shared comparison periods, gate profiles, unavailable values and the distinction
  between account history and exact-version performance. A review is separate from assignment/execution.
- **Delphi:** saved daily selection, selected lens and dated market context. Refreshing saved evidence
  remains separate from running official Delphi. Indicators and model outputs retain their meanings.
- **Delphi Live:** intraday paper candidate progression and reasons, with observation time, missing
  data and replay/current mode prominent. "Live" does not mean real-money execution.

October 18 inspection read the current XAML for the shell and these four screens, their shared styles,
and existing UI/measurement contracts. Trading currently includes the broker panel, recorded-account
disclosure, recommendations and account/report/history controls. Portfolios already exposes a unified
comparison grid and selected-account evidence. Delphi retains its saved-result/official-run distinction;
Live retains account selection, current/replay modes and advanced controls. These are source findings,
not observations of the installed running application. The concepts deliberately sample the landing
content; they are not a complete inventory or a decision to remove existing features.

### Trading Workspace refinement

The next concept is `tradervi-trading-workspace.html`, focused only on Trading. It uses a single
Workspace layout with the account value/cash and separately labelled recorded-sale/open-position gains
above the assigned-strategy strip. Full account gain remains unavailable. The holdings table has
stock, shares, value, open gain and decision; narrower desktop widths move decision detail to the panel.
The side panel shows the selected stock, saved position facts and strategy explanation, with Buy/Sell
together at its bottom. A compact current-data notice selects the affected holding; there is no large
page-wide attention banner. Recent account activity is secondary below the holdings.

**Provisional new-stock route:** a small Find stock action within the same panel lets the concept
select an unheld fictional stock without moving Buy outside the panel. This proposes placement only;
actual search behavior and order tickets remain for later detail design. Both order buttons are inactive
in the concept. Selecting a stock is not a recommendation, account change or order.

The concept includes local selection/search and optional panel-width, row-spacing and activity-display
design controls. Fictional figures reconcile within their stated scopes; separately timed account and
holding sources must remain explicit in implementation. The operator has accepted the Workspace
direction, Trading-first sequence, trade-control location and overall content arrangement. A complete
interaction specification remains open. Next refine the top overview's gains/performance before other
screens or detailed views.

The current integrated `tradervi-trading-performance-first.html` subsequently adopts the operator's
**shares in the panel only** choice. Its table has four columns: stock, position value, open gain and
strategy status, with stock name beneath the symbol and a selected-row highlight. The panel groups
position figures and share/cost/price facts before the strategy explanation, with both trade controls
below. The operator accepts this visual grouping and requests refining the strategy explanation next.
Saved-price and evaluation times remain separate; dated-price warnings stay visible. Selection changes
the local fictional panel only. Detailed views, source qualification and real order tickets remain open.

**Strategy decision refinement:** the panel now demonstrates Hold, Sell and Await data without repeating
“saved strategy evaluation.” Hold explains the unmet exit and a concrete exit trigger; Sell explains
the triggered rule and separately says whether an order was submitted. Await data does not imply a
current Hold. A short check time remains. Prices/protection levels and continuation failures in this
concept are fictional examples of explanation structure, not new thresholds, rule changes or verified
decision outputs. Implementation must use the exact selected strategy's actual applicable rules and
facts, including manual/unmanaged ownership and existing independent protection where relevant.

The strategy strip now contains a labelled native dropdown in the former placeholder's position.
Two explicitly fictional Delphi Live examples demonstrate a single selected strategy changing the
table/panel explanation together; account values, shares, costs and history stay fixed. These examples
are not the comparison books or qualified real choices. The actual portfolio remains deferred; wider
family support and current comparison-selection exclusions remain unchanged. This mock changes local
display state only. Real assignment must retain the existing reviewed exact-version handover, account,
order and protection contracts (ADRs 0079/0081/0085/0086); dropdown placement does not authorize immediate
live assignment or an order. Detailed switch/review interaction remains to design.

### October 18 Trading-only implementation

The operator subsequently authorizes implementation in Trading and accepts queueing old-only exits
when existing checks permit, including at a loss. This clarifies ADR-0079's existing handover: retain
shared actual positions and established protection, confirm/reconcile required non-overlap sales, then
permit fresh qualified entries within real available funds and risk limits. It does not wait for
break-even or copy paper holdings/balances into the real account. Manual/unmanaged holdings remain
outside implicit management (ADR-0085). No new throttling interval or partial-sale sizing rule is agreed.

`WealthsimpleAccountPanel` implements the compact Performance-first summary, Today/Overall controls,
four holdings columns, selected holding position/share/average-cost panel, decision/reason/next-step
and the existing manual Buy more / Sell all commands together. The table stacks above the panel below
860px. Current broker values retain their currency; snapshot retrieval is explicitly not market-price
time. The top account heading and net deposits are omitted. Pending orders, connection, risk/import,
manual-order history, stop/resume-buying and legacy reported-account tools remain accessible below.
No shared style, navigation shell or other tab's source is changed.

The dropdown loads the existing Delphi Live review catalogue and opens the selected portfolio's
existing transition review. Unsupported comparison sources remain visible but disabled. If the requested
source disappears, the review does not silently substitute another. Closing the preview restores the
actual saved-selection display; it does not approve a strategy, create SQL writes or send orders.
The existing preview's family limits are implementation limits, not a new restriction on the accepted
all-family product direction. Gradual automatic switching and broader family execution remain unfinished.

Today shows **Unavailable** because no daily account-return source/calculation is qualified. Overall
uses the existing broker amount/rate with the supplied period and original as-of availability; retrieval
time never substitutes for return valuation time. The fictional HTML values are not runtime defaults.
The broker holding panel reads verified linked management records for exact security/symbol/quantity
matches: planned required exits, queued intents, acknowledgements and uncertain sales have distinct
states. Missing explicit current Hold/Sell results stay **Await decision**. This does not derive new
signals or pretend the unintegrated execution host has evaluated current prices. Full native strategy
decision/trigger presentation remains a source-integration dependency. Broker rows remain distinct from
the separately scoped recorded gains available in the legacy recorded-account section.

Validation: .NET SDK 10.0.401; focused WPF build passes; 194 account-reader/control/presentation tests
pass, including six new workspace/decision tests. Four isolated synthetic WPF renders at 1200/900/680px
exercise holding selection and exact Sell target, Today/Overall bindings and dropdown label with no
binding errors; inspected wide and stacked layouts. Evidence is in ignored
`artifacts/trading-workspace-20261018/` and `artifacts/ui-redesign-20261018/trading-*.log`.
Known nullable-annotation compiler warnings and `MainWindow.xaml.cs`'s unused variable warning remain.
Restore used `NuGetAudit=false`, so no fresh dependency-security assessment is claimed. Full solution/
SSDT validation did not run. This offline fixture does not launch TraderVI or access SQL, broker,
market services or models. In the later operator-authorized rebuild/restart, the normal Release output
built successfully (262 existing warnings, zero errors), then launched at 22:15 Toronto; PID 20036 was
responsive with the TraderVI window at 22:16. Executable path and output WPF/Core hashes are recorded
in ignored `artifacts/ui-redesign-20261018/`. No old process was running to close. Native Trading
bindings/operator visual feedback and broker/execution qualification remain unverified. Normal desktop
startup uses existing configured services; no separate migration, training, broker diagnostic, manual
order or strategy activation was invoked. The operator requests committing/pushing the pending work.

### Account gains and engine/portfolio clarification

The operator accepts the Trading arrangement and requests refining account gains/performance next.
`tradervi-account-performance.html` compares **Performance first** (recommended for review) with
**Account value first**, using fictional numbers, a broker period/as-of time, cash and net deposits.
Separately scoped open-position gain and last-30-calendar-day recorded sale gain remain below; they
must not be added to reconstruct whole-account return. Availability controls illustrate missing broker
return without turning it into zero. The operator now selects **Performance first** and explicitly
defers portfolio choice. The operator then chooses **Today by default with an Overall option**. The integrated full-screen
`tradervi-trading-performance-first.html` combines that overview with the accepted Trading arrangement
and Buy/Sell panel. Its initial strategy placeholder is subsequently replaced by the fictional dropdown
described above; neither represents the actual account assignment or a change to it. Earlier concept
files preserve the alternatives and design history.

The current integrated draft has functional local Today/Overall controls that update the headline
amount, percentage and period together. The operator subsequently requests removing the entire
Main account / Example TFSA / Canadian dollars header, superseding the prior account-name retention.
The current single-account concept removes that block; a future account-switching context would need
clear account identity. Currency remains beside figures. The headline amount is reduced from 33px to
26px, its percentage from 21px to 17px, with tighter overview padding and spacing. Both periods use fictional values.
The next request removes net deposits from this overview, leaving account value and available cash
as two supporting figures, and moves the design focus to holdings/table-panel refinement. Net deposits
(deposits less withdrawals) can explain contributed capital in account history, but add little to this
holdings-focused summary. Removing the display does not remove cash-movement records or change return
calculations. Account history is the recommended later location; its detailed layout is not decided.
Choosing Today is a display decision, not qualification of a daily-return producer or calculation.
The daily reference boundary, confirmed cash movements, percentage method and native source coverage
remain implementation work; existing dated day-value/risk readers do not by themselves prove a daily
performance result. If daily performance is unavailable, keep Today labelled and show unavailable;
never substitute the broker's overall return under a Today label. Overall retains the actual provider
period/as-of context. Switching the headline period does not silently change the separately labelled
open-position or last-30-day recorded-sale figures. No valuation/risk formula changes here.

Source inspection distinguishes two existing paths: `WealthsimpleAuthClient.Accounts` maps optional
`simpleReturns.amount`, `rate`, `referenceDate` and `asOf` into `WealthsimpleTfsaSnapshot`; the broker
panel already displays the amount/period/as-of context through `WealthsimpleAccountViewModel`.
The rate field exists in the source record but is not displayed there yet. `TradingAccountOverview`
separately reports that complete gain calculated from TraderVI records is unavailable. A broker-reported
return need not be manufactured from those incomplete local records. Preserve per-field availability,
currency, provider period, original as-of time and a missing-time notice; do not use retrieval time as
return valuation time. Percentage formatting, requested daily history and current native source values
remain to qualify; there was no broker read or accounting-method change in this design pass.

The operator's tentative Delphi Live/swappable-portfolios expectation is useful context, not an
installed assignment or a supersession of ADR-0085/0087. The proposed UI distinction is **engine**
(the rules-running component) and **selected portfolio** (its exact reviewed rule configuration).
The operator now says “I think switch between delphi live portfolios,” while asking for an explanation
of the multiple portfolios. Treat this as leaning toward Live's own configurations, not final policy
approval or a change to wider family/AI support. The October 18 recorded setup has three Live paper
accounts: the original operational paper book and ADR-0067's Normal/Portfolio rules only comparison
pair. The pair isolates daily admission gates while retaining other portfolio/data/risk rules; each
book has independent simulated financial state. The internal OperationalChampion label is a paper
role, not proof of real-account readiness. Current `TfsaLiveStrategyCandidate.SupportsCurrentPromotion`
excludes comparison-account/GateComparison snapshots, matching ADR-0068; three paper books therefore
do not establish three ready real-account choices. This is source/record explanation, not a new SQL or
running-host check. Preserve actual family rules and existing comparison restrictions unless separately
changed. Whichever configuration is selected, the real account retains its own capital, holdings,
gains, history and risk; adopting rules never imports paper financial state. No exact configuration,
assignment or switching change is agreed. The operator then explicitly defers portfolio choice and
selects Performance first. Their “three engines” wording is corrected again: one engine runs three
independent paper portfolios, two created for comparison. The deferred choice does not block UI work.

### Concepts, verification and implementation boundary

The conversation presents `tradervi-layout-directions.html`, with **Focus** and **Workspace** variants.
Each includes all four screens, local row selection, daily-lens changes and a labelled sample replay
mode. All account amounts, securities, assignments, performance and operational states are fictional.
The replay is an illustrative layout, not a backtest or reconstructed historical result. A copy and
standalone preview are retained in ignored `artifacts/ui-redesign-20261018/`.

Verification for all four concept files: fragment wrapper generation and Node JavaScript syntax checks pass. Browser security
policy rejected local-file inspection, so no browser-rendered layout, viewport or interaction test is
claimed. No app build/test suite was required or run for this concept/documentation-only increment;
there is no new dependency audit or full-solution/SSDT qualification. No WPF source, database, model,
broker workflow or operating host changed. Before implementation, verify the chosen concept at the
actual target window size and specify empty, stale, missing, blocked and replay states; keep actual
account/source context and consequential controls explicit. Full behavior coverage remains pending.

The starting repository housekeeping was committed/pushed at `65956789` after JSON parsing and exact
staged-diff review; the push also included 184 pre-existing local commits. Personal chat photos were
preserved locally and excluded through `.gitignore`. That push does not imply new validation of the
previously committed application history.

## Problem and outcome

**Immediate problem:** the operator cannot consistently tell what information Trading, Portfolios,
Delphi and Delphi Live show, particularly after adding fixed gate-comparison accounts.

**Parent problem:** navigation and information hierarchy have grown around individual components,
while the product now connects daily selection, intraday evaluation, independent paper accounts,
comparison evidence and approved real-account recommendations.

**Root goal:** see the main real account's earnings, understand independently running paper portfolios,
compare strategies meaningfully, and deliberately select the main strategy on the accepted path toward
separately designed automated Wealthsimple execution.

## Evidence from the UI before this implementation

Source inspection covered `PaperDashboardWindow.xaml` and the Portfolios, Delphi and Delphi Live
views. Existing synthetic WPF preview images were inspected; they are older fixtures, not screenshots
of the current running app or proof of its current state.

- Trading separates Recommendations from Tracked holdings and monitor. The latter places Ghost and
  Real totals together and gives data collection history substantial space.
- Portfolios puts holdings, candidate monitoring and operational controls beneath Account tools and
  saved activity. Gate pairs are a scope within Compare, which makes their importance easy to miss.
- Delphi presents published Picks alongside model probabilities and gates. Static descriptions still
  describe Breakout as a comparison journal and Continuation as the operational thesis; those labels
  need review against the newer independent-account and approved-strategy routing contracts.
- Delphi Live combines current watchlists, replay accounts and advanced research. Its name alone
  does not explain that Live describes intraday observation and a paper strategy.
- Dates, data freshness, account scope and reasons are available in places, but lack one consistent
  presentation across the workflow.

## Decision record

| State | Direction |
| --- | --- |
| Accepted user scope | Redesign Trading, Portfolios, Delphi and Delphi Live together; explore visual concepts along the way. |
| Existing accepted contract | Independent paper accounts, fixed comparison profiles, deliberate recommendation approval and separate real execution authority remain authoritative. |
| Implemented | Keep the four familiar names and give each a plain-language purpose. |
| Accepted user clarification / ADR-0068 | Trading opens on the main real account's overview: earnings, value, cash, holdings and the active promoted strategy. Attention is important supporting content, not the landing page's main purpose. |
| Accepted user clarification / ADR-0068 | Portfolios has clear boundaries from the main account, lets the operator watch and inspect paper portfolios, and provides direct access to reviewing an eligible portfolio as the main strategy. |
| Accepted user clarification; implemented | Portfolios lists every paper account in one strategy comparison grid. Gate profiles are row facts; matched profiles remain adjacent by default. Separate standard/gate-only destinations are superseded. Intraday links preserve the selected account. |
| Implemented | Delphi owns the saved daily publication, ranks, model evidence and daily gate explanations, with market-data date separate from saved time. |
| Implemented | Delphi Live owns intraday candidate progression and account-specific entry explanations, explicitly labelled paper. |
| Implemented | Common side navigation, readable tables and scoped details; operational controls remain available in secondary sections. |
| Superseded | The initial attention-first opening recommendation was rejected in favor of the main-account overview. The operator retained the overview itself. |
| Implemented evidence boundary | Total gain remains unavailable. Display reported value/cash, saved holding values, recorded sale gains over 30 Toronto calendar days and open-position gains separately. Show the actual strategy and start date. |
| Deferred | Complete-account earnings history, selectable return periods, strategy attribution and broker synchronization/execution. |

The first concept opened on matched pairs to illustrate the immediate example. The revised concept
opens on Trading's main account, as the operator requested. ADR-0068 records this accepted product
direction; the four-screen implementation follows the operator's subsequent authorization. The original visual is preserved.

## Main account and promotion — September 9 clarification

Trading is the enduring real account. Its assigned strategy can change; its financial history does
not restart or become the paper account's history. Show total account results independently from the
current strategy's identity and effective time. Any future performance attribution by strategy needs
a documented period and evidence contract, including the treatment of holdings crossing a change.

The revised mockup leads with account gain, with account value, cash and holdings value underneath.
A breakdown distinguishes realized gains (closed trades) from unrealized changes (open holdings).
The current strategy and the link to compare/change it are immediately visible. A compact attention
summary supports the page; urgent issues should also appear beside the affected holding or account.

Portfolios lists ordinary-paper and fixed-pair accounts together. A visible **Use as main strategy…**
action opens a concise review for an ordinary eligible strategy; it does not perform a one-click
assignment. The example review explains the existing paused/empty requirement because its fictional
target account has holdings. Fixed gate pairs currently cannot be promoted. The operator's desire for
quick access does not settle whether those rules should change.

Display the actual account source and execution authority. Today these are operator-reported account
facts and recommendations only. Wealthsimple connection and autonomous orders are the long-term
direction; this UI work neither implements nor authorizes them. A future connected design must show
account synchronization and broker-confirmed fills without treating recommendations as execution.

Full real-account earnings need a dedicated evidence contract. The existing account report/value and
tracked-fill records do not by themselves prove complete net contributions, fees, income or returns.
Preserve unavailable values until the required evidence is present. See the linked open questions.

## Explanation contract

Every screen should make its purpose and scope clear before presenting detailed numbers:

1. Identify the selected account, mode (paper or operator-reported Real), strategy and gate profile
   wherever those affect interpretation. A shared daily source is not a selected account.
2. Distinguish recommendation session, daily data date, publication/adoption time, completed-bar time,
   last successful evaluation and display refresh time. Show the relevant times with a timezone;
   do not let refreshing the screen make old evidence appear current.
3. Explain the current result in ordinary language. Preserve the underlying reason and identity in
   an inspectable detail view. Do not infer a specific entry failure merely from zero fills.
4. Distinguish daily admission from portfolio qualification, pending orders and completed fills.
   "Published," "eligible," "waiting," "blocked," "cannot evaluate" and "filled" are different facts.
5. Preserve account, source and date context on cross-screen links. If the target cannot show the same
   scope, disclose the change. UI selection must never change the account running in the background.
6. Make replay and historical evidence visibly distinct from the current session. An unavailable value
   is not zero; stale evidence is not fresh; a late partial day is not a complete comparison period.

Normal and Portfolio rules only appear as adjacent rows within each matched strategy pair by default.
Explain that the experimental profile bypasses daily entry gates while preserving portfolio entry,
data, sizing, exit and risk rules. Keep each account's independent capital and results visible; do
not present the sum of hypothetical accounts as the operator's actual capital.

Performance measures need explicit period, units, valuation time and coverage. A first-day position
count measures activity, not success. The initial concept intentionally supplies no invented returns,
current account values or price history. Later performance screens need actual supported metrics.

## Initial concept and verification boundary

The conversation concept uses the user's September 9 update and the documented 13:58 snapshot:
five pairs, CAD 1,000 per account, Shadow experimental holdings of 1/2/1/1 in Breakout Top 3/Top 5
and Continuation Top 3/Top 5, cash-only Normal controls, and 41 experimental Live candidates with no
Live fills. These are historical reference facts, not refreshed account data. Candidate examples
EX1/EX2/EX3 are explicitly synthetic. Real account state is unavailable in the concept.

The concepts are stored in the task's conversation visualization directory as
`tradervi-workspace-concept.html` (initial) and `tradervi-main-account-concept.html` (revised).
The revised Trading and ordinary-paper examples use explicitly fictional financial amounts, stock
identifiers and strategy assignments. They are layout examples, not reported financial facts. The
fixed-pair screen retains its separately labelled historical snapshot. This document is the durable
repository handoff.
The original design turn checked markup only. The subsequent implementation validation is recorded
below; fixture screenshots are not live financial records or proof of operational runtime state.

## Implemented scope

- **Shell / Trading:** `WorkspaceStyles.xaml`, `PaperDashboardWindow` and `TradingRecommendationsView`
  provide side navigation and the main-account landing. The pure `TradingAccountOverview` model and
  `TradingAccountOverviewPanel` show only matching Real-account records, evidence times, available
  gains and the actual assigned strategy. Reporting, recorded fills and prior approvals remain available.
- **Portfolios:** `PortfoliosView` and its view model expose All portfolios, Performance, Strategy review and
  Account activity. One grid contains ordinary paper portfolios and both profiles of every fixed pair.
  Daily gate behavior is a column; reference accounts are optional additions. Holdings, candidates and decision audit are visible in activity;
  setup and engine controls are secondary. `TradingPromotionDialog` retains the current guarded review.
- **Daily / intraday:** `DelphiView` and `DelphiViewModel` identify the daily market-data date and saved
  publication time. `DelphiLiveView`, its view model and pure `DelphiLiveWatchlistScope` restrict the
  current candidate display to the selected paper account; replay remains distinct. Account-name lookup
  cannot prevent the existing scheduled tick. Links labelled "Latest saved Delphi source" disclose
  that the destination is the latest publication, rather than pretending to open a historical source.

Total earnings remain unavailable because the ledger lacks complete cash-flow/cost/income history.
The 30-day sale subtotal requires recorded realized profit on every selected sale. Open gains require
matching saved marks for every selected-account holding. Reported NAV and cash are labelled as
operator reports, with stale-session or changed-ledger context. Saved position update time is distinct
from a guaranteed market quote time. Wealthsimple remains visibly disconnected/recommendations only.

## Initial implementation validation and remaining work

- WPF Release build passed with .NET SDK 10.0.401 into isolated
  `artifacts/workspace-ui-20260909/build/`; existing operational output was not replaced.
- Full Core suite: **827 passed**, zero failures/skips. Eight new presentation tests cover account/mode
  isolation, incomplete/stale/mismatched evidence, recorded sale boundaries and same-symbol observations
  belonging to different Live accounts.
- **14 offline WPF renders** cover the four primary areas, adjacent pairs and activity at 1280×820
  and 1600×1000, plus unavailable-account and strategy-review states. The compiled main-account panel
  loads successfully; other views use stripped XAML to avoid operational constructors/handlers.
  Binding diagnostics are clean. Fixtures verify five complete pairs/ten accounts, ordinary-paper scope
  exclusion and fixed-pair promotion disabled. Visual inspection caught and corrected gain formatting,
  row sizing, inherited text color and wrapping issues.
- Existing compiler warnings remain (unused exception local and test nullability). Test logs contain
  cached dependency-security advisories, including critical `System.Drawing.Common 5.0.0`; no fresh
  package audit or dependency upgrade was performed. The complete solution/SSDT was not built here.
- No operational app, database workflow, model training, market service or broker operation was run.
  The existing running host remains separate from this build. Updating it is a remaining operational
  step because starting the host can resume collectors and paper monitors.

Preview sources, logs and labelled synthetic images are in ignored `artifacts/workspace-ui-20260909/`.
Database-backed interaction, exhaustive keyboard navigation and all DPI/display-scaling settings have
not been verified by these fixtures. Full-account earnings, attribution and broker integration remain
explicitly deferred in [open questions](../reviews/open-questions.md).

## September 9 navigation/discoverability follow-up

This was an intermediate design; the separate gate-only destination is superseded below. The
collapsible sidebar remains accepted and implemented.

The operator's screenshot selected ordinary Paper strategies, so the separate gate-comparison accounts
were not visible. The shortcut and scope dropdown now say **Daily gates on / off**, with the shortcut
showing loaded bypass-account and matched-pair counts. The paired view retains the exact Normal and
Portfolio rules only profile names and explicitly labels **Daily gates applied** / **Daily gates skipped**.
Descriptions make clear that portfolio entry conditions, data checks and risk limits still apply.
This changes discoverability, not the account filter, profiles or trading behavior.

`WorkspaceStyles.xaml`, `WorkspaceNavigationState.cs` and the main window provide a collapsible side
menu: 178 layout units expanded, 56 collapsed, all nine named icon links retained. The toggle has
collapse/expand tooltips and accessibility names, keyboard focus feedback and the standard toggle
automation action. State lasts for the current window; it does not reset the page or selected account.

The Release build passed, with existing CS0168 warnings. Nineteen offline renders and automation checks
cover collapse/expand, retained selection, 122 extra layout units for content, labelled icons and the
five matched pairs; bindings remain clean. Visual checks include 980×680, 1280×820 and 1600×1000.
The preview uses compiled navigation styles and synthetic account data; no operational host/database/
external-service workflow ran. Core tests were not rerun for this presentation-only refinement.
Logs, images and isolated output are under `artifacts/workspace-navigation-20260909/`.

## September 9 unified portfolio list

The immediate problem was hidden gate-bypass accounts. The parent problem was using category
navigation where the operator expected one portfolio inventory. The root goal remains inspecting
every paper strategy and selecting the main real-account strategy with clear financial boundaries.
The operator explicitly preferred all portfolios in the strategy comparison grid. The separate gate-only
view is removed; **All portfolios** is the opening section. Standard, Normal and Portfolio rules only
accounts appear together, with a Daily gates column and full account names/tooltips. Matched rows remain
adjacent in default ordering; selecting either shows its own activity, pair identity and profile rules.

The grid expands with the account list and scrolls with the page, replacing the five-row fixed-height
view. The selected-account panel retains holdings/activity and guarded strategy review. An optional
**Include reference accounts** checkbox adds operator-reported references without changing the paper
inventory or comparison period. The original Gate comparison pairs / Daily gates on / off category
buttons and scope dropdown no longer exist.

Ordinary accounts are labelled Per strategy because their actual admission depends on their assignment
and current source; the UI does not infer Normal merely from absence of fixed-pair metadata. Fixed
Normal means daily gates Applied; Portfolio rules only means Skipped, while portfolio entry conditions,
data and risk checks still apply. Fixed pair promotion and editing exclusions remain unchanged.
Returns still use matching observed endpoints across the shared period; newer accounts remain
unavailable rather than silently switching to a different return period.

Validation: isolated WPF Release build and 15 focused Core comparison/gate tests passed. Nineteen
offline renders plus automation checks cover 15 paper accounts (five standard plus five complete pairs),
correct labels/default adjacency, ordinary strategy-name fallback, reference checkbox inclusion,
selection continuity, unavailable younger-account returns, fixed-profile promotion/edit exclusions
and menu collapse/expand. Bindings are clean. Existing compiler warnings and cached dependency-security
advisories remain; no fresh audit or complete solution/SSDT build was performed. No operational app,
database workflow or external operation ran. Sources/logs/images are in ignored
`artifacts/portfolio-unified-grid-20260909/`; the running session needs a rebuild/restart.

## Daily gains follow-up

The operator requested gains for each portfolio within a given day. The immediate problem is that
shared-period blanks conceal first-day performance; the parent problem is making independent account
activity understandable; the root goal is comparing paper strategies before approving the main
account's strategy. All portfolios now exposes a selected-day money/percentage gain alongside the
existing shared-period return. Details show the day's saved opening, ending value and evidence time.
Partial Live/Shadow values remain labelled, cash-only valued days show zero, and no pre-activation
history is inferred. [ADR-0068](../adr/0068-main-account-trading-overview.md#daily-portfolio-gains--selected-trading-day)
records calculation and data boundaries. This does not implement a new snapshot schedule or host.

## Review questions

1. What is the difference between a published daily candidate, an eligible entry and a filled position?
2. How should the screen show which paper account and gate profile a result belongs to?
3. Why must the evidence time remain visible even when the display has just refreshed?

## Manual market buys — September 10 design draft

**Status:** The original exploration below led to the operator's request to implement, with an
explicit editable share quantity for manual buys. [ADR-0075](../adr/0075-manual-market-buy-ticket.md)
records the accepted implementation and exact execution boundaries. This section preserves the
design's progression; the ADR supersedes its provisional alternatives. Offline tests and isolated
WPF renders passed. No live order or operational-host restart was requested or performed.

The implemented compact dialog opens from a single action for the selected stock across both Delphi
lenses, or a secondary **Buy more…** action for the selected broker holding. The operator's subsequent
UI review removed the initially implemented row Buy columns because they repeated the selection
action and distracted from reviewing existing holdings. The original row-button proposals below are
historical; the current presentation is recorded in ADR-0075.
Shares starts blank and accepts any positive whole quantity within the account checks. Optional ±10
shortcuts do not enforce ten-share lots. Manual orders remain operator-managed, and Orders retains
protected recovery records across restart. Portfolio execution and automatic selling remain deferred.

**Immediate problem:** the Delphi Picks strip asks for shares, an actual fill price, Ghost/Real mode
and a free-text account. It records an existing fill; it cannot initiate the newly supported market buy.
**Parent problem:** expose one understandable manual-order workflow consistently across WPF grids.
**Root goal:** deliberately buy a selected stock in the permanent self-directed TFSA, with reliable
order recovery and broker-confirmed results, before adding portfolio-directed trading.

### Evidence at the start of the exploration

- `DelphiView.xaml` contains Continuation/Breakout grids and the tracking strip. Its code-behind
  explicitly asks the operator to confirm a Real fill already happened. `DelphiViewModel` feeds the
  existing position-opening workflow; changing that button's label would not implement a broker buy.
- `WealthsimpleConnection` owns desktop authentication and serializes account, pending-order and
  security reads. It does not expose a submission/status workflow to WPF yet.
- `WealthsimpleAuthClient.SubmitMarketBuyAsync` implements the captured whole-share CAD market-buy
  mutation and checks instrument eligibility. `ReadMarketBuyOrderAsync` validates the order's scope
  and reports cumulative fills. Native submission from TraderVI remains unverified.
- The existing attempt marker contains hashes only. It fences duplicate transmission but cannot
  recover the reviewed account/security/quantity/request ID after restart by itself.
- The inspected account/security contracts expose no current bid/ask or timestamped quote. The
  saved Delphi source and operator-entered fill price cannot stand in for a fresh order estimate.

See [ADR-0074](../adr/0074-wealthsimple-market-buy-protocol.md) for the implemented broker contract and
[ADR-0073](../adr/0073-permanent-tfsa-and-whole-share-execution.md) for existing account/quantity rules.

### Decision record

| State | Direction |
| --- | --- |
| Accepted user scope | Explore and then implement the WPF overhaul and reusable quick market-buy actions, with manual buys and editable exact share quantities first. |
| Existing accepted contract | Permanent self-directed TFSA; positive whole-share orders; multiples of ten are a soft preference; broker acknowledgement is not a fill. |
| Provisional recommendation | A compact `Buy…` action in each eligible stock row opens one shared ticket. The row itself never submits an order. |
| Implemented UI preference | Compact dialog for a stable review target. A panel beside the grid remains a possible future presentation variant. |
| Provisional recommendation | Replace the editable tracking strip with a selected-stock summary and `Market buy…`, opening the same ticket. |
| Provisional recommendation | Keep `Record an existing fill…` and `Create Ghost position…` as separate secondary actions. A real order ticket has no Ghost/Real mode toggle or editable actual fill price. |
| Provisional recommendation | Show the verified self-directed TFSA as fixed account context. No free-text account routing. If account discovery is ambiguous, require explicit verified account selection. |
| Provisional recommendation | Treat a manual purchase as operator-directed and operator-managed. Display the source/date/direction, including Hold, without changing recommendations or implying strategy authority. |
| Provisional recommendation | Initial placements in both Delphi Picks grids; share the component with Trading and, later, other stock grids. Placement in a paper grid must still identify the real destination. |
| Deferred | Strategy-directed entries, portfolio promotion/activation, market sells, limit/cancel/replace and automatic exit ownership. These are not small extensions of the buy button. |

The provisional recommendations in this original decision table were adopted in ADR-0075, with
broker holdings included as the first additional grid. The existing ability to record a discretionary
Real Hold purchase informed the design; the new broker execution checks are now defined by ADR-0075.
No daily gate, ranking, policy threshold or portfolio strategy permission changed.

### Proposed ticket and states

The ticket contains the resolved stock/exchange/currency, verified TFSA, whole-share quantity,
timestamped reference price and source, estimated cost, current buying power and relevant existing
orders/holdings. The final action states `Buy {quantity} {symbol} at market`. This ticket is the explicit
order review; avoid a second generic confirmation box that repeats it. No implicit default-button or
double-click submission. Quantity shortcuts may add/subtract ten without excluding smaller quantities.
The mockup's twenty shares and prices are illustrative, not an accepted remembered quantity or price.

Show `Manual purchase · managed by you` and the source publication/direction. A Hold remains visible
as a manual departure from the saved recommendation. Preserve the source as context, not as an
eligible strategy entry or future automatic-sell assignment.

States are Draft → Checking → Ready → Submitting → Submitted → Partially filled/Filled, with explicit
Rejected, Blocked and Status unavailable outcomes. Any account, symbol or quantity edit invalidates
the reviewed draft. Freeze its resolved scope when submission begins. Grid refresh/selection cannot
retarget an open order; closing the ticket does not cancel a submitted order. Read-only status checks
can continue and the order remains accessible in Trading → Orders and after application restart.

When submission is ambiguous, reopen the same saved order. Do not offer a replacement or generate a
new request ID to retry. A null/delayed lookup is inconclusive. Submitted and partial quantities remain
distinct; cumulative fill reads replace an observation rather than add duplicate fills. Unknown broker
statuses remain unresolved. Terminal cancellation/expiry can still contain real partial fills.

### Shared implementation boundary

1. A reusable WPF buy button/template passes a stock reference and immutable source context to one
   application-scoped coordinator. Grids have no broker credentials, pricing rules or submit code.
2. A shared ticket view model owns validation, review and order state. The coordinator uses an explicit
   order interface through the existing connection owner, preserving serialized authentication and
   session revision checks. Authentication refresh must not retry a mutation.
3. Before transmission, save the complete reviewed intent and stable request ID in a protected,
   durable local journal. Define atomic writes, exclusive access, schema versioning and account/identity
   rebinding after restart. Retain the existing hash attempt marker as a separate transmission fence.
   Account objects are scoped to a verified client; recovery must revalidate exact identities, not
   deserialize them into trusted account objects or resolve a changed ticker as the same instrument.
4. Obtain fresh broker account facts, pending orders, holdings and instrument eligibility, plus a
   timestamped quote/reference from a verified supported source. Define the manual cash/risk policy
   and freshness bounds before enabling submission; no arbitrary buffer is accepted by this draft.
5. A submitted quantity market order has no guaranteed maximum cost. Estimated affordability and
   any cash cushion are preflight checks, not a price cap. Do not label an estimate `maximum spend`
   or size from proceeds of an uncompleted sale.
6. Reconcile order-specific broker fills and refresh account facts before showing success. Do not
   create a local Real fill from a submission acknowledgement, estimate or arbitrary user price.
   Defer automatic tracked-position insertion until broker fills can be linked idempotently and
   ownership/exit behavior is settled. Broker-confirmed manual orders and the refreshed account
   remain visible without claiming a complete synchronized account ledger.

First-version simplifications, subsequently implemented in ADR-0075: allow submission only within a verified
regular Canadian trading session; block stale/missing required checks, unknown local order outcomes,
unavailable pending-order reads and outstanding broker orders until reservation/conflict handling is
defined. Preserve applicable global buying pauses and financial holds. Paper-account-specific gates
must not accidentally become the real manual account's policy. These restrictions must be visible
beside the ticket action, with a reason, rather than silently disabling it.

### Acceptance criteria and verification

- Both Delphi lenses open the same ticket for the clicked row; later selection/refresh cannot change
  the reviewed stock. No order is transmitted by row selection, refresh, reopening, timers or startup.
- The account is broker-resolved. Shares are positive whole numbers. Missing price/account evidence
  never becomes zero or a fresh-looking estimate. Stale/changed checks invalidate readiness.
- A final explicit buy action saves the intent before one transmission. Double-click, concurrent
  tickets, storage failure, disconnect, timeout and restart do not create duplicate transmissions.
- Partial fills, rejection, no result and terminal-with-partial-fill cases display honestly. Repeated
  observations do not duplicate shares/cost. Broker failure text is sanitized.
- Real existing-fill and Ghost workflows remain accessible and distinct. Manual buys do not alter
  paper ledgers, activate a strategy or acquire automatic sell authority.
- Verify with focused tests using fake broker/clock/storage, protected-storage fixtures and isolated
  WPF renders at normal/narrow widths. Test restart recovery and selection/session races. Build the
  affected project without replacing the running host. A live broker test needs an explicitly chosen
  order; it is not routine validation for this UI work.

### Implemented sequence and remaining decisions

Compact dialog placement and operator-managed ownership were adopted. The captured broker quote and
regular-session metadata provide market evidence; quotes expire after two minutes and account/review
checks after thirty seconds. Estimated ask cost must fit the lower of CAD cash and buying power.
Outstanding broker orders, unresolved local orders and existing account/global protection block buys.
Protected intent/recovery and the shared coordinator are implemented. Both Delphi grids share one
selection action; broker holdings has a separate selection action. Other surfaces can reuse the same
command without enabling portfolio automation.

Native live acceptance still requires an operator-selected order. Cross-device recovery, resolution of
contradictory broker observations, complete account reconciliation and portfolio-directed execution
remain deferred. Exact behavior and offline validation are recorded in ADR-0075 and project status.

The concept is `manual-market-buy.html` in the conversation's visualization directory. Source symbols
and saved rankings illustrate the supplied screenshot; prices, balances and order outcomes are demo
values. The initial exploration changed only design documentation and that mockup. The subsequent
approved implementation added WPF/Core code, focused tests and isolated synthetic render fixtures under
`artifacts/manual-market-buy-ui-20260910/`. No live broker call, SQL operation, operational host restart,
model workflow or commit was performed. The running WPF app still needs a rebuild/restart.
