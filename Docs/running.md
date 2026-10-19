# Running TraderVI

## Read-only local database diagnostics

Use the operator-authorized [fixed installed helper](../Operations/ReadOnlyChecks/README.md) for
task-relevant local SQL reads. Submit reviewed SELECT-based SQL through the ignored mailbox, run
the documented constant command separately, and match the response's query hash before reading
private results. Do not broaden sqlcmd/PowerShell approval or use an unrestricted fallback.
The helper's installed code, SQL-user permissions and syntax policy enforce the boundary. Existing
chats need a Codex restart to reload the rule. Saved broker-logins/current-account API reads remain
separate; a database approval does not cover Windows-protected broker credentials. Installation
evidence and limits are in [project status](project-status.md) and ADR-0091.

## Read-only broker diagnostics

Use the operator-authorized [installed broker helper](../Operations/BrokerReadOnlyChecks/README.md)
for current TFSA discovery, balances/holdings, pending orders and daily-performance checks. Its
fixed command reads the saved Windows-protected login without refreshing or saving credentials;
only exact read requests can reach the provider. It saves encrypted observations and prints a small
status report. Missing/expired authentication requires the normal sign-in flow. Do not generate a
new mutable NativeRead/dotnet command or widen PowerShell approval for covered checks. Existing
chats must reload rules and use the documented command. History/quotes/other reads require a reviewed
extension; SQL writes, account changes and execution retain their separate authority. ADR-0093 and
[project status](project-status.md) record scope/validation; no desktop restart is part of this setup.

## Dots: one saved-input replay

The operator authorized one historical Dot review October 17; the Dot is ready. The prepared private
bundle is `artifacts/ai-dot-saved-review-20261017/` (ignored by Git). Attach only
`dot-review-input.txt` in the Dot chat and ask it to perform the single review specified in that file.
The attachment contains the current supervisor instructions, exact response schema and current v3
rendering of the saved October 1 15:05 UTC packet (15 choices/four eligible). No computer/plugin
connection or market-hours collection is needed for this manual file handoff. Do not attach the
private source packet, learning journal, credentials, manifest, old answers or future outcomes.
The replay preserves original dates/expiry/eligibility; its input is not a current trading packet.
The original call stopped at token admission; its rejected status is retained in the private manifest.

Save the Dot's exact returned JSON as `dot-proposal.json` in that bundle, then run from the repository:

```powershell
& artifacts/ai-dot-saved-review-20261017/Validate.ps1
```

The local helper verifies bundle/assembly hashes, validates the packet at its original as-of time,
and uses PowerShell `Test-Json` plus the existing `AiPortfolioReviewer.ParseAndValidate` method.
It does not invoke a client, initialize/write operational history or execute a proposal. Preserve the
raw answer if validation fails; do not silently repair it or rerun the Dot without discussing the result.
A valid `wait` is allowed; selection is not compulsory. Inspect reasons separately for unsupported
facts, unavailable-choice misunderstanding or future knowledge. Valid JSON/domain references do not
prove good investment judgment, timing reliability, unattended operation or paper execution readiness.

`Validate.ps1 -SelfTest` exercises this bundle's synthetic valid wait and rejection cases; October 17
passes ten checks including rejection of the historical packet for current use. The operator's Dot
answer subsequently passes schema/domain validation as `wait`, with 16 factual checks against the
saved packet. It returns a permitted two-minute interval and no rule overrides. Its recognition of
old scheduling narrative is correct. Review reasoning separately: `FreshAiReviewRequired` is an
expected pre-review block, not a permanent prohibition on choosing a strategy; the packet builder
excludes it from choice eligibility because this review supplies that attention. Other risk controls
still require current rechecks. This one wait does not test selected portfolio/version handling,
switching, future outcomes or automatic operation. `Prepare.ps1` is the one-off exporter; do not rerun over an existing manifest or update timestamps
to make the snapshot current. It read/checksummed the original journal without opening its writer or
changing it. Keep exported market/paper information and responses private and outside Git.

## AI review coordinator — prepared, not enabled

The Core coordinator is callable through `AiPortfolioReviewCoordinator.FromSavedPaperInputs`; it
requires the existing account risk reader/journal, durable schedule, learning journal and budgeted
Responses client. Supply reviewed calendar/checkpoint times, evidence ages, history window and the
current active portfolio/holds. Journal initialization is an explicit setup operation; the coordinator
does not create replacement history, start itself from desktop startup or submit trades.

SQL review storage migration `TraderDB/Migrations/20260930_042_AddAiPortfolioReview.sql` was installed
under explicit authorization September 30 after a verified backup. **Do not replay it or publish the DACPAC.**
Query `dbo.AiPortfolioReview` by ReviewId or PortfolioId.
Pending contains the exact input; Reviewed includes the validated answer; Rejected has no accepted
answer. These are historical records, not execution permission. Raw provider/cost evidence remains
in the private call journal under the same ReviewId. Preparation failures remain in the schedule.

If final SQL persistence fails, `PersistReviewAsync(reviewId)` copies the saved local disposition
without calling the AI. Keep the original local journals; do not reset them or retry the paid call ID.
Native SQL roundtrip/idempotency, rejected changes and recovered-answer saves passed September 30;
cross-process concurrency/crash qualification and connected AI review remain outstanding. The opt-in
`AiPortfolioReviewSqlTests` uses synthetic rows and rollback; it still requires explicit authorization.
The selected starting cash/holdings source is the existing Breakout 3 / PortfolioRulesOnlyV1 account;
a read-only lookup uniquely matched it. This setup choice does not change the active assignment or
restrict later AI portfolio selection. Process/user/machine API-key environment checks were empty.
Starting the normal desktop below does not enable this coordinator yet.

## Build and run the local desktop checkout

After the reviewed strategy branch is merged into the main checkout, close the running TraderVI
window and use the normal source command:

```powershell
Set-Location C:\src\TraderVI
dotnet run --project TraderVI.WPF -c Release
```

This builds and runs that checkout. It does not merge another branch/worktree or replace an already
running process. No separate installer or manual binary copy is needed. The September 25 integration
combines the strategy controls with the September 24 memory and broker-trade synchronization fixes.

In **Trading**, leave **Real account** set to **TFSA**. The Wealthsimple panel should show
**Strategy management** and **Account risk setup…**. With no approved managed assignment, disabled
stop/resume controls and an execution-not-enabled message are expected. First verify sign-in, fresh
holdings/cash/pending orders and refresh status. Risk setup can be reviewed, but native storage recovery
and risk-input qualification still precede the operational baseline and trading activation.

## Broker risk and managed-session storage (ADRs 0081, 0083, 0086)

The initial-baseline preparation, reviewed save/recovery workflow and SQL repository are implemented
in source, with an account setup screen included in the integrated checkout. Its native use is unverified and
there is no operational baseline yet. Installing storage alone does
not establish a baseline, change recommendation inputs or enable orders.

Migrations `035_AddBrokerAccountRiskBaseline`, `039_AddBrokerAccountRiskContinuation` and
`040_AddBrokerManagedSession` were individually reviewed and installed on September 22 at 23:34 Toronto
under explicit operator authority, after a fresh checksum-verified full backup and hash-matched
OneDrive-folder copy. Three empty tables and immutable triggers, trusted constraints and preserved
account records were verified. **Do not replay these migrations**, migration 034 or the legacy correction.
Never publish the DACPAC. See the [installation evidence](reviews/supervised-session-20260923.md).

The integrated strategy panel and recurring account/history persistence were observed September 25.
The isolated managed-session SQL rehearsal below passed; next qualify baseline/continuation repository
runtime recovery and verify the setup dialog. The workflow requires a
fresh saved linked observation and a consistent read of existing risk state; a failed read cannot be
treated as an empty history. Request recovery can read an already committed result without repeating
setup. Daily/cash-flow continuation, Daily Shadow protection handover and broker projection are source
tested; native risk evidence and operational verification remain. Core now supports explicitly approved
ongoing Daily Shadow assignments under [ADR-0085](adr/0085-portfolio-wide-ongoing-tfsa-management.md).
Older one-day approvals still expire, and ongoing scope does not replenish the approved purchase ceiling.

The source TFSA panel restores its saved strategy and buying mode after a successful linked-account
refresh at startup or during polling. **Stop new buys** and **Resume buying** save only that assignment's
purchase control; they do not approve a strategy or start an execution loop. An uncertain save requires
Refresh to read back the saved mode. Logout, failed/stale account checks and changed assignments disable
the controls. **Market sell all** retains its existing separate workflow. The separate default-off
execution host is now connected in source and an isolated morning build; native risk/order/source
qualification and first execution approval remain unfinished.

### Prepared Delphi Live Normal host

Open the [current native-review copy](reviews/delphi-live-native-qualification-20261019.md)
after closing the old desktop instance. In Trading, **Execution setup…** reads readiness and an
existing approved assignment. **Prepare first execution review** reconciles the account through the
existing import workflow, reads the exact current Normal definition and reports each required native
qualification. Both reviewed holdings must already be fully sold and initial risk setup saved.
For the explicitly approved October 19 limited trial, preparation recovers the exact import referenced
by that baseline and both original full-sale journal records, then writes protected scoped proof.
The subsequent order-review copy asks the operator to identify those two original sales by their
existing IDs; it never treats a later verification-position sale as a third opening holding.
The [prepared manual verification sequence](concepts/supervised-tfsa-opening-sequence.md#operator-review-bounded-native-order-verification-october-19)
includes returning the test purchase to cash **before** initial setup. Review its remaining native
qualification conditions before choosing to trade; preparing that sequence sends no orders.
It replaces only production cash-event/opening-value checks; the other four native and five scoped
checks remain. Missing independent proof leaves approval blocked. It never creates a baseline or
issues production qualifications. The later explicitly accepted native first-day contract adds
**Capture native recovery checkpoint** after all verification trades and saved cash-only initial
references. Close/reopen the same executable, then **Prepare first execution review** reads the same
four IDs and unchanged baseline/Read/Find, verifies current source/storage and publishes all four
native/five scoped references only on complete actual observations. The protected packet is explicitly
first-session-only. Partial/cancel/uncertain automatic outcomes latch an order-review hold that survives
restart/Resume buying and prevents further orders/residuals while original-ID reads may recover facts.
Purchase permission ends at the reviewed close, with no next-day buys;
ongoing assignment does not renew it. Older scopes retain their original six production requirements.
**Approve reviewed assignment** is enabled only for a complete compatible review; it rechecks source,
evidence and account before saving through the existing SQL workflow. Approval sends no order.
**Enable reviewed host** is a separate action for the matching saved Normal/account/build.
No host permission is installed by this build. Default-off polling makes no host-driven SQL/broker
request. A previously enabled exact permission resumes regular-session checks without clearing a
stopped buying mode, risk hold or original order. Changed builds/assignments require review.

**Stop execution host** is available without sign-in or a selected account. It finishes a running
cycle, disables saved host permission and preserves the assignment/orders; **Stop new buys** remains
the separate control that retains ordinary exit cycles. The setup view names missing references/
approval and never treats login, installed tables or synthetic tests as native launch qualification.
The built copy has no approved actual assignment yet; it is not ready for automatic TFSA orders.

The read-only qualification consumer expects `execution-qualification.dat` beside the protected
manual-order journal under `%LOCALAPPDATA%\TraderVI\Wealthsimple\ManualOrders`. It contains a
Windows-protected `BrokerExecutionQualificationBundle` with exact binding/strategy/build/session,
reviewer/time/expiry, native proof references and the optional first-day scope. Each evidence reference
must be an absolute file path whose bytes match `EvidenceHash` (SHA-256). The approved native first-day
producer above writes this packet from complete actual observations; it is not a general-production
issuer. Never populate it with synthetic tests, installed-table counts or operator checkboxes as
substitutes for real proof.

## Broker account history import (source milestone, September 16)

**September 24 source extension (ADR-0084):** after separate installation of migration 040 and the
updated desktop, signed-in TFSA refresh also synchronizes supported completed broker orders into real
positions and trade history. The September 25 integration uses the agreed five-minute cadence, including while another tab is
selected, and on **Refresh Wealthsimple**. The broker panel reports success, pending work or a blocked
reason. Closing/signing out stops these checks. The first link still requires review; already-linked
history is checked automatically. Each saved observation can refresh account-value evidence while
the atomic trade projection applies repeated cumulative order totals only once. One shared read supplies
the display and saved history; the prior two-minute timer is removed. Failed reads use the existing
5/10/20/30-minute retry backoff. First-link/manual approval still rechecks its reviewed facts separately.

The new import/ledger changes commit together. Existing imports and matched legacy trades stay intact;
partial sales retain their position, full exits close it and later purchases create a new identity.
Cash/risk reports are not fabricated: stale reported values/advice become unavailable until reconciled
or reevaluated, and broker balances remain visible separately. Pending or ambiguous orders, corrections,
fractions, withholding and sub-cent amounts require attention. No trade is inferred just because a
holding disappeared. The September 24 resolved-order extension was installed and broker-verified;
the combined five-minute/control path has September 25 native empty-assignment and recurring-import
evidence. Longer coverage, rate/failure behavior and populated assignment recovery remain unverified.

Two independently applied scripts have the historical numeric suffix 040:
`20260923_040_AddBrokerManagedSession.sql` and `20260924_040_AddBrokerTradeSync.sql`.
Use their full filenames/table names to distinguish them. Both scripts remain byte-for-byte unchanged;
source integration adds no migration and is not authority to replay either one.

After a planned WPF rebuild/restart, open **Trading → Main account → Import account history…**.
The first invocation reconciles the owned broker account and previews matches to recorded TFSA trades,
new orders, current account facts and any blocks. **Link TFSA and save history** rechecks the observations
and saved ledger before atomically saving the account binding and history. After linking, opening this
window or using **Refresh and update saved history** updates supported observations. There is no
unattended broker polling timer in this milestone.

Missing migration 034 permits preview but disables saving. Review
`TraderDB/Migrations/20260916_034_AddBrokerAccountImport.sql`, obtain a fresh verified backup and explicit
installation authority before applying it manually; never deploy its DACPAC. The migration adds empty
history tables and does not link an account or enable trading. Native history calls remain to verify.

Migration 034 was installed and verified on the current TraderDB at 10:08 Toronto on September 16,
after the separately authorized verified backup. Both tables remain empty and the running WPF instance
was not restarted. Do not replay this migration against the installed schema. Restart WPF when ready
to expose the new source controls; native reconciliation/import verification is a separate next step.

If reconciliation stops with `UnsupportedResponse`, capture the failed-read description and
`Diagnostic:` line from the import window. These identify the request, response field, or safe
HTTP/GraphQL category/query location without account identifiers or raw server messages. A failure
before the preview leaves saving disabled. The September 16 follow-up identified a missing/invalid
`status` value. The reader now preserves null/empty activity-feed status as checkpoint metadata,
while requiring the actual historical order status. This correction requires a rebuild/restart and
another preview attempt. Later diagnostics identified EXPIRED, then CANCELLED, with absent execution
details. The latest reader retains an empty expired or cancelled order only when average price and every
other selected execution field are explicitly null and activity reports no fill. It records that the
broker quantity was blank and adds no shares or proceeds. Partial fills and previously saved fills
remain protected. Rebuild/restart WPF and retry **Refresh preview** to verify the native response;
no new migration is needed for this fix. The cancellation screenshot also reports absent average price.
A successful native import is still unverified. Do not omit orders or create a cancelled/expired test
order. See the [capture reference](concepts/wealthsimple-account-history-contracts.md).

The operator subsequently reached the 32-order preview. Latest source supports completed historical
ETF orders and verified Canadian listing aliases, without enabling ETF strategy orders. Rebuild/restart
and reopen the import to apply these fixes. An amount mismatch now shows TraderVI and Wealthsimple
values side by side below the order grid; scroll the window if needed. Saving stays disabled while a
financial difference remains. Review that concrete comparison before any correction; repeated refresh
does not change the saved ledger. No migration is required for these compatibility/display changes.

If an import response is uncertain, refresh to inspect saved history. The repository retains request
receipts and uses each order once per observation. Do not sum cumulative orders across snapshots or
delete saved records to recover. Ambiguous/split-fill matches, changed legacy history, broker corrections
or a different account identity require explicit resolution; there is no force-match/relink shortcut.

The first native TFSA link and 32-order save were confirmed in SQL. Its subsequent "Saved broker import
identity or content is inconsistent" error was a timestamp-precision bug: the default SQL parameter
rounded the snapshot while the saved JSON kept all digits. Latest source writes precise timestamps and
reads the exact older encoding without rewriting history. Rebuild/restart WPF, then reopen **Import
account history…**; a linked account will refresh/update saved history. No migration, database repair
or second account link is needed. Other identity/content errors still require investigation.
The operator confirmed the rebuilt native refresh at 22:06 Toronto on September 16: history saved,
32 cumulative orders, all already saved and no remaining import blocker. This checkpoint is complete.

Saving broker history preserves the existing tracked ledger and does not activate or change a strategy.
The engine's account inputs, risk baselines and inherited-position protection are the next integration
boundary. See [ADR-0080](adr/0080-reviewed-broker-history-import.md).
Starting from a verified current-account baseline with optional older history is a proposed next
design, not a currently available mode. It would still need ongoing synchronization and explicitly
preserved holdings, costs and account/position protection; it is not a database reset.

Background development uses isolated artifact build directories. The operator allows coordinated
restarts when useful, but Delphi Live records interruptions as coverage gaps and requires fresh entry
confirmation; prefer grouped changes outside collection when practical.

## Market-order client reference

`Core/WSTrade/Orders` and `WealthsimpleAuthClient.MarketBuys.cs` implement a whole-share market-buy
request and separate fill-status read from the operator's captured browser transaction. The WPF manual
ticket is now wired through the protected workflow in [ADR-0075](adr/0075-manual-market-buy-ticket.md).
Promotion activation and automatic engines remain separate. Market buys have no limit-price cap.
Do not call submission as a connectivity probe or replay a HAR.

The client also provides `WealthsimpleMarketSell`, `SubmitMarketSellAsync` and
`ReadMarketSellOrderAsync` under [ADR-0076](adr/0076-wealthsimple-market-sell-protocol.md).
It submits whole-share `SELL_QUANTITY` / `MARKET` / `DAY` orders and reads cumulative actual fills.
Buy and sell share the durable attempt fence; never change side or generate a replacement request ID
to recover an uncertain order. Sell checks `sellable` separately from `buyable`.
The WPF holdings workflow now calls it through the protected coordinator in
[ADR-0077](adr/0077-manual-holdings-market-sell.md). Native sell acceptance remains unverified.

## Manual sells from Wealthsimple holdings

After rebuilding/restarting WPF, open **Trading → Main account → Your Wealthsimple holdings**.
Each supported CAD equity row has **Market sell all** beside its share count. **One click sells the
entire displayed whole-share holding**, following fresh broker checks; there is no quantity ticket
or second confirmation. Market execution may differ from the displayed value or checked bid.

If the quantity changed, refresh holdings and click again for the new displayed quantity. Fractional
holdings require Wealthsimple. The app requires a fresh regular-session quote and no pending broker
or unresolved local order; after-hours clicks do not queue sales. Buying pauses and cash shortages
do not prevent an otherwise eligible manual sell.

The result window shows actual fills and proceeds when available. **Manual sells…** reopens saved
history; **Check sell status** reads the original order and remaining holdings. An uncertain order or
lagging holdings update keeps new manual orders blocked. Do not remove saved records or attempt
markers to retry. Closing the results window does not cancel the order. Sell records share the
Windows-encrypted `ManualOrders` directory with buys and remain available after restart.

**Limit sell…** opens a quantity/minimum-price preview. Its submission button is disabled; limit
placement still needs a verified broker contract. Use Wealthsimple to place a limit sell today.
Manual sales do not close SQL tracked positions or activate automatic strategy exits.

## Manual market buys in WPF

After rebuilding/restarting WPF, select a stock in either **Delphi → Picks** grid and use the single
**Manual buy…** action below the grids. To add to an existing broker position, select it in
**Trading → Main account → Your Wealthsimple holdings** and use **Buy more…** below that grid.
There are no per-row Buy buttons; selection alone never submits. Sign in if necessary,
select the verified self-directed TFSA if more than one exists, and enter the exact whole-share
quantity. **Refresh checks** loads the reference ask, estimate, cash/buying power, holdings and orders.
The explicit **Buy N SYMBOL at market** button sends a real order. Quantity starts blank; ±10 is optional.

The initial version requires an open regular broker session, a two-sided quote no older than two
minutes, account checks no older than thirty seconds, sufficient estimated CAD cash/buying power,
and no pending broker or unresolved local order. Applicable global/Trading pauses and TFSA financial
holds block submission. A price increase during final checks returns an updated review for another
explicit click; estimates never guarantee the fill cost.

**Manual orders…** is available beside Delphi's selected-stock action and in Trading's broker panel.
It reopens the saved order and reads cumulative broker results with **Check order status**. After an
uncertain submission, retain that order; a missing result does not permit replacement. Closing the
window does not cancel it. Filled/partial shares and actual costs are broker observations. Manual buys
do not activate portfolios, assign automatic selling or insert a tracked position automatically.
**Record an existing fill…** and **Create Ghost position…** retain their separate legacy workflows.

Full intents/results are Windows CurrentUser encrypted under
`%LOCALAPPDATA%\TraderVI\Wealthsimple\ManualOrders`; the existing `OrderAttempts` folder holds the
transmission fences. Do not remove either to retry a trade. Corruption or contradictory observations
requires recovery/reconciliation and blocks new buys. These local records are not a cross-device or
complete-account transaction ledger. Native live submission still requires the operator's deliberate
first use; offline tests do not prove broker acceptance.

For the supplied reference, open the ignored local
`artifacts/wealthsimple-market-buy-20260910/README.md` index. Each GraphQL exchange has separate
request/response JSON files; stable redacted IDs preserve relationships. Financial values remain private.
The original HAR retains every byte. See [ADR-0074](adr/0074-wealthsimple-market-buy-protocol.md) for
acknowledgement/fill semantics, attempt markers and remaining integration work.

## TFSA strategy transition preview

**October 18 Trading UI increment (Release rebuilt/launched at operator request):** Trading opens with compact
Today/Overall performance and a four-column holdings table beside the selected holding panel. Today's
follow-up reads native Wealthsimple `ONE_DAY` / `REGULAR` simple returns through the existing signed-in
connection and five-minute refresh. Latest-point amount/rate use the previous-close reference and
original valuation time; weekends/prior-date responses have a dated heading. Missing daily values stay
unavailable without using an older point or Overall. Daily-read failure leaves loaded balances/holdings
available; an overdue receipt is disclosed. Overall retains its supplied period/as-of context.
Shares/average cost and the existing Buy more / Sell all commands are in the panel.
The daily-reader follow-up is built/tested in source, but installation/restart is explicitly deferred
until concurrent strategy-switching work is ready; the earlier running build still shows Unavailable.
The strategy dropdown opens the existing transition review for that exact source; it does not change
the saved assignment or start execution. Unsupported comparison portfolios remain disabled. On return,
the dropdown restores the actual saved strategy. Account connection/import/risk/stop-buying and pending
order tools remain below; recorded-account recommendation/report/history controls are in **TraderVI
records and recommendation tools**. No other tab has been redesigned. Build, 194 focused tests and
four offline synthetic renders pass. The normal Release app launched at 22:15 Toronto and was responsive
at 22:16 (PID 20036). The operator can now review Trading; launch/offline checks do not qualify native
Trading bindings, current broker data or strategy execution.

**September 14 / account reconciliation:** **Reconcile account…** on the main Wealthsimple account
reads the selected owned TFSA's activity/order history and compares reconstructed shares and book
values with fresh holdings. It saves protected observations locally and shows missing/conflicting
history, partial-fill costs and fractional exceptions. Complete the existing manual-order status checks
when a saved order is unresolved. A failed or changing read saves no partial result. Historical
conflicts remain blocked for explicit recovery; deleting journals is not a recovery procedure.

Matching holdings do not establish complete cash-flow history, account loss baselines or execution
authority. The check does not write SQL positions or send orders. Initial native validation of the
new history query remains separate from the offline tests. Account observations are appended under
`%LOCALAPPDATA%\TraderVI\Wealthsimple\ManualOrders\account-<hash>-<sequence>.reconciliation` using
the same exclusive lease and Windows protection as manual orders. Preserve that history.

After native Wealthsimple sign-in, open **Trading → Main account**. Refresh reads balances, holdings
and paginated pending orders. **Review TFSA strategy transition** ranks Delphi Live paper sources on
their latest common saved day, with partial results labelled. Choose a source, enter a purchase ceiling
and select **Reconcile account and prepare review** to see the exact strategy version and proposed
retained/exit/await-entry holdings. The ceiling defaults to zero and never includes anticipated sale
proceeds. Paper-held symbols must resolve to unique CAD TSX/TSXV broker instruments before an
overlap is accepted. Fixed-gate comparison profiles remain unsupported by real strategy approval.

The review now requires reconciliation, stable broker cash/holdings and an unchanged source portfolio.
The accepted first execution version retains overlaps, uses regular-session market sells for required
non-overlaps and waits for confirmed exits to reconcile before new buys. This screen remains a preview:
it does not activate a strategy, change SQL records or send orders. Broker-to-SQL binding, cash/risk
baselines, inherited protection and the durable execution coordinator remain unfinished. Existing
recorded-account recommendation approval continues to use its existing gates. See ADR-0079.

## Wealthsimple authentication prototype

The isolated `Tools/Wealthsimple.Auth` console host can verify a user-completed browser session
from a local HAR. It does not start the trading app or connect its dashboards. See
[authentication commands and limits](concepts/wealthsimple-authentication.md). A successful direct
token-info check is verified. WPF's **Wealthsimple** sidebar button now opens native email/password
inputs and prompts for 2FA when required. Browser verification is a conditional fallback, and saved
sessions use optional Windows encryption. Native login is operator-confirmed; live refresh rotation
still needs verification. Green denotes authentication only, not trading readiness.

On **Trading → Main account**, the **TFSA** page loads the self-directed broker account after sign-in.
Use **Refresh Wealthsimple** for a new snapshot. Multiple eligible self-directed TFSAs require a choice;
managed accounts are excluded. The lower expander contains TraderVI records and the active strategy.
Broker reads do not update SQL reports, recorded fills or recommendation inputs. See [ADR-0072](adr/0072-wealthsimple-tfsa-account-display.md).

## Historical Practice

The **Practice** tab opens a saved `frames.ndjson` for labelled playback with a simulated Toronto clock,
Play/Pause, Step, speed controls, daily picks and four independent accounts. Generate a run with the
dedicated `Practice` console project. Acquisition is explicit and separate; `run` is offline and never
publishes operational results. See [input preparation and commands](concepts/historical-practice-implementation.md).
The first trial defaults to August 10–September 4, 2026, CAD 1,000 per account, and accepts missing data.
No actual starter dataset acquisition/replay ran during implementation.

## Central Settings

In the rebuilt app, open the top-level **Settings** tab. Delphi, Delphi Live, Trading and Portfolios have
shortcuts to the same relevant editor. General & operations contains the local Ghost-exit preference;
system sections contain their supported strategy fields; Portfolios & accounts lists existing targets.

Choose **Pause new buys** on the system page or Settings. Use each holding's **Sell** action to request a
simulated close, or **Record sale** for an actual completed Real fill. Requests retain their audit records
and wait for eligible market evidence. Exits continue while paused. No broker order is sent.

When the affected family has zero holdings and pending orders, its strategy fields unlock. Select a saved
version or template, edit related sections, name the draft and enter a reason, then **Review and save
version**. Navigation retains the combined draft in memory. Saving changes no assignment. Select the saved
version and target, then **Review assignment**. Assignment remains paused; choose **Resume buys** separately.
Existing account risk holds remain in force. If reevaluation fails, the assignment stays active and the
failure is reported for retry.

Daily Delphi selects complete preserved four-model sets and eight gates; it does not mix arbitrary
individual candidates. Assign also starts an official local-data run. Live and Shadow use deterministic
policy editors, not the daily ML-model selector. Trading limits belong to the strategy; financial facts
belong to accounts. The initial editor does not expose fixed calendar, evidence or Shadow lens/slot
contracts as tunable controls. Shared Daily changes require all dependent families paused and empty;
Live/Shadow entries wait for compatible frozen daily picks in a fresh session after a daily source change.
Manual Live assignment or operator intervention pauses automatic research promotion.

Before operational rollout, obtain authorization, create/verify a full backup, close old hosts and apply
`TraderDB/Migrations/20260907_027_AddCentralSettings.sql` manually with SQLCMD error stopping. Old hosts
do not participate in the new settings fence. Building a DACPAC does not apply this migration. Without
028, the revised trading-rule editors and new controls are read-only. Apply
`TraderDB/Migrations/20260907_028_AddTradingControls.sql` after 027 with a fresh verified backup and SQLCMD
error stopping; close older hosts before rollout. The migration creates no pauses, requests or assignments.
Migration 028 was applied with a fresh verified backup and preservation checks on 2026-09-07; do not
reapply it. The updated Debug and Release desktop outputs are ready for the next app launch.
Migration 027 was applied with a verified backup on 2026-09-07; do not reapply it. The updated Release
desktop app is installed in the build output and was launched successfully. Settings is in the main tab
bar between Scorecards and Project Docs. No settings assignment was seeded during rollout.
See [ADR-0060](adr/0060-central-settings-and-scoped-configuration.md), the
[system map](concepts/settings-system-map.md) and the
[implementation and rollout review](reviews/central-settings-implementation-20260907.md).

TraderVI currently operates in advisory and ghost-execution modes. None of these commands should be used as routine code-change validation: several call external services, write SQL Server, train models, or create model artifacts.

Read `Docs/project-status.md` before restarting a workflow after a long pause.

[ADR-0055](adr/0055-independent-strategies-to-approved-live-execution.md) records the path from independent
paper strategies to approved real-account recommendations and future Wealthsimple execution. The desktop
now implements manual recommendation promotion under [ADR-0062](adr/0062-complete-strategy-recommendation-promotion.md),
as described below. Delphi Live's separate research promotion control changes its own paper policy only.
Hercules saves replacement model candidates without selecting them. Training still writes private artifacts
and SQL and needs explicit authorization; model selection is a separate reviewed operation under
[ADR-0057](adr/0057-preserved-model-sets-and-explicit-selection.md). Strategy-directed broker execution is not implemented; the separate manual-buy ticket does not activate a strategy.

## Manual strategy promotion to Trading

Migration `20260907_029_AddTradingPromotion.sql` was applied on September 7 after a verified backup and
preservation checks; do not reapply it. The updated Debug and Release desktop outputs are ready for the
next launch. Rollout did not launch the app or approve any strategy.

1. In **Trading → Recommendations**, load the Real account. Reconcile its actual holdings in **Tracked
   holdings**, then use **Report current account state** to confirm current CAD cash, account value,
   session opening value, highest closing value and outstanding orders. These must come from the actual
   account. Report again each session and after a fill or external cash change.
2. Satisfy the existing configuration requirements: **Pause new buys** and no holdings or pending orders
   in the affected tracked family. Promotion also requires the target Real account to have no holdings
   or outstanding orders. Record sales only when they have actually happened; approval does not close holdings.
3. In **Portfolios**, select the supporting paper account and review its complete strategy for Trading.
   Inspect the target account, exact strategy/model/policy versions, evidence limitations, previous source
   and any operational blockers. Performance evidence is advisory for this manual decision.
4. Enter your reason and choose **Approve this strategy for Trading**. The approval records you as reviewer
   and takes effect when its transaction commits. Paper holdings and histories stay independent. Return to
   **Trading → Recommendations** to see the approved source and subsequent results; **Resume buys** separately
   when ready. New entries require current reported account facts and compatible frozen daily inputs.
5. Report actual purchases and sales separately after they occur. A recommendation sends no broker order.
   To switch back, select a prior approval in the history and **Review selected strategy again…**; the same
   prerequisites apply and a new approval preserves the previous history.

At rollout, all four Shadow accounts produced valid reviews. TFSA still has reported holdings, no current
account-state report, and Trading buys are not paused. Delphi Live has no initialized paper ledger yet.
These are remaining operator prerequisites, not evidence-performance thresholds. The reviewed calendar
loads; actual approved evaluation and live source capacity still require operational observation.

## Safe validation commands

Core tests:

```powershell
dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --verbosity minimal
```

Focused project build after restore:

```powershell
dotnet build <project.csproj> --no-restore
```

The complete solution must be built with installed Visual Studio MSBuild plus SSDT because it contains `TraderDB/TraderDB.sqlproj`. A successful SQL-project build produces a DACPAC; it does not deploy the database.

## Isolated managed-session SQL rehearsal

`BrokerManagedSessionSqlTests` is skipped during ordinary tests. Run it only with authority to create
a separate synthetic database and interrupt its own test connection on local SQL Server. It never
opens `TraderDB`, loads broker credentials or constructs a broker client. It creates and retains a
database named `TraderVI_BrokerRecovery_<random GUID>`; the detailed test output records its name.
The SQL identity needs database-creation, session-inspection and connection-termination permissions.

```powershell
$env:TRADERVI_RUN_BROKER_SQL_REHEARSAL = '1'
try {
    dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --configuration Release --artifacts-path artifacts/sql-recovery-tests --filter FullyQualifiedName~BrokerManagedSessionSqlTests --logger 'console;verbosity=detailed'
} finally {
    Remove-Item Env:TRADERVI_RUN_BROKER_SQL_REHEARSAL
}
```

It executes the actual managed-session repository against the canonical session table/trigger,
with synthetic ID-only parent tables. It verifies precise readback, competing leases, buying-mode
restoration, interrupted transactions, uncertain acknowledgements, original order IDs and reconciled
partial exits before purchases. The interruption targets only a positively identified connection
owned by this test in its new database. No migration or automatic database cleanup runs.

This qualifies the session journal under these scenarios, not baseline/continuation repositories,
production schema compatibility, power-loss recovery or native broker fills. The separate production
risk/setup and execution approval requirements remain. See the [dated result and limitations](reviews/supervised-session-20260923.md#native-desktop-and-isolated-sql-recovery--september-25).

## Database prerequisites

Runtime repositories connect to the local `TraderDB` SQL Server database. Before running a producer or evaluator:

1. Confirm required tables exist.
2. Confirm the backup requirements in `Docs/database-operations.md` are satisfied.
3. Apply only an explicitly authorized dated script from `TraderDB/Migrations`.
4. Verify the changed object and preserved data after execution.

Never publish the DACPAC. `TraderDB.sqlproj` targets SQL Server 2019 for build-time schema validation and blocks Deploy targets; it is not a live deployment mechanism.

After a successful Hermes data update, Hermes automatically creates and verifies a full database backup, then copies it to the approved OneDrive destination with SHA-256 verification. `TraderDB/Operations/Backup-TraderDB.sql` remains the manual fallback and the pre-migration backup tool.

## Guarded nightly pipeline

ADR-0046, as corrected by ADR-0058, schedules one deterministic pipeline at 00:30 Toronto/Eastern every calendar day:
Hermes, then Delphi, then Athena. Running after midnight gives Delphi the new recommendation date while it
uses the prior completed TSX session. This schedule is recurring authorization for these exact three
programs and their documented effects; it grants no authority to train models, apply migrations, deploy a
database, run Sandbox probes, start WPF monitoring, invoke Oracle, or place a broker order.

Install the Windows task once, or rerun the installer when changing its schedule or task settings:

```powershell
./Operations/Install-TraderVINightlyTask.ps1
```

The task invokes the repository runner and configuration directly. ADR-0078 captures the latest tracked
and untracked non-ignored source into a verified private copy at run start. Later edits belong to the next
run. It restores the reviewed Core/Hermes/Delphi/Athena lock files with `--locked-mode` into a dedicated
cache, then builds the three Release projects inside that copy. Neither intermediate nor executable files
are shared with development builds. Source and runner edits apply without reinstalling the task.

Capture and transient restore failures get at most three preparation attempts, with ten-second pauses.
A restore attempt has five minutes; a thirty-minute preparation budget is checked before each process.
Compiler errors, missing/changed dependency locks and package integrity failures stop before any stage.
The copied source is verified after building and executable hashes are checked before every stage.
Commit and working-tree provenance passed to Delphi identify the captured source.

Run offline regression checks or actual build-only validation without starting operational programs:

```powershell
./Operations/Test-TraderVINightly.ps1
./Operations/Invoke-TraderVINightly.ps1 -BuildOnly -StateDirectory C:\src\TraderVI\artifacts\nightly-build-check
```

Build-only status is separate from operational status and records `Prepared`. Run copies and their full
source manifests live under ignored `artifacts/nightly/<run-id>/`; the dedicated package cache is
`artifacts/nightly/packages`. Cleanup is currently manual. When dependency declarations change, generate
and review the affected Core/Hermes/Delphi/Athena `packages.lock.json` files with `dotnet restore
--use-lock-file` before the next night. Restore uses `Operations/nightly-nuget.config`; package-security
audits are separate from the unattended build and must be reported independently.

The authorized September 23 one-time storage cleanup removed compiled binaries from older artifact
builds while preserving their source snapshots, manifests and evidence. Directories with
`COMPILED-BINARIES-REMOVED-20260923.md` require rebuilding before reuse; historical executable hashes
remain evidence of the original run, not proof that its binaries are still present. The latest two
nightly builds and package cache were retained. No automatic retention policy was installed.

The task uses the current user's interactive token, stores no Windows password, wakes the computer, and
starts when available; the user must remain signed in.

Read the latest status without starting any operational program:

```powershell
./Operations/Get-TraderVINightlyStatus.ps1 -IncludeLogTail
```

The runner writes atomic `status.json` state and per-run logs beneath
`%LOCALAPPDATA%\TraderVI\Nightly`. `Succeeded` means the current source built and all three stages passed.
`Attention` includes Athena exit code 2 or a Delphi run degraded by an actual evidence limitation; a dirty
working tree by itself remains recorded provenance and does not change the run audit state. `Failed` includes build failures,
unstable source capture, dependency-lock failures, post-build artifact changes, timeouts, Hermes failures, invalid Delphi
provenance, or any unexpected exit code. Hermes failure stops later stages; Delphi failure
does not prevent Athena from maturing earlier evidence. Only source capture and transient dependency restore may retry before any stage starts. Neither the runner
nor Task Scheduler retries a failed operational pipeline automatically. A same-date completed run is suppressed unless an operator explicitly uses
`-Force` after reviewing it.

The daily 07:00 Codex supervisor is read-only. It may inspect `status.json` and its referenced log, explain a
problem, and recommend the next safe action. It must not launch or retry a program, modify SQL, call a market
service, or repair data.

## DataAudit — read-only local data-quality scan

**Project:** `DataAudit`
**Typical schedule:** after a successful Hermes run, weekly or whenever universe quality is in question

DataAudit scans every local symbol and the core `DailyBars` / `StockSectorMap` / `SectorIndices` relationships. It detects stale active symbols using completed XIU sessions, missing mappings, likely stock/fund misclassifications, malformed or duplicate bars, and orphaned data.

It performs no external calls and no database writes. Findings about current listings or security type are candidates for official-source review, not automatic corrections. See `Docs/data-audit.md` for the checks, thresholds, and exit codes.

```powershell
dotnet run --project DataAudit
```

## Hermes — market-data collection and maintenance

**Project:** `Hermes`
**Entry point:** `Hermes/Program.cs`
**Typical schedule:** after market close

Hermes currently:

1. Loads the TSX symbol universe.
2. Downloads missing historical daily bars from TMX and upserts `DailyBars`. Historical read requests use the existing three-attempt transient-transport policy (one- and two-second waits); this does not retry database writes or an operational pipeline.
3. Incrementally updates `AdvanceDeclineLine`.
4. Incrementally updates and prunes per-symbol `SymbolObv` history.
5. Computes the current market-wide `MarketClimax` record.
6. Backfills/updates TSX sector-index history.
7. Refreshes stock-sector mappings when stale.
8. Updates market-leadership data.
9. Updates US index history from the configured external source.
10. Collects actual SPY daily-chart history into `DailyBars`, validates required benchmark coverage and appends missing observations without rewriting prior SPY rows. A failed required SPY refresh stops with an error.
11. After the data-update stages return successfully, creates and verifies a compressed checksum backup in `C:\ProgramData\TraderVI\Backups`, copies it to OneDrive, and compares SHA-256 hashes.

Running Hermes performs external HTTP requests and writes multiple SQL tables. Obtain explicit authorization and review schema/data prerequisites first.

Migration 017 was applied and verified on 2026-09-02. It introduced the nullable leadership contract and
`v3.2-leadership-missingness` identity; the current v3.4 strategy preserves that contract. The first authorized post-migration Hermes run also
passed: its 2026-09-01 row preserved unavailable movers breadth as null, its leadership constraint remained
enabled/trusted, and its checksum-verified staging and OneDrive backup copies hash-matched. Later Hermes
runs still require explicit authorization. A deliberate official Delphi cohort may now start when wanted;
Athena does not collect or repair leadership data.

Migration 018 was manually applied and verified on 2026-09-03 after a fresh checksum-verified full backup
and hash-matched OneDrive copy. The two Leadership value checks and `FK_Quotes_Symbols` are enabled and
trusted, with zero invalid Leadership rows and zero Quote orphans. No DACPAC was deployed.

The backup behavior is part of Hermes itself, so it applies whether Hermes starts from Visual Studio or `dotnet run`. By default, the destination resolves to `$env:OneDrive\Joseph\Tradervi\backups`. Override the existing directories with `TRADERVI_BACKUP_STAGING_DIRECTORY` and `TRADERVI_BACKUP_DESTINATION_DIRECTORY` when needed. Hermes never creates these directories or overwrites a backup generation. Updated source keeps the latest two completed full backups in each location, with cleanup only after the new backup and its copy verify (ADR-0082). Partial/unrelated files are ignored; cleanup warnings are reported separately from backup failure.

If the data update completes but backup creation, verification, or copying fails, Hermes prints a prominent warning and exits with code `2`. The updated database remains intact, and any completed staging backup is preserved for diagnosis or manual copying. A successful copy still requires the OneDrive client to finish cloud synchronization.

```powershell
dotnet run --project Hermes
```

`dotnet run --project Hermes -- --spy-only` runs the same SPY maintenance and verified-backup path without
refreshing the TSX universe or other indicators. It still accesses the external source and writes SQL;
use only under explicit operational authority. SPY is not added to the TSX symbol universe or the Genuity
index list. A missing/stale shared benchmark is not interpreted as a positive signal.

One-time A/D, OBV, and CLX backfills are separate operations; use only the documented Sandbox probe or explicitly enabled backfill path after reviewing its scope.

## Hercules — model training

**Project:** `ML.Train`
**Entry point:** `ML.Train/Program.cs`
**Typical schedule:** deliberate weekly/on-demand retraining, not every run

Hercules:

1. Preserves the selected strategy's exact models and metadata for comparison and rollback.
2. Loads stored equity histories and XIU, retaining a checksummed private data snapshot and source archive.
3. Trains only profit tasks enabled in the code's `ProfitModelRegistry`, using the shared dated inputs.
4. Saves new files in a unique candidate directory and refuses to overwrite existing artifacts.
5. Records experiment metrics, disabled model rows and a completed manifest only after all tasks succeed.

Pattern detectors are rule-based and are not trained or stored in `ModelRegistry`.

Running Hercules is consequential: it is CPU-intensive and mutates both model artifacts and SQL state.

```powershell
dotnet run --project ML.Train
```

Do not retrain until the intended data cutoff, enabled model set, output paths, and registration behavior have been reviewed.

Candidate-only dated training is now the default; `--dated-candidates` remains an optional alias.
Storage defaults to `%LOCALAPPDATA%\TraderVI\Models`; `--output-root <directory>` can select another
reviewed private location. Signal thresholds are inherited from the predecessor; optimized suggestions
remain recorded metrics. Neither a successful training run nor a completed manifest changes selection.

## Model review and deliberate selection

**Project:** `Tools/ModelLifecycle`
**Selection script:** `Operations/Set-TraderVIModelSelection.ps1`

Under explicit operational authorization, `ModelLifecycle` can inspect a private registry export
(`inspect-registry`), verify a preserved assignment using artifact hashes and synthetic predictions
(`verify-set`), or prepare previous/corrected assignments from a completed candidate manifest
(`prepare-switch`). `verify-active` reads SQL and loads models through Delphi's actual bootstrap without
publishing recommendations. These commands do not launch the nightly pipeline or fetch market data.

The selection script defaults to `Prepare`, producing reviewable forward and rollback SQL. Its
`VerifyTransaction` and `VerifyRollbackTransaction` modes exercise the transitions and roll them back;
`Apply` commits reviewed selection and `Rollback` commits a separately authorized return to the retained
predecessor. The script verifies files and expected state and holds the nightly lock. Migration 026
provides immutable assignments and selection history. Registration changes the selected strategy, while
global model-enable flags and prior recommendations/account history remain unchanged.

The 2026-09-06 authorized [cutover review](reviews/model-input-cutover-20260906.md) records the completed
selection. Future training or selection is not authorized merely by these instructions.

ADR-0058 adds `ModelLifecycle verify-benchmarks` and `prepare-benchmark-switch <review-directory>`.
The latter requires the dated-input predecessor, validates current XIU/SPY inputs, preserves the same four
models and prepares a new source/strategy identity. The selection script's `-NewDecisionRef ADR-0058`
requires an unchanged predecessor model set; provide the reviewed `-NewVersionName` for every mode.
Default ADR-0056 selection remains supported for existing review records. None of these commands publishes
recommendations; preparation and verification do not select a strategy.

## Delphi — advisory recommendations

**Project:** `Delphi`
**Entry point:** `Delphi/Program.cs`
**Shared workflow:** `Core/Runtime/DelphiWorkflow.cs`
**Typical schedule:** before market open, using the most recently completed daily bars

Delphi resolves the active strategy's immutable model assignment from SQL. The nightly CLI and WPF use
the same shared workflow, so the selected `v3.4-observed-benchmarks` automatically receives the corrected
ADR-0056 model inputs and ADR-0058 observed XIU/SPY policy, with exact reviewed model files. The benchmark
policy validates the prior session against the reviewed TSX calendar and requires 200 valid observations
and a matching endpoint for both series before evaluation. A US-only holiday without a matching SPY row
therefore stops new publication with a reason; no stale-data tolerance or substitute index is assumed.
No per-host binding flag is needed. A corrected
strategy missing its assignment fails before evaluation. An optional `--model-input-binding` file cannot
override a stored assignment. Unbound legacy strategies retain explicitly unverified legacy inputs.

Delphi currently:

1. Loads the one active strategy version and its assigned profit models. A persisted official run
   also requires the strategy's explicit initial-code and decision identity (ADR-0042).
2. Creates deterministic pattern signals from the code registry.
3. Computes XIU/SPY regime and A/D breadth.
4. Evaluates Granville groups #1–#20 and #25–#28.
5. Loads the equity universe, applies liquidity/ETP filters, and computes live relative strength.
6. Loads per-symbol OBV field trends and recent CLX history.
7. Evaluates two ranking lenses:
   - Continuation: executed recommendation lens.
   - Breakout: journaled comparison lens.
8. Writes daily picks, decision dossiers, and Granville diagnostics for later analysis.
9. Prints machine-oriented diagnostics and a human summary.

Delphi is advisory—it does not place a broker order—but it is not read-only. Do not use it as a harmless smoke test; use focused builds and tests for routine validation. A no-write mode can be added later if an operational reporting need emerges.

Migrations 016 and 017 introduced `v3.2-leadership-missingness` on 2026-09-02 without changing thresholds,
models, gates, ranking formulas or execution policy. On 2026-09-06 the authorized input correction selected
`v3.3-dated-profit-inputs` with four separately trained models and unchanged strategy/signal thresholds.
The later ADR-0058 repair selected v3.4 with the same four model files and thresholds, after the Friday/SPY
refresh. Earlier evidence retains its original identity. The next normal scheduled run uses v3.4; a full
pipeline was not launched to validate either switch. Delphi remains a consequential database writer and must not be
launched as routine validation.

Delphi records `DailyPick.PickDate` and Granville `EvalDate` using the recommendation run date so their date-scoped records remain linked. Its reports separately show the latest completed TSX session as the market-data-as-of date. A weekend run can therefore produce a weekend recommendation date based on Friday's completed market data; this is intentional audit semantics, not a claim that Saturday was a trading session.

```powershell
dotnet run --project Delphi
```

## TraderVI — ghost execution and headless paper monitor

**Project:** `TraderVI`
**Entry point:** `TraderVI/Program.cs`

TraderVI is a CLI for simulated trade and position bookkeeping. Ghost mode records trades and positions but does not submit live broker orders. Its `paper-monitor` command uses the same durable monitor as the WPF dashboard: each cycle records TMX poll receipts and completed evidence, evaluates the 15-minute policy, and records an authorized policy exit at a separately observed delayed price. Pass `--advisory-only` to suppress automatic ghost exits.

For each completed policy bar, the monitor also reads the existing immutable calibration ledger and selects the newest valid `OfficialPaper` run that both started after the position entry and was durably created before that bar began. Only a same-run published Breakout with probability at least 60%, direction edge at least 10%, and down probability below 35% can qualify the paper-only 10% loss exception. Missing/unpublished evidence or a read failure defaults to the ordinary 10% exit, and no Delphi evidence can bypass the absolute 20% exit.

```powershell
dotnet run --project TraderVI -- list
dotnet run --project TraderVI -- pnl
dotnet run --project TraderVI -- buy SYMBOL SHARES PRICE "notes"
dotnet run --project TraderVI -- sell SYMBOL PRICE "notes"
dotnet run --project TraderVI -- scan
dotnet run --project TraderVI -- paper-monitor
dotnet run --project TraderVI -- paper-monitor watch
dotnet run --project TraderVI -- paper-monitor watch --advisory-only
dotnet run --project TraderVI -- paper-add EDR Continuation 5 15.34
```

Even in ghost mode, `buy`, `sell`, and `paper-monitor` mutate SQL records. `paper-monitor` also calls TMX. `scan` loads market data and models. Obtain explicit authorization before using mutating commands.

## TraderVI.WPF — combined Ghost/Real trading dashboard

**Project:** `TraderVI.WPF`
**Startup window:** `TraderVI.WPF/PaperDashboardWindow.xaml`

### Fixed-gate comparison accounts

Migration 033 and the initial five comparison pairs are active from September 9; see
[ADR-0067](adr/0067-fixed-gate-paper-comparison-pairs.md). In **Portfolios → Compare**, select
**Gate comparison pairs** to view adjacent Normal and Portfolio rules only accounts for each of the
four Shadow strategies and Delphi Live. Each has independent CAD 1,000 starting cash. Select an account
to see its fixed profile, publication, funded start, fresh-evidence boundary and admitted candidate count.
The candidate monitor explains the remaining entry conditions; missing data remains a block.

Keep the WPF host running for scheduled shared Live and Shadow collection. Startup reads the existing
accounts; it does not create accounts or add capital. A newer valid publication under the pinned daily
strategy is adopted automatically by both members. Changing the active daily strategy pauses incompatible
new entries; it does not mutate a pair. Global new-buy pauses in Settings continue to apply, with held
protection retained. The original Shadow generation's controls do not target comparison selections.

Profiles and paired rules are fixed, and their names identify the pair. Shared Settings and recommendation
review cannot change or promote these accounts. A future experiment requires a separately reviewed fresh
pair. The initial operator-only activation is `PaperComparisonRepository.ActivateInitialAsync`; the
explicit helper and verification records are under ignored `artifacts/gate-comparisons-20260909/`.
Activation retries return the existing ten identities without resetting cash or history. Do not replay
the immutable migration against an already installed schema. Today's 13:25 source boundary and runtime
gaps remain partial-session evidence; neither missing returns nor missing opening bars are fabricated.

### Existing accounts and monitoring

The WPF app is the tabbed interactive TraderVI shell. Its Tracked positions area shows open Delphi-linked positions plus operator-reported Real holdings that were enrolled without a Delphi pick; completed lifecycles remain available in Trade history. Unlinked Real holdings are labelled as historical, begin durable monitoring at their enrollment time, and cannot receive the fresh-Delphi loss exception. Unlinked Ghost rows remain excluded. The tab also shows separate Ghost/Real realized and unrealized P/L plus durable poll receipts. Rows use both an icon and a `GHOST`/`REAL` label. It refreshes SQL history every thirty seconds. During the Toronto regular monitoring window it runs once on startup and then collects every five minutes from the first safe 09:47 poll through 16:02. The exit policy still consumes only completed 15-minute bars; intermediate collection ticks do not create five-minute policy decisions. Outside that window it is history-only and makes no TMX request.

The Portfolios tab adds ADR-0051's separately versioned Shadow V1 ledger. Enter the total TFSA value and
available cash, then explicitly confirm Start. Each of the four System portfolios receives the total value
as its own alternative starting cash; the available-cash field describes the real comparison and does not
cap those separate what-if accounts. A same-session Start establishes an activation baseline and waits for
fresh completed five-minute evidence. Pause blocks new buys, add-ons, re-entries, and rotations while risk
exits continue. A portfolio at `CapitalReviewRequired` needs an explicit Resume after review. Recording a
later real snapshot updates only the comparison; it never overwrites Shadow cash or returns. Portfolio
display names are editable, but stable identity and history are not. Selecting a System portfolio shows a
read-only candidate monitor with its frozen rank, current state, yesterday's close, prior/latest completed
five-minute closes, distance from yesterday, plain-language decision reason, and last evaluation time. The
overview freshness also advances from candidate evaluations before any holding exists. Migration
`20260904_019_AddSystemShadowPortfolioLedger.sql` was manually applied and verified on 2026-09-04 after a
checksum-verified, hash-matched full backup. Migration 020 later corrected the exact 2026-09-04 Delphi run
and cleared only its first empty Shadow attempt. The first completed session is retained as engineering
shakedown evidence: it is operationally useful but is excluded from strategy-performance conclusions.
Migration `20260904_021_HardenSystemShadowExecutionCausality.sql` was backed up, manually applied, and
verified on 2026-09-04. It additively stores the last consumed fifteen-minute bar identity while preserving
all prior ledger rows and totals; historical identities remain unknown rather than inferred. The controller
also cancels any pending buy whose exact next-bar fill window was missed and requires current five-minute
requalification; pending sells remain protective. Shadow has no Wealthsimple or other broker connection.

The same Portfolios overview now also reads Delphi Live's independent paper accounts, including queued
activations and ended comparison accounts. The row's selector, currency and status identify its scope.
Before the main account is created, a **Not activated** placeholder explains where to review activation;
it has no fabricated cash or returns. Selecting a saved Delphi Live account shows its candidate states,
holdings, decisions and fills, retaining estimated-fill labels. Account values use the latest saved exact
checkpoint matching its holdings and cash; incomplete or superseded marks remain unavailable.
Daily Shadow start/pause/resume/rename controls cannot operate on a Delphi Live selection. Manage live
activation and capital review in **Delphi Live → Setup & advanced**. Friday's historical replay remains
under **Delphi Live → Replay account** and is not an operational portfolio in this list.

The Data Audit tab calls the same host-neutral `MarketDataAuditWorkflow` as the retained DataAudit console application. It runs only when its clearly labelled button is pressed, uses local SQL reads only, and makes no correction or external call.

The Delphi tab calls the same host-neutral `DelphiWorkflow` as the retained Delphi console application. Opening or refreshing the tab only reads the latest saved Continuation and Breakout picks and their matching saved presentation evidence. Its inner views are Overview, Picks, Market, Granville, Diagnostics, and Full Report. New official runs reopen from a typed immutable snapshot stored inside the existing calibration run context. Runs from before ADR-0035 show a clearly labelled, date-aligned reconstruction; missing facts remain unavailable rather than being replaced with newer values.

The Picks view can create a monitored Ghost position or record a Real position
from a selected saved Continuation or Breakout recommendation. The operator
enters positive whole shares and the actual fill, chooses the mode, supplies an
account label for Real, and confirms the lens, rank, recommendation date, and
book cost. `Real` means the operator says that fill already occurred; TraderVI
does not submit or verify it. The shared `PaperTradeEntryWorkflow` preserves the
exact `PickId`, rejects duplicate active symbols, and never calls a broker or
invents a fill. Breakout selections are explicitly labelled exploratory.

Migration `20260827_013_AddTrackedExecutionMode.sql` was manually applied and
verified on 2026-08-28 after a checksum-verified full backup and hash-matched
secondary copy. All legacy rows were deliberately classified Ghost. An active
Ghost row can be marked Real only through the confirmed reconciliation control,
which writes an immutable audit event. The five-share EDR Ghost mirror is no
longer active: it was automatically paper-sold at $15.62 under
`Policy TrailingProfit`. If the broker holding is still open, create a separate
operator-confirmed `REAL / TFSA` entry using the actual five-share, $15.34 fill;
do not convert or reopen the completed Ghost lifecycle.

The monitor evaluates both modes. Automatic exits are hard-guarded to Ghost.
A Real exit alert remains a manual-action signal until the operator records the
actual all-shares broker sell fill; that control changes only TraderVI's ledger
and never sends an order.

The Scorecards tab is a read-only view of the advanced official Delphi report:
coverage/readiness, model probability metrics, reliability, deciles,
Continuation/Breakout rank performance, and diagnostic slices. It uses the
same official evidence query and pure calculator as Athena, writes nothing, and
does not require CSV export. Refreshing it cannot mature outcomes or change
Delphi. Migration `20260828_014_SeedCalibrationOutcomeDefinitions.sql` was
applied on 2026-08-28 to initialize four contracts, and migration
`20260901_015_AddDelayedIntradayOutcomeDefinition.sql` was operator-applied and
verified active on 2026-09-01 to add the fifth. The latest verified Athena run
wrote 112 valid three-session mark outcomes and 112 valid excursion outcomes;
the two prediction definitions and delayed-intraday definition still have zero
outcomes, so their performance sections remain correctly unavailable.

The Project Docs tab discovers Markdown throughout the repository except `.git`, `.vs`, `bin`, `obj`, `packages`, and `node_modules`. It groups documents by folder, searches title/path/content, opens `Docs/project-status.md` by default, and reloads external edits with Refresh. Relative Markdown links and heading fragments navigate inside the tab only after safe repository resolution. Clicking an HTTP(S) link opens the system browser; merely loading, searching, or refreshing documentation never opens a web page. The reader does not write files or access SQL, models, or market services.

`Run official Delphi` first shows a confirmation describing the operation: it reads local market data and registered model files, appends immutable calibration evidence, and replaces same-date operational picks and supporting records. It does not place a broker order or create a paper position. Do not confirm it merely to test the interface; use focused builds and tests instead.

```powershell
dotnet run --project TraderVI.WPF
```

Keep the app open for future polling. Closing it, signing out, sleeping, or restarting the computer stops the in-process schedule. Version 1 does not install a Windows service or background task. The tracked-position monitor has manual ledger-reconciliation controls; its automatic actions remain database-only Ghost exits. The separate manual-buy ticket can submit an explicitly reviewed broker order and does not give this monitor automatic broker authority.

<a id="progressive-profit-protection--implemented-disabled"></a>

## Progressive profit protection — installed and enabled

[ADR-0090](adr/0090-progressive-individual-profit-trailing.md) is implemented/tested across Daily Shadow
variants and Delphi Live, with recommendation/supported managed-account integration. It is now installed
and assigned to **all 15 active paper portfolios** (12 Shadow/three Live), with 37 holdings' protection
preserved/initialized and post-start state verified. V1/default flags stay false. The running normal-path
desktop uses the hash-verified installed Core. Weekend checks do not establish a market-session sale.

Future rollout remains an explicit operation: review required migration installation and binaries, then save/assign
new versions with `UseProgressiveProfitProtection=true` under the existing pause/assignment controls.
Migrations 043, 044 and 045 are installed; do not replay them. Migration 044 adds the narrow comparison
guards and 045 compares canonical Live settings in their own serialization. No trigger is disabled.
The Shadow schema-presence cache requires recreating its repository/app after installation. Do not
deploy the DACPAC. Review retained holdings/pending sells and verify prospective operation before
claiming coverage. This document does not authorize those operations.

For retained holdings and paired accounts, save through `SaveProgressiveProfitProtectionAsync`
and assign through `EngineSettingsRepository.AssignProgressiveProfitProtectionAsync`. New buys must be
paused; this narrow path permits invested accounts and pending protective sells. Ordinary settings
editing still requires empty portfolios. Restore the original entry-pause state after verification.

For paired comparison accounts, use only `EngineSettingsRepository.AssignProgressiveProfitProtectionAsync`
with a new saved version and explicit target. It validates that only this opt-in changes (plus the new
Live policy ID); immutable comparison profiles/sources and original gate choices are preserved. Ordinary
comparison editing remains blocked. Record the prospective intervention separately from old experiment
returns. Existing real-account execution prerequisites remain unchanged.

Activation carries stronger existing floors and protective sells, reconstructs available actual
five-minute history since purchase without inventing missing bars, and preserves high/earned gap
through combined buys and restart. The new floor is persisted before its trigger quote. Independent
quote-only checks use five minutes while safely above the stop and one minute within 0.25%, on the
existing host wake cadence during reviewed regular sessions. Missing/invalid/stale-receipt bids cannot
trigger the new rule; existing protections continue. Live obtains a separate fill quote; Shadow keeps
its existing later-five-minute fill/friction model.

Additional quote investigation remains deferred, not an added implementation gate. The existing TMX
receipt contract lacks a bid market timestamp; at most 60 seconds since local receipt does not prove
upstream freshness. The rollout's native checks requested no market quotes or broker orders. Existing
real TFSA execution prerequisites remain in force; paper enablement does not activate broker execution.

## Delphi Live — explicit paper-monitor activation

The frozen design is [Delphi Live V1](concepts/delphi-live.md); source and validation status are in the
[implementation checklist](delphi-live-implementation-checklist.md). The WPF Delphi Live tab reads
status separately from activation. Migrations 022, 023, 024 and 025 were separately authorized and
applied in order on 2026-09-06 after a checksum-verified full backup and a hash-matched OneDrive copy
reported synced by Windows. Schema and preservation checks passed, and `DBCC CHECKDB` reported no
errors. That initial installation was inactive; current operation and qualification are recorded in
[project status](project-status.md). Building the SQL project alone does not apply a migration.

Desktop tick failures append private metadata to
`%LOCALAPPDATA%\TraderVI\Diagnostics\delphi-live-failures.jsonl`: error type/HResult, SQL error
numbers and bounded Core method locations. Arbitrary messages, provider bodies, SQL and account
values are omitted. A missing log alone does not prove healthy collection; verify scheduled receipts,
evaluations, portfolio updates and renewed ownership. Current-cycle warnings can clear after recovery,
while recorded host gaps remain. See [ADR-0089](adr/0089-tmx-session-context-and-supplementary-records.md).

The live exact-bar adapter explicitly requests context through its observed fetch-start minute when
that falls inside the following regular-session interval. History and ordinary diagnostic end bounds
remain unchanged. Receipt JSON records actual provider end and latest aligned source time separately
from the logical completed-bar request. StaleNoNewBar describes older source coverage; it supplies no
substitute price. Missing/unreported minutes never justify a synthetic bar (ADR-0030/ADR-0089).

The reviewed local calendar was installed on 2026-09-06. The current Windows user's
`TRADERVI_TSX_CALENDAR_PATH` points to
`C:\src\TraderVI\Operations\Calendars\tsx-2026-through-20261223-v1.json`.
It covers 247 full sessions within 2026-01-01 through 2026-12-23; see the
[calendar installation record](../Operations/Calendars/README.md). Restart an already-open Visual Studio
or other launcher before starting WPF so its child process receives the new variable.
The JSON has these required
fields (names are case-insensitive; unknown fields are rejected):

| Field | Contents |
|---|---|
| `version` | Immutable name for the reviewed snapshot |
| `sourceReference` | Official source reference and review provenance |
| `firstCoveredDate`, `lastCoveredDate` | Inclusive `yyyy-MM-dd` coverage, including non-session dates |
| `regularSessionDates` | Distinct `yyyy-MM-dd` dates of the regular 09:30–16:00 TSX sessions inside that coverage |

Include enough prior history for the frozen daily baselines and enough future coverage for five-session
outcomes and next-session assignments. The host uses `America/Toronto`, refuses dates outside coverage,
and never guesses weekdays or fetches a calendar on demand. The installed snapshot stops before
December 24's 13:00 close because V1 assumes a full 16:00 close. Short-session handling, including
protection of carried positions, must be reviewed before extending coverage. The next listed session
after the installation date is Tuesday, September 8; Monday, September 7 is Labour Day.

The main **Current watchlist** reads the latest valid saved Delphi picks while inactive, showing the
recommendation date and combined source ranks. Friday's 25 picks per lens overlap into 28 stocks;
published rows that fail entry gates remain visible. This preview does not start collection or make
Friday's source eligible for a later live session. Once saved live observations exist, the same table
shows them. Select a stock to see its checkpoint price, data quality and explanation.

Click **Replay Fri, Sep 4** to open the saved historical simulation. Move the time slider or use
Previous/Next; **Replay account** shows cash, marked holdings and estimated trades at that point in time.
The app automatically finds the latest supported report under `artifacts/delphi-live-replays` when run
from the repository. If none is found, **Open saved replay…** opens a local `*-report.json` file.
This file viewer makes no market requests. The estimates and limitations are recorded in
[ADR-0054](adr/0054-delphi-live-preview-and-historical-replay.md) and the
[replay review](reviews/delphi-live-friday-replay-20260906.md).

After separately authorizing activation, use **Setup & advanced** and enter a positive simulation
amount, currency and operator reason. There is no default live capital, broker cash lookup, deposit or withdrawal.
Activation queues a cash-only Operational Champion for the next regular-session boundary. Start WPF
sufficiently before 09:30 Toronto time to establish successful pre-open heartbeats, with the eligible
official daily run and daily baselines already available. Keep it open through completion of the final
16:02 collection and closing persistence. Its monitor is independent of the selected tab.
Closing or interrupting the host records a coverage gap; restarting protects existing holdings and
rebuilds ordinary confirmation from fresh observations without replaying missed actions.

The experiment controls record explicit reasons and future boundaries. Discovery requires ten clean
engineering sessions and starts fresh, equal-capital champion-control and challenger runs for one
predeclared threshold family. Untouched confirmation selects one contender after thirty matured paired
discovery cohorts and starts new aligned runs. A passing thirty-cohort untouched score permits human
promotion review; it does not activate a policy automatically. The former champion then supplies a
thirty-clean-session baseline. Portfolio capital-review resumption is a separate recorded human action.

Research reports load only when requested for an explicit date range of at most 366 calendar dates and show their read time. They do not
run inside the periodic monitoring refresh. Official portfolio NAV includes every valid fill; the adjacent
fill-confidence diagnostic compares realized returns for all closed trades against the bid/ask-only closed
trade slice, with estimated-fill counts and percentages. Its trade-return denominator is labelled and is
not substituted for a portfolio NAV return. Report a known or suspected corporate action with its symbol,
affected date range and reason; the resulting audit excludes affected evidence without adjusting cash,
shares or historical prices.

Use offline Core tests and builds for source validation. Starting WPF, collecting quotes/bars, applying
migrations, scheduling an experiment and activating a portfolio are operational steps with persistent
effects; none was performed merely to validate this implementation.

The one-shot `delphi-live-closed-market-trial` Sandbox probe is the explicitly selected historical
source check documented in the [2026-09-06 trial record](reviews/delphi-live-closed-market-trial-20260906.md).
It requests nine pinned September 4 intervals from TMX and writes one unique local metadata report,
with no SQL or portfolio action. It requires external-service authorization on each run and does not
exercise current-session availability, persistence or activation.

The separately authorized `delphi-live-friday-replay` Sandbox probe produced the saved historical replay
on 2026-09-06. It reads the frozen Friday source and saved Shadow account capital from SQL, then obtains
at most 58 historical TMX batches (29 symbols including XIU, one- and five-minute intervals). Completed
symbol requests are cached. Its only writes are ignored local replay artifacts; it is not a database
backfill. Do not run it as a build check or to open an existing replay. Any additional acquisition run
requires explicit operational authorization.

## Athena — calibration outcome evaluation

**Project:** `Athena`
**Entry point:** `Athena/Program.cs`

Athena reads immutable official calibration candidates and local `DailyBars`, reproduces the enabled production labelers, and idempotently writes matured 10-session labels, 20-session price paths, three-session marks/excursions, and delayed-intraday outcomes. It prints the identified active strategy scope and excludes/counts earlier strategy identities before reporting comparative coverage: distinct completed-market-session cohorts, official run and candidate counts, valid/degraded/invalid/pending outcomes, completion coverage, usable coverage, and whether the 95% reporting floor permits a primary descriptive score. ADR-0038 adds official-only probability calibration, eligible-lens rank quality, and descriptive technical/market slices. All metrics use nested candidate/run/market-session weighting so a deliberate Delphi rerun cannot inflate independent evidence. The report never changes a model, weight, gate, lens, or trading policy.

For `DelayedIntradaySwing`, Athena replays only a continuous first-received 15-minute path. A later bar that proves a missing bar/session, a receipt-order conflict, or a later five-minute bar that proves the exact symbol/XIU fill bar is missing produces an audited invalid outcome. If no later evidence yet proves a gap, the candidate stays pending. An alert during the session uses the exact next five-minute boundary; an after-close alert waits for the next observed regular-session open. Athena never substitutes a later convenient price.

The 2026-09-01 verified run wrote 112 valid `SwingMarkToMarket3` and 112 valid `SwingExcursion3` outcomes. It wrote zero `PredictionLabels10`, `PredictionPath20`, or `DelayedIntradaySwing` outcomes. Durable SGY/XIU evidence still ended on 2026-08-28, so launching WPF without a newer receipt does not make delayed outcomes ready.

Athena makes no external requests. It is still a database writer because it creates missing definitions and matured outcomes: apply the reviewed calibration migrations manually first and obtain explicit authorization before running it. Migration 016 is applied; until a new official Delphi cohort exists, the active identity correctly reports 0 included runs and 7 excluded earlier-identity runs. The optional CSV switch writes five export-schema-v2 artifacts stamped with strategy ID/name, initial code commit, and decision reference, and refuses to overwrite an existing filename.

```powershell
dotnet run --project Athena
dotnet run --project Athena -- --scorecard-csv C:\path\to\an-empty-export-directory
```

Athena remains independently runnable, and ADR-0046 also invokes it as the final stage of the guarded
daily pipeline. Hermes and Delphi do not launch it directly.

## AI saved-record reader check

`AiDecisionReadersSqlTests` is an explicit opt-in, SELECT-only check of the configured local database.
It calls the maintained catalogue and both paper decision-context repositories, using the installed
reviewed calendar. It never starts collectors, trading engines, AI clients or journal writers. Missing
saved sessions remain unavailable; a pass does not certify fresh feeds or complete variant coverage.
Use only when a native read is intended. Ordinary test runs skip this check.

```powershell
$env:TRADERVI_RUN_AI_READ_ONLY_SQL = '1'
try {
    dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --no-restore --filter FullyQualifiedName~AiDecisionReadersSqlTests --logger 'console;verbosity=detailed'
} finally {
    Remove-Item Env:TRADERVI_RUN_AI_READ_ONLY_SQL
}
```

`TRADERVI_TSX_CALENDAR_PATH` must already identify the reviewed calendar. Output contains aggregate
counts/timings and mapping failures; never add private account values or saved signal JSON to output.
The ten-minute diagnostic age is not a runtime policy. Session-close rejection uses a simulated clock;
no saved timestamp is rewritten. Serial reads do not qualify production lock duration or concurrent changes.

## AI paper risk-value check

`TmxAiRiskPriceSource` is available in source for explicitly invoked one-minute price collection.
Construction makes no requests. A caller must supply the reviewed calendar, canonical target symbols,
price/receipt age limits and a bounded request timeout, and retain ownership of its `TmxClient`.
It returns price evidence and per-symbol outcomes, not a reconciled portfolio valuation. Do not substitute
it directly for the current paper risk readers. `PaperAiFastValuationReader` now joins the complete
account and `PaperAiFastRiskMonitor` persists qualified evidence through the existing risk journal;
the faster overload of `PaperAiPortfolioRiskReader` connects them. Native timing/concurrency and
operating qualification remain pending. No normal app/nightly host invokes this path yet. A saved
source conflict blocks new observations across restart and requires reviewed recovery; do not remove
the journal or initialize a new round to bypass it. Existing account history and protection persist.

`AiFastRiskNativeTests` is the opt-in check of that connected path. It reads real account books using
SELECTs and writes only fresh, uniquely named diagnostic journal directories beneath an explicit output
root. During a regular session, two passes a minute apart make at most 48 TMX requests (144 transport
attempts); the workload is capped before collection. It refuses the first minute and final three minutes
of a session. After hours it verifies rejection and journal reopening with no TMX requests. Use the
inherited reviewed calendar variable, not an absent user-scoped replacement. Do not run without explicit
authorization for SQL reads, bounded external collection and private diagnostic journals.

```powershell
$env:TRADERVI_RUN_AI_FAST_RISK_NATIVE = '1'
$env:TRADERVI_AI_RISK_DIAGNOSTIC_DIRECTORY = 'C:\src\TraderVI\artifacts\ai-risk-native\journals'
try {
    dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --no-restore --filter FullyQualifiedName~AiFastRiskNativeTests --logger 'console;verbosity=detailed'
} finally {
    Remove-Item Env:TRADERVI_RUN_AI_FAST_RISK_NATIVE
    Remove-Item Env:TRADERVI_AI_RISK_DIAGNOSTIC_DIRECTORY
}
```

Journal files contain private source/account evidence; keep them out of commits. Console output includes
aggregate outcomes and anonymous account/symbol slots with read-clock differences, completed-bar counts
and price times/ages; no balances, prices or account/symbol identities. Shadow verification now uses
app UTC while the complete SQL book remains locked, not SQL server time. Diagnostic age settings
(45 seconds for account reads, 120 seconds for prices and
30 seconds for receipts) do not establish operating limits. A passing after-hours run verifies no fresh
accepted valuation; even a regular-session sample does not establish full-session coverage or retention.

`AiRiskReadersSqlTests` reads the installed catalogue and both saved paper risk books using SQL SELECTs.
It reconciles cash, holdings and saved marks, then exercises valuation and stale/altered-evidence
rejection in memory. It never creates a risk journal, modifies SQL, collects data or starts trading.
Ordinary tests skip it. To run the explicitly intended native check:

```powershell
$env:TRADERVI_RUN_AI_RISK_READ_ONLY_SQL = '1'
try {
    dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --no-restore --filter FullyQualifiedName~AiRiskReadersSqlTests --logger 'console;verbosity=detailed'
} finally {
    Remove-Item Env:TRADERVI_RUN_AI_RISK_READ_ONLY_SQL
}
```

The reviewed calendar must already be configured. Output contains counts and timings, never account
values or identities. Shadow cash-only reads now report how many current books received a transaction
verification timestamp. This SELECT-only receipt does not write account data or refresh held prices.
A pass can include safely unavailable/stale books: inspect the aggregate counts.
The ten-minute diagnostic age is not a production freshness decision. This is saved-book validation,
not one-minute feed, broker cash-event, concurrent-load or operating AI host qualification.

For a SELECT-only price-timing report over the last hour of today's saved regular session:

```powershell
sqlcmd -S localhost -d TraderDB -E -b -y 0 -w 65535 -i Operations/Read-AiPriceFreshness.sql
```

This uses the saved reviewed-session bounds and reports aggregate receipt delays, interval lengths,
and mature Live collection outcomes. Missing session bounds cause an error. It makes no provider
calls and writes no SQL data. Completed-bar age is measured from bar end, never download time;
repeated old prices remain old. The native test above also reports actual invested-book ages.
These samples do not establish continuous coverage, prove every expected slot exists, or approve
a production freshness limit. Existing five-minute collection and quote receipt timestamps cannot
establish one-minute protection. Inspect failed/missing slots and source conflicts as well as successful reads.

For an explicitly authorized provider-failure diagnostic, use Sandbox's `tmx-live-collection-failure`
with `TRADERVI_TMX_FAILURE_SYMBOL` set to one canonical TSX symbol reported as failing and
`TRADERVI_TSX_CALENDAR_PATH` set to the reviewed calendar. It calls the existing Live five-minute
provider adapter once for that symbol and once for XIU (deduplicated if XIU was selected), using
one recently completed interval and the normal two-minute delay. To repeat a particular interval,
set `TRADERVI_TMX_FAILURE_BAR_END_UTC` to its exact end (`yyyy-MM-ddTHH:mm:ssZ`). Fixed windows must
be five-minute aligned, fully inside a reviewed session and past the normal two-minute delay;
they may be historical and never qualify current freshness. With no override the session must be open.
Set `TRADERVI_TMX_FAILURE_INCLUDE_BENCHMARK=false` for an explicitly scoped single-symbol follow-up;
the default is true and other non-boolean values are rejected. Each target has a 25-second
deadline; existing transport retries permit at most three attempts per target. Run a Debug build
with its PDBs to obtain exception source lines. It emits only request times, dispositions, presence,
transport counts where available and Core exception locations. Messages from the exact local
`TmxClient.ValidateIntradayResponse` method contain only fixed text/timestamps and are included;
raw provider bodies/prices and arbitrary exception messages are omitted.
Set `TRADERVI_TMX_FAILURE_TIMESTAMPS=true` for an explicitly authorized timestamp comparison on
those same targets/windows. The default is false; invalid Boolean settings reject. This uses the same
client request/mapping/validation, once per target, and captures a timestamp-only immutable copy before
validation. It reports source count, up to the first 32 source timestamps, field completeness, mapped
UTC times and bounded mapping-error types. Only recognized date syntax is printed; arbitrary text is
redacted. Transport attempts remain available if the provider response reached inspection. Incomplete
rows cannot become bars. ADR-0089's normal source path uses same-date regular-session context,
returns only the logical requested window and excludes trailing off-grid records explicitly;
interior off-grid and malformed data still reject. Excluded timestamps are retained in receipt
metadata and never rounded or used as prices. Timestamp inspection remains an unfiltered source
copy and cannot change that acceptance contract. This mode makes no additional provider calls.
It performs no SQL, journal writes, monitor ticks, AI calls or trading. A later successful request
does not identify an earlier exception or repair saved collection gaps. It is never an automatic retry
or routine validation command; external-service authorization is required before invocation.

The explicitly invoked `tmx-one-minute-freshness` Sandbox probe measures the candidate faster source:

```powershell
dotnet run --project Sandbox -- tmx-one-minute-freshness
```

This requires authorization for external TMX access. It reads the configured reviewed calendar,
then makes five XIU one-minute requests spaced one minute apart, approximately ten seconds past each
minute. Each call has a 25-second timeout and the existing three-attempt transport cap (15 attempts
maximum overall). It requires ten minutes of session history and five minutes before close. Provider
failure stops the sequence. Output reports timestamps, ages, missing minutes and revisions/disappearances
of previously completed bars. Forming bars are excluded, and missing beginning/tail minutes count too.
It opens no SQL connection and writes no account, risk journal or order; console output may be saved
privately for evidence. This is a benchmark sample, not held-stock coverage, production eligibility or
approval of an operating freshness threshold. Tests of the probe's calculations make no external calls:

```powershell
dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --no-restore --filter FullyQualifiedName~TmxOneMinuteFreshnessProbeTests
```

To extend the explicitly authorized check to current paper holdings:

```powershell
dotnet run --project Sandbox -- tmx-held-price-freshness
```

This reads held symbols through the maintained catalogue and existing Shadow/Live repositories,
adds XIU, and refuses more than eight distinct targets. It makes five one-minute polls per target,
then one five-minute comparison and, for a gappy response, one broad reread plus one narrow request
for the first missing minute. The cap is eight requests per target (64 total; up to 192 transport
attempts). Requests time out after 15 seconds, each poll after 45 seconds, and the run after ten minutes.
It requires a reviewed regular-session window and stops rather than catching up a missed poll.
It reports anonymous target labels, bar ages, simultaneous latest-minute coverage, revisions,
missing minutes and interval-comparison counts. Equal volumes alone do not prove no trading occurred
in a missing minute. The final held-symbol reread detects changed target sets, not every intervening
account change. No SQL/account/risk/order writes occur. The source can omit incomplete/null price
rows, so a missing bar is not automatically a verified no-trade minute.

To distinguish provider omissions from client filtering and compare recent reported trades:

```powershell
dotnet run --project Sandbox -- tmx-missing-minute
```

This explicitly authorized read-only probe uses the same saved held-symbol selection and reviewed
calendar. It requests today's session through a completed five-minute boundary at least a minute old:
one-minute rows, five-minute rows, up to 25 recent trades, and one narrow rerequest of the latest gap
per target. At most eight targets, 32 requests and 96 transport attempts; each request has a 15-second
deadline and the run has eight minutes. It stops on failure and writes no SQL/accounts/risk/orders.
Output distinguishes absent, all-null and partially-null rows; compares interval volumes and checks
reported trade timestamps against missing bars. The oldest recent-trade minute may be truncated, so
its volume is excluded from comparisons. Empty minutes between listed trades describe that bounded
provider list, not an exhaustive cross-market tape. Success neither fills gaps nor refreshes old prices.
The ordinary TMX batch method still returns only validated complete bars; unfiltered response points
are exposed only through the diagnostic method. Missing/null response data now fails the ordinary
batch read; an explicit empty list remains an empty response. Keep console evidence private.

To inspect whether quote-page fields offer a usable alternative, with explicit external-read authorization:

```powershell
dotnet run --project Sandbox -- tmx-quote-timing
```

This reads current paper holdings, adds XIU and requires the reviewed calendar with at least five
minutes left in the regular session. At most 16 targets and two polls separated by a 60-second wait:
32 GraphQL requests / 96 transport attempts, 15 seconds per request, 60 seconds per poll and five
minutes overall. Each query compares current quote fields, delayed bid/ask and the latest reported
trade. It prints anonymous availability and timing, then rereads the held-symbol set; keep logs private.
No SQL/account/risk/order writes occur. Success means the diagnostic completed, not that quotes
qualified for valuation. Last-trade timestamps do not date bid/ask; receipt time cannot refresh an old
price. The 120-second trade-age comparison is diagnostic only. Existing valuation rules remain intact.

For the separately authorized Wealthsimple comparison, use the existing saved Windows login:

```powershell
dotnet run --project Sandbox -- wealthsimple-quote-timing
```

This verifies the protected saved session without refreshing or saving it, then requires a reviewed
regular-session window with at least five minutes remaining. It reads current paper holdings through
existing SQL readers, caps them at 16 distinct symbols, resolves each once and takes two quote samples
separated by a 60-second wait. At most 49 logical broker reads including verification; the existing
transport allows at most one DPoP nonce retry per read (98 HTTP attempts). Each target's lookup/quote
work has a 15-second deadline and the complete run has five minutes. No broker balances, pending
orders or order history are read; no orders, SQL writes, token refresh or credential persistence occur.
A changed saved login invalidates the diagnostic. The quote reader retains existing Canadian-equity,
sell-eligibility and identity checks; an unsupported target does not silently switch providers.
Anonymous output reports source/receipt times, quote availability, timestamp advancement and whether
prices changed, without prices or account identifiers. Keep logs private. Completion only establishes
the reported sample; timestamp meaning, continuous coverage and portfolio valuation remain unqualified.
The 120-second comparison is diagnostic, not a chosen production limit. Pre-open verification may
confirm authentication but requests zero quotes and exits unsuccessfully as `OutsideRegularSession`.

## Oracle — LLM narration

### AI Momentum API key and starting-portfolio inspection

Reuse the existing Oracle credential before creating another key. A September 30 read-only check
confirmed it in `%APPDATA%\Microsoft\UserSecrets\00ba8049-f46a-4e8b-8262-a6ac14848c58\secrets.json`,
under `Oracle:OpenAi:ApiKey`. `LlmClientFactory.LoadOracleConfiguration()` loads that store through
`AddUserSecrets`, then environment configuration. Pass it to `CreateAiMomentum`; loading configuration
does not enable calls. Environment-only checks do not inspect user-secrets. Do not print the file
or use `dotnet user-secrets list` in captured output.

The explicitly authorized `dotnet run --project Sandbox --no-build -- ai-momentum-access` check
makes one 20-second-bounded GET to OpenAI's requested-model endpoint, without redirects or retries.
It prints only time, HTTP status, exact-model visibility and elapsed time. It does not generate output,
send portfolio inputs, create journals or access SQL. Metadata success does not establish generation
permission, funding or actual costs. Project status records the dated result.

If a new key is needed, create one in the [OpenAI API dashboard](https://platform.openai.com/api-keys), following
the [official quickstart](https://developers.openai.com/api/docs/quickstart). Enter it locally,
not in chat, source code or a command-line argument:

```powershell
./Operations/Set-TraderVIOpenAiKey.ps1
./Operations/Set-TraderVIOpenAiKey.ps1 -CheckOnly
```

The first command prompts with masked input and writes the existing `OPENAI_API_KEY` setting
to the Windows user environment and this PowerShell process. Environment storage is not encrypted;
`-SessionOnly` instead keeps it only in this process and programs launched from it. CheckOnly reports
presence, never the key. It does not verify authentication, model permission, funding or readiness.
Already-running apps and their launchers retain their old environment; when startup is connected,
launch from the configured PowerShell session or a refreshed launcher. The existing explicit
`Oracle:OpenAi:ApiKey` configuration takes precedence over this environment fallback.
Neither command calls OpenAI, starts TraderVI or enables reviews/trading.

Run [Read-AiStartingPortfolio.sql](../Operations/Read-AiStartingPortfolio.sql) against `TraderDB`
to view the selected Breakout 3 / `PortfolioRulesOnlyV1` source. It returns current cash from
`ShadowPortfolio`, open holdings from `ShadowPosition`, then saved review records from
`AiPortfolioReview`. It requires one unique active match via `PaperComparisonAccount`; it never
silently picks one of several matches. These live source rows are not an injected starting snapshot.
Only a subsequently saved review's `RecordJson` freezes the actual AI input and answer. Keep results private.
The query makes no persistent writes; separate result sets are inspection, not a coherent risk packet.

### Current composition boundary

The desktop reads `%LOCALAPPDATA%\TraderVI\AiMomentum\review-settings.json` once on startup.
Absent settings or `Enabled: false` leave the reviewer off. Invalid/incomplete settings or missing
history report attention; startup never initializes replacement history. `AiPaperReviewSettings`
requires `Enabled`, `PortfolioId`, `ActiveChoiceId` (nullable), `JournalRoot`, `CalendarPath`,
`InitialSession`, `RiskMaximumAge`, `CatalogMaximumAge`, `ContextMaximumAge`, `HistoryDays`,
`LearningRefreshInterval` and `IndependentBlocks`. Use JSON property names exactly as written,
ISO dates, .NET TimeSpan strings and explicit arrays. Keep identities/settings private, with no key
in this file. Input ages/history scope must be selected for the connected test, not guessed from
diagnostic limits. `InitialSession` remains the schedule journal's original initialization date.

Prepare one stable protected root outside the checkout before activation: `shared` holds the
spending and learning journals for all AI roles; `paper/<portfolio-guid>` holds the selected
account's risk and scheduling journals. All four existing JSON/lock pairs are required. Establish
qualified risk history and an explicit current learning snapshot; startup does not invent lessons
or reset their expiry. Preserve pending/interrupted files for reviewed recovery. Changing the
root or deleting history is not a budget or risk reset mechanism.

The initial host uses the coordinator's existing saved Shadow book/bar reader for the selected
Breakout account. It does not start TMX collection or adopt broker quotes. The AI's proposed
switches are saved opinions; the host does not switch the running engine or move holdings.
The 30-second background tick checks the durable scheduler, with the agreed five-minute starting
interval and open/open+3h/close-15m checkpoints. Only one review runs at a time; paid-review cadence
remains 1–15 minutes as requested by the AI. Closing WPF cancels/awaits the task and disposes its
owners. Unexpected failures stop this owner; errors never dump packets or credentials to the UI.
Private settings/journals were prepared October 1 with `Enabled: false`; one connected review and
SQL readback are verified. The October 1 publication was subsequently restored to all portfolios. A follow-up
review with those signals stopped before generation at the input-token limit; do not repeat the oversized
packet unchanged or raise the limit as a workaround. The concise view passes a saved-packet count-only
check and one fresh wait decision; later desktop input grew beyond the cap. The approved v3 view omits
internal engineering/testing fields and detailed unavailable-choice data while retaining dated blocking
notices and independent managed-account/risk/learning context. All frozen source evidence stays saved.
It shares table defaults and uses @N references (literal leading @ is escaped as @@); expansion equality
checks packing after the declared omissions. The largest revised saved example fits the unchanged cap.
A saved count does not establish current readiness, model interpretation or a bound on future growth.
Check current inputs and obtain bounded-generation authorization, then qualify v3 interpretation and
repeated successful desktop reviews before enabling automatic startup. No v3 desktop installation or
model answer is qualified. See project status for dated results. This does not enable trading.

The explicit Sandbox command `ai-paper-first-review` has seven modes, selected with
`TRADERVI_AI_FIRST_REVIEW_MODE` (default `Preflight`). Set `TRADERVI_TSX_CALENDAR_PATH` to the
reviewed calendar's absolute path for preflight/preparation. `Preflight` reads the selected
BreakoutTop3 / PortfolioRulesOnlyV1 book and reports counts/timestamps only. It needs SQL access.
`Signals` uses the existing private settings and the same catalogue/engine readers as the AI
coordinator to inspect every choice. It reports strategy family, gate profile, context freshness,
proposal availability and timestamps without holdings, prices or account IDs. This mode is SELECT-only:
it does not initialize/update journals, call the model, advance a monitor or change eligibility.
`Prompt` reads the latest local saved review packet and reports its original/rendered character sizes
without SQL, API access or journal writes. `PromptCount` additionally sends that saved view, current
instructions and response schema to the same OpenAI input-token counter used for admission. It requires
explicit API authorization but never sends a generation request or reserves spending. A saved packet
may be stale: these modes measure its representation and do not validate it for a current decision.
`Prepare` additionally requires the operator-approved positive integer
`TRADERVI_AI_FIRST_REVIEW_AGE_MINUTES`; it applies that age to risk, catalogue and engine observations,
uses 15 sessions of context and a five-minute lesson consultation interval. The operator accepted
ten minutes for the first review on October 1. This does not qualify a continuous-operation default.

Before Prepare, protect the stable private storage root for the operator and system administrators.
Prepare validates the current opening book, creates all four journal pairs once, records the initial
risk observation and publishes an empty lesson snapshot expiring at today's reviewed close. It leaves
`Enabled: false`; existing files or an interrupted `.preparing` marker require reviewed recovery.
It does not write SQL or call the model. `Review` reopens that disabled configuration and runs one
scheduler admission, with a four-minute command deadline and existing spending guards. It may call
OpenAI and save Pending/Reviewed/Rejected SQL evidence. It prints only status and token/cost totals,
never packets, credentials or raw exception bodies. It starts no loop, changes no enabled setting,
and never retries automatically. Inspect journals before repeating an uncertain attempt; preserve
reservations and recover saved answers through the existing coordinator. A new session requires a
current qualified learning snapshot; this initial empty snapshot is not renewed automatically.

`ContinueEmptyLearning` is separately authorized preparation for a later session while no evaluated
lessons have ever been published. It reopens existing learning history and appends an empty snapshot
with the current publication time and today's reviewed close as expiry. It requires disabled settings,
a later open reviewed session, an expired latest snapshot and no Pending review. Any previous evaluated
lesson, missing/corrupt/interrupted history or repeated same-session continuation blocks it. It leaves
the original settings/session, spending, risk, scheduling and prior learning/review records intact.
It calls neither SQL nor the model and is never invoked by runtime startup. This does not evaluate
outcomes, qualify lesson criteria or authorize a review. After authorization, use this mode once,
then the separately authorized `Review` mode with fresh market-hours data and the existing guards.

For an explicitly authorized desktop lifecycle test, supply `TRADERVI_AI_REVIEW_TEST_UNTIL_UTC`
only in the new WPF process environment, in exact `yyyy-MM-ddTHH:mm:ssZ` form. It must be future and
at most ten minutes away. Existing saved settings must have `Enabled: false`; startup enables only
an in-memory copy. The loop ends after two distinct new completed attempts (success or failure),
or cancels at the deadline and awaits pending cleanup. Old recovered attempts and unchanged idle ticks
do not count. Cadence is not accelerated; a long AI interval can mean only one review before timeout.
An expired/invalid deadline or enabled/missing saved configuration fails closed. Normal startup with
no test variable retains the saved enabled/disabled behavior. Never persist this variable for the user
or machine. The existing spending caps and ten-minute first-test ages require their applicable approval.

Before launching, get authority for the operational desktop restart and up to two paid reviews. A normal
restart also resumes existing paper collection and broker refresh and can record a monitoring gap.
Use normal window close, wait for process exit, and honor pending-operation/unsaved-work prompts; never
force-stop to make qualification pass. Inspect saved attempts, spending reservations, SQL/local matches,
loaded binaries and journal ownership. After the test, verify the saved disabled settings and restart
without the test variable to check disabled startup. Neither the bounded test nor a successful build
qualifies continuous operation, invested coverage, eligible selection or AI trading.

AI Momentum's review-only Core host (ADR-0087) is connected to WPF startup in source and remains disabled
until explicitly configured. It has no nightly host or execution path.
Running Oracle does not start it; Oracle's `--dry-run` can still contact the model. Use the synthetic
tests for offline validation:

```powershell
dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~AiMomentumClientTests|FullyQualifiedName~AiPortfolioReviewerTests|FullyQualifiedName~AiLearningWorkflowTests|FullyQualifiedName~AiReviewSchedulerTests|FullyQualifiedName~AiScheduledPortfolioReviewerTests|FullyQualifiedName~AiReviewScheduleJournalTests'
```

Before any future AI Momentum composition, review billing and expiring currency/tax bounds, establish
one protected private journal directory outside the checkout, and qualify model access. Preserve
`ai-calls.json`, its initialization/ownership marker `ai-calls.lock` and any interrupted `.pending`
file during reviewed recovery; deleting them or creating a new directory would bypass local history.
Never put the journal's private prompts/responses in commits or ordinary application logs. Numerical
risk and handover approval plus the existing execution prerequisites still precede live activation.

Learning-aware composition uses `AiLearningAwareSupervisor` and `AiPortfolioExecutionCoordinator`
with one `AiLearningJournal`. Preserve its `ai-learning.json`, `ai-learning.lock` and any `.pending`
file under the same private-storage rules. Snapshot publication belongs to a trusted outcome evaluator;
do not mark model hypotheses as evaluated lessons. Both periodic refresh hooks need explicit host
intervals. Neither hook creates a timer, runs a model or enables orders. A Current preparation check
still requires fresh broker/strategy/risk validation at execution time. The initial empty production
learning snapshot was prepared October 1; outcome evaluation and automatic renewal remain unfinished.

The source-only `AiReviewScheduler` and `AiScheduledPortfolioReviewer` accept explicit test/host ticks;
construction starts nothing. Supply a reviewed calendar session and checkpoint timing explicitly.
Use `AiReviewScheduleJournal` for durable scheduling, with the exact account/portfolio/mode, original
session/configuration and reviewed calendar. Its recovery path preserves pending events, timing and
blocks; do not replace it with a newly constructed in-memory scheduler. Preserve `ai-review-schedule.json`,
its `.lock` ownership/initialization marker and any `.json.pending` artifact. Initialize explicitly once
in protected private storage; missing, changed or interrupted files require reviewed recovery, not deletion
or a new directory. An interrupted attempt keeps its original identity in history but is never
resubmitted. Call-journal reservations remain untouched; a new successful fresh review is still required.
Advance explicitly to today's reviewed session after the old call has completed or recovered; this
retains pending evidence and does not replay missed sessions. Full history is bounded at 32 MB; qualify
retention, backups and sustained I/O before production use. Shared spending/learning storage rules
remain unchanged. The scheduled reviewer uses the source-tested version-3 interval/reason contract;
earlier direct version-1/2 reviews retain their contracts. Accepted timing is saved with completion,
not as a separate write that could survive a failed completion. No production schedule, automatic
protection integration or operating account/input binding is installed.

**Project:** `Oracle`
**Entry point:** `Oracle/Program.cs`

Oracle consumes deterministic decision dossiers and produces optional narrative analysis. Depending on configuration, it may call an external LLM service and write `LlmNarrative` rows.

Review `Docs/oracle-rules.md` and `Docs/oracle-phases.md`, confirm provider configuration and token/data handling, and obtain explicit authorization before running it.

## Sandbox — probes and backfills

List probes without running one:

```powershell
dotnet run --project Sandbox
```

Run a selected probe:

```powershell
dotnet run --project Sandbox -- <slug>
```

Each probe has its own external and database effects. Read `Sandbox/AGENTS.md` and the selected probe's summary before execution. Backfill probes must be treated as database maintenance, not test fixtures.

## Operational troubleshooting

### Missing SQL object

A DACPAC build does not update SQL Server. Confirm the runtime database, add a narrow dated migration and matching canonical schema definition, build the project, execute only the explicitly authorized migration, and verify the object afterward.

### Multiple enabled registry rows

`ModelRegistry` should have no more than one enabled row per `TaskType`. Delphi additionally filters registry rows against the currently enabled code registry, so historical retired task types may remain enabled without loading; they are still cleanup debt.

### ML.NET schema mismatch

Feature vectors must have fixed size. Training and inference must use matching schema definitions and feature builders.

### Sector or benchmark gaps

- Check `SectorIndices` coverage before debugging relative-strength calculations.
- Verify stock-sector mappings and the `TsxSectorMap` normalization aliases.
- Verify external benchmark symbols still resolve before changing indicator logic.
- A TMX quote `404` can mean a retired listing, rather than a temporary outage. Review issuer/exchange
  corporate-action evidence and the symbol's last stored price before changing its active flag; never
  deactivate automatically from a provider error alone. Migration 036 retires ARX and SES after their
  September 2026 acquisitions, preserving price and sector history. It was applied September 17.
- A partially successful weekly sector refresh advances the current global refresh timestamp. A later
  rerun can skip that refresh, so a successful rerun alone does not prove failed lookups were repaired.
  Verify each failed symbol explicitly before closing the incident.

### OBV or CLX unavailable

- Confirm `SymbolObv` has adequate per-symbol history.
- Run the OBV backfill only when explicitly authorized.
- Confirm `MarketClimax` exists and Hermes has written at least one row.
- CLX is diagnostic-only; missing history should degrade reporting rather than alter ranking.

## Automatic daily-source recovery (ADR-0066)

After manual migration 032 and a WPF restart, active Shadow and Delphi Live sessions adopt newer Valid OfficialPaper results for today, the prior reviewed TSX session and the assigned daily strategy. No session reset is needed after a late Delphi run. Inspect the account explanation/activity and Live source warning for the active run and adoption time. Original freezes and coverage gaps remain; old pending buys are cancelled and confirmation restarts. Daily gates still apply. WPF waits for its monitor to release the durable lease when closing.
