# TraderVI Project Status

**Snapshot date:** 2026-09-14
**Purpose:** Fast orientation to what is implemented, operational, and currently blocked. Update this document after major milestones or when the daily workflow changes.

## Executive summary
**Nightly preparation repair (2026-09-14):** ADR-0078 retains the latest-source requirement while
capturing a verified private source copy, restoring locked dependencies into a dedicated cache, and
building Hermes/Delphi/Athena outside development output directories. Source capture and transient
restore failures have bounded retries; operational stages are never automatically repeated. The
existing scheduled task reads the updated runner directly and does not need reinstallation.

All **1,146 Core tests** and **36 offline runner assertions** passed. An actual isolated three-project
build and a deliberately mismatched-lock rejection passed; lock files preserve existing resolved package
versions. WPF built with zero errors and 261 existing compiler warnings. No full solution/SSDT build or
fresh dependency-security audit ran. Known dependency advisories remain tracked separately.

SQL confirms Friday September 11 is present: 480 daily bars, XIU/SPY through that session, and a Valid
September 12 official run. September 14's original 00:30 attempt built successfully but Hermes received
one BadGateway collection error, stopping Delphi/Athena after a verified backup. The operator authorized
a recovery through the updated runner. Its first isolated build and backup passed, but another symbol
returned HTTP 502. The historical TMX read now uses the existing bounded transport retry policy; an
explicit recovery after this correction is pending. Outstanding source
work was preserved in commit `ea8d9d5e` (215 files); generated/private artifacts remain excluded.

**Manual holdings sells in WPF (2026-09-13):** the operator explicitly chose one-click sale of the
entire holding. The main Wealthsimple holdings grid now has **Market sell all** and **Limit sell…**
beside each row's share count. Market sell pins the displayed account/security/quantity, rechecks
regular-session quote, exact holdings and pending orders, durably records intent, then submits once.
Changed or fractional quantities stop the action; closed-market clicks never queue an order.

Buy and sell share one local submission lock and protected journal lease. Unresolved orders block
both sides; a terminal sell retains its reservation until fresh holdings confirm the expected remaining
shares. **Manual sells…** supports restart/status recovery and actual proceeds. No automatic SQL
position closure occurs. The limit dialog is explicitly a preview: placement remains disabled because
the supplied HAR contains only market orders. See [ADR-0077](adr/0077-manual-holdings-market-sell.md).

Validation: **293 focused tests passed**; the isolated WPF build and **16 synthetic renders** passed,
including command routing, stale/fractional row checks, normal/narrow dialogs, clean bindings and
Windows-encrypted sell recovery. Existing compiler warnings remain. Separately, cached dependency
advisories include critical `System.Drawing.Common 5.0.0` and existing moderate/high advisories.
No fresh dependency audit, full solution/SSDT build, live broker request, SQL operation, operational-host
launch/restart or commit ran. The running app needs a rebuild/restart to use the controls; native sell
acceptance remains unverified. Evidence: `artifacts/manual-market-sell-ui-20260913/`.

**Wealthsimple market-sell client (2026-09-11):** the operator supplied a HAR of buying and selling
one FEC share. Core now implements `WealthsimpleMarketSell`, `SubmitMarketSellAsync` and
`ReadMarketSellOrderAsync` with the captured `SELL_QUANTITY` / `MARKET` / `DAY` contract. Sell preflight
checks current sellability independently of buyability. Shared buy/sell transport and cumulative-fill
parsing retain the existing buy API and protected manual-buy record format. Compatible local attempt
markers prevent retransmission even if side or quantity changes under the same request ID.

The new capture also confirms the existing market-buy request contract. Synthetic request/response
fixtures preserve protocol fields while replacing identifiers, timestamps and financial values; privacy
checks found no captured order/account/security identifiers or authentication headers in those fixtures.
The original Downloads HAR is unchanged. See [ADR-0076](adr/0076-wealthsimple-market-sell-protocol.md).

Validation: **235 focused tests passed**, including 53 new buy/sell protocol and safety cases, and an
isolated WPF build succeeded. Existing nullable-context/unused-variable compiler warnings remain.
Separately, cached package advisories include critical `System.Drawing.Common 5.0.0` and existing
moderate/high advisories; no fresh dependency audit or full solution/SSDT build ran. No live broker call,
SQL operation, operational-host launch/restart or commit occurred. Sell UI, durable reviewed sell
workflow, available-share reservations and position/cash reconciliation remain unfinished; WPF's manual
ticket remains buy-only. Local validation logs: `artifacts/wealthsimple-market-sell-20260911/`.

**September 11 overnight verification (08:54 Toronto):** the 00:30 scheduled pipeline completed
all three stages by 00:37:05. Release preflight, Hermes and Delphi succeeded. Hermes inserted 481 bars
with zero collection failures and recorded a checksum-verified backup with a SHA-256-verified OneDrive
local copy. SQL confirms a Valid September 11 official run `3fbcd615-a831-4ddf-9ded-64de8a9ff802`
on `v3.4-observed-benchmarks`, using September 10 data: 211 candidates, 422 lens evaluations,
zero observation mismatches, and 25 published Hold rows per lens blocked by Breadth. XIU/SPY and the
checked derived series are current; four active symbols remain older and seven feature inputs are excluded.

Athena completed with **Attention** after writing 154 valid outcomes and nine invalid delayed-intraday
outcomes. Six invalid outcomes belong to the current strategy and three to earlier strategies; their
required initial session/bar evidence is missing or mismatched. Research coverage remains insufficient
for primary scores. Repeating the pipeline would not repair those historical first-received paths,
so no recovery run was needed or launched. This was SELECT-only/log verification; evidence and the
backup-verification limits are in ignored `artifacts/nightly-readiness-20260911/`.

**Manual WPF market buys (2026-09-10):** the operator approved the shared ticket and explicitly
required editable manual share quantities. After the operator's presentation follow-up, Delphi Picks
uses one **Manual buy…** action for the stock selected in either lens, and broker holdings has one
secondary **Buy more…** action for the selected position. Repeated row Buy columns were removed;
clearing selection or refreshing holdings clears the buy target. Both actions open a compact dialog with a blank Shares field, optional ±10
shortcuts, verified TFSA/instrument, current ask, estimated cost and available CAD cash. The final
button states the exact shares and symbol. The old existing-fill/Ghost entries remain separate.
Portfolio sizing, strategy activation and automatic selling are unchanged.

The new workflow checks regular-session quotes, cash, pending orders and existing buying pauses/
financial holds, then durably saves the reviewed intent before one explicit transmission. Protected
Windows order records support restart/status recovery and cumulative actual fills; ambiguous outcomes
block replacement orders. Holding launches pin their broker security identity. No automatic SQL
tracked-position insertion occurs. See [ADR-0075](adr/0075-manual-market-buy-ticket.md) and
[the operating steps](running.md#manual-market-buys-in-wpf).

Validation: **164 focused tests passed** for the order workflow. The selection-action follow-up passed
an isolated WPF build and **21 synthetic WPF renders**, checking normal/narrow layouts, editable
quantity/cost/action bindings, selection changes/clearing and holdings refresh without a stale target,
separate existing-fill entry and Windows-protected recovery. Existing compiler warnings remain.
Separately, cached package advisories include critical `System.Drawing.Common 5.0.0` and existing
moderate/high advisories. No fresh dependency audit, full solution/SSDT build, live broker call, SQL
operation or operational-host restart ran. Native live submission remains unverified. The running app
still needs a rebuild/restart to use this source. Local evidence: `artifacts/manual-market-buy-ui-20260910/`.

**Wealthsimple market-buy client (2026-09-10):** the operator supplied a successful whole-share market
buy HAR. The Core client now implements the captured `SoOrdersOrderCreate` request, current Canadian
security eligibility checks, and a separate account/identity/security-scoped order-details read for
actual cumulative fills. A write-once, hash-only local attempt marker prevents retransmission of the
same request after ambiguity/restart. It does not substitute for a full execution journal, durable
reviewed intent, cash/risk checks or broker-to-local reconciliation. Auth/read nonce retries remain;
mutation retries are disabled. Exact Canadian ticker matching now recognizes venue-specific suffixes.

All 125 GraphQL exchanges (124 with response bodies) are indexed under ignored
`artifacts/wealthsimple-market-buy-20260910/README.md`; credentials/identifiers are redacted and financial
values retained for the operator. The original Downloads HAR is unchanged. Checked-in fixtures are
synthetic. No captured request, live broker mutation, database update or operational host run was made.
The market-buy method is not connected to the WPF promotion/strategy execution flow; limit operations
remain preferred and unfinished. See [ADR-0074](adr/0074-wealthsimple-market-buy-protocol.md).

Validation: **141 focused tests passed**; the isolated WPF build succeeded without replacing the
running application's files. All 250 exported request/response JSON files parse; known account,
identity, security and external-order IDs are absent from the inspection copies. Existing compiler
warnings remain. Separately, cached dependency advisories include critical `System.Drawing.Common
5.0.0` plus prior moderate/high advisories; no fresh package audit or full solution/SSDT build ran.

**September 10 opening engine verification (09:48 Toronto):** the operator-started Debug WPF host
(PID 67296, started 09:12) is responsive. Delphi Live holds one current lease and has completed three
scheduled cycles through the 09:45 endpoint, with 68 usable receipts and complete valuations for all
three accounts. The account with holdings has changing saved values. Today's valid daily source is
frozen and `HostGapObserved=false`, but session coverage is **Blocked** by 15 `NoCompletedBar` receipts
across nine observed symbols; none is an open Live holding. A closing HostCoverageGap recovered at
startup belongs to September 9, not today's session.

All 12 Shadow portfolios/sessions are Active. The 09:47 poll saved valid five-/fifteen-minute evidence,
filled three previously pending paper exits with zero timing violations, and refreshed all seven
remaining open positions to today's prices. The tracked-position monitor also ran, but one fifteen-minute
request still has `CompletedBarConflict`. No global buying pause or Live account capital-review hold
is active. Paper displays read saved updates approximately every 30 seconds; collection uses delayed
completed bars. The Wealthsimple TFSA panel still requires explicit Refresh for new account snapshots.
This was SELECT-only/process/source verification, with no host restart, strategy/rule change, data repair,
broker action or build. Evidence and display limitations are in ignored `artifacts/engine-readiness-20260910/`.

**September 10 pre-open nightly recovery (09:11 Toronto):** the 00:30 task failed during the
Hermes/Core build on three `Math.Min` namespace-resolution errors; none of Hermes, Delphi or Athena
started overnight. The current source already contained the `System.Math.Min` correction. At the
operator's request, the normal guarded runner rebuilt all three Release projects and completed the
recovery by 09:09:12, before the open. Hermes succeeded with 481 daily bars plus one SPY observation
and a checksum-verified backup whose staging/OneDrive local copies independently hash-match.
XIU/SPY, derived data, sector indices and US indices are current through September 9; four active
symbols remain older. OneDrive cloud synchronization and test restore remain unverified.

Delphi saved Valid official run `3fbd701a-2547-4b09-a1fb-ea0ed1c31cd3` on
`v3.4-observed-benchmarks`, recommendation date September 10 / market-data date September 9,
with 210 candidates, 420 lens evaluations and zero candidate-observation mismatches. Both lenses
publish 25 Hold rows blocked by Breadth. Athena completed with **Attention**: 486 valid outcomes
and four invalid ten-session labels from old `v3.0` evidence with `MissingSymbolSession`.
The current strategy's outcome report remains pending; today's Delphi run is Valid.

No source correction, schema migration, model training, WPF/paper-monitor launch, broker order or
commit occurred. The three focused builds passed with existing compiler warnings; no fresh dependency
audit or full solution/SSDT build ran. The original overnight failure and verified recovery evidence
are preserved under ignored `artifacts/nightly-readiness-20260910/`.

**TFSA transition review implementation (2026-09-10):** Trading → Main account now shows
paginated pending Wealthsimple orders and offers **Review TFSA strategy transition**. The review
ranks active Delphi Live paper accounts on the same latest saved date, labels partial observations,
shows the exact current strategy version, and resolves intended paper holdings to Canadian broker
security identities. Matching holdings are retained at actual quantities; non-overlaps are identified
for later exits. An unresolved instrument cannot trigger an inferred exit. Whole-share limit intents
reject fractional quantities; buy sizing rounds down to tens when at least ten shares fit and allows
smaller whole-share purchases within cash/risk limits. These are review/sizing components, not live
execution. Pending orders block transition planning; cash ceilings exclude anticipated sale proceeds.

The new pending-order query and a unique Canadian instrument match for an existing holding were
verified through the saved session with an isolated read-only probe.
No broker order, strategy activation, local fill, schema migration or operational Delphi run was made.
Completed-fill reconciliation, durable broker-account binding/order journal, verified submit/cancel
contracts and broker-backed Delphi Live execution remain unfinished. The login HAR contains no
observed equity-order submission; another operator capture is pending. The latest saved Live results
are September 9 partial observations at 15:55 Toronto: operational champion and normal comparison
0.00%, portfolio-rules-only comparison −0.4855%. A flat winner is not a current entry signal.

Validation: WPF build succeeded; 112 focused authentication, account, transition and presentation tests
passed; ten synthetic account/transition renders had clean bindings. No operational WPF application
or full SQL solution build was run. Existing nullable-context/unused-variable compiler warnings remain.
No fresh dependency-security audit was performed; previously reported package advisories remain open.

**TFSA execution design discussion (2026-09-10):** the operator accepted a permanent self-directed
TFSA, independent paper accounts, primary strategy promotion as future real-order authority,
reconciliation and whole-share-only TraderVI orders. Ten-share buy increments are a soft preference.
[ADR-0073](adr/0073-permanent-tfsa-and-whole-share-execution.md) records these decisions. The
[continuing draft](concepts/strategy-promotion-and-execution.md#tfsa-integration-draft--september-10-2026)
now records the accepted overlap exception: retain existing holdings the incoming strategy intends to
hold and transfer their management, avoiding a promotion sell/rebuy. Unconditional liquidation is
superseded. Non-overlap exits, activation timing and the order lifecycle remain open.
The design now has the limited review implementation above; the broker boundary remains read-only.

**Wealthsimple TFSA landing page (2026-09-09 Toronto / September 10 UTC):** native sign-in is
operator-confirmed. The main Trading page now reads the open self-directed TFSA through the saved
native session, showing account value, cash, buying power, net deposits, provider return and holdings.
The separate managed TFSA is excluded. Loading follows sign-in; Refresh updates the in-memory snapshot.
Fractional shares and currency labels are preserved; missing values remain unavailable. Existing
recorded holdings and strategy controls sit below it, without automatic SQL reconciliation.
See [ADR-0072](adr/0072-wealthsimple-tfsa-account-display.md).

A live isolated read using the operator's Windows-encrypted session succeeded for account discovery,
financials, trading balances and holdings. No credentials or private account values were logged.
**86 focused authentication/account/presentation tests passed**, and the normal WPF Debug output builds.
Eight synthetic renders cover loaded, disconnected, unavailable and empty states at two widths.
Existing compiler warnings and cached package-security advisories remain separate (including
`System.Drawing.Common 5.0.0` critical severity). No fresh audit, complete solution/SSDT build,
operational dashboard launch, SQL/trading/model workflow, order or commit ran. Live refresh rotation,
broker-to-local reconciliation and order lifecycle remain unfinished.

**Native Wealthsimple sign-in is now the default (2026-09-09 Toronto / September 10 UTC):** the
operator rejected browser-first login. The WPF connection window now submits email/password directly,
then asks for and submits a 2FA code when required. It uses the existing auth client and optional
Windows-encrypted session storage. Passwords/codes are never persisted; closing/cancelling or a
ten-minute timeout clears an unfinished attempt. Timer checks preserve pending 2FA. A browser is
offered only for an explicit provider security challenge. See [ADR-0071](adr/0071-native-wealthsimple-login.md).

The operator also demonstrated successful browser login with failed native handoff. A real offline
WebView2 probe reproduced `NotImplementedException` on the COM request stream. The fix uses bounded
zero-offset byte-array reads, clears buffers, and schedules browser closure after the callback.
The passkey setup page was not the cause. At that earlier step, native password/2FA and refresh still needed operator live
verification. TFSA data synchronization, reconciliation and order APIs remain separate unfinished work.

Validation for this step: **54 focused auth tests pass**, the WPF build succeeds, and the actual
browser stream adapter passes its offline probe, and 11 renders verify native/OTP/fallback/connected
UI and sidebar bindings. Windows encryption and file ownership checks also pass. Existing compiler
warnings and cached dependency-security advisories remain separate. No operational app, database,
model workflow, live native login, live refresh, order, commit or full solution/SSDT build ran.

**Earlier Wealthsimple desktop sign-in (2026-09-09 Toronto / September 10 UTC):** WPF now has
a connection window opened from the bottom of the left sidebar. It displays the provider's login
page in a private WebView2 browser, with password saving disabled, and accepts a supported bearer
session only after native token-info verification. Optional remembered sessions use Windows
CurrentUser encryption in LocalAppData; email/password/OTP values are not saved. The sidebar shows
green verified, red disconnected, and amber checking/reconnecting/unavailable, including when
collapsed. Checks run independently of SQL/trading work, refresh is serialized, and uncertain
sessions are removed before they can be restored. The sidebar's compiled property bindings also
now keep its icons and collapse toggle synchronized. See [ADR-0070](adr/0070-wealthsimple-desktop-sign-in.md).

The normal WPF Debug build succeeds, **45 focused authentication/connection tests pass**, and eight
offline renders verify status colors, labels, compact layouts and navigation bindings. A Windows
fixture verifies actual encryption/decryption, corruption rejection, session deletion and exclusive
storage ownership. Existing compiler warnings remain; cached dependency advisories reported by the
test project are separate, including `System.Drawing.Common 5.0.0` critical severity. No full
solution/SSDT build or fresh dependency audit ran. No app launch, DB/trading/model workflow, live
password login, live refresh or commit ran during this UI step. The operator stopped debugging;
the normal output is rebuilt for their next launch. Embedded-browser challenge compatibility and
live refresh still need operator verification; account reconciliation and execution remain future work.

**Earlier direct authentication proof (2026-09-09 Toronto / September 10 UTC):**
the operator chose direct integration and supplied a local browser login capture. The isolated
`Core/WSTrade/Authentication` client imported its bearer session and received HTTP 200 from a fresh
Wealthsimple token-info request. The observed password/OTP flow issues a 1,800-second token; Turnstile
is present and this login uses no DPoP. No raw capture or credential values were copied into notes or
tests. The tool retains session values only in memory; HAR files are ignored by Git.

The `Tools/Wealthsimple.Auth` host built under SDK 10.0.401 and its initial **31 focused auth tests passed**.
Password/OTP methods, refresh rotation/failure handling and optional DPoP proofs are verified offline;
live fresh login and refresh remain unverified. Login UI and protected persistence were subsequently
implemented above. Account reconciliation remains future work; authentication grants no order authority.
No database, trading, market-data, model workflow, app restart or commit ran. Existing compiler
warnings and cached package-security advisories remain separate (including Drawing.Common critical
severity); no fresh dependency audit or complete solution/SSDT build was run. See
[ADR-0069](adr/0069-direct-wealthsimple-authentication.md) and
[protocol/usage notes](concepts/wealthsimple-authentication.md).

**Portfolio banner removed and filters localized (2026-09-09):** the page-wide Main Trading strategy
banner is removed. Compact period/reference filters now sit at the top right of Strategy comparison,
with its saved date range inside the card. Performance retains equivalent compact filters and date
context; Trading and Strategy review retain the main assignment information. The selected-account
panel starts higher on the page. Period, daily-date, account selection and refresh behavior are unchanged.
Isolated WPF Release build and 22 offline renders/checks passed with clean bindings, including control
placement, reclaimed height, filter behavior and Performance access. Existing compiler warnings remain.
No Core test rerun for markup-only changes, fresh dependency audit, full solution/SSDT build, operational
restart or database/external workflow. Evidence is in ignored `artifacts/portfolio-card-controls-20260909/`;
the normal app needs a rebuild/restart.

**Portfolio comparison layout simplified (2026-09-09):** Evidence and Max decline are removed from
the grid. Both remain in the selected-account panel, which now receives one-third of the available
column space instead of one-quarter, with a 320-unit minimum. Period-return cells retain evidence
tooltips; evidence is grouped under the selected account's period return. The five remaining grid
columns are Strategy / account, Daily gates, Day gain, Period return and Account value.
Isolated WPF Release build and 21 offline renders/checks passed, including the retained detail values,
tooltip and prior selection/refresh behavior; bindings are clean. Existing compiler warnings remain.
No Core test rerun for this markup-only change, fresh dependency audit, full solution/SSDT build,
database/external workflow or operational restart. Evidence is in ignored
`artifacts/portfolio-details-layout-20260909/`; the normal app needs a rebuild/restart.

**Portfolio refresh flicker corrected (2026-09-09):** the 30-second desktop timer previously reread
portfolio/dashboard SQL and cleared/repopulated the grids and date picker after hours. Automatic
display reads now use a weekday 09:30–16:10 Toronto window, with one final read on leaving it. Startup,
reopening Portfolios, explicit actions and Refresh saved data can still read saved results at any time.
Engine wakeups, pre-open liveness, close finalization and operator safety handling remain independent
of this display policy. Background engine/status database checks still run; this is not a zero-I/O mode.

Equal rows/dates now retain their objects and emit no collection notifications. Changed values update
individual rows without resetting the collection; same-account activity stays visible during its read.
Changing accounts clears the old details immediately. The desktop timer also avoids overlapping turns
and runs engine work before portfolio display reads. No trading rules or Core engine schedules changed.
Validation: 55 focused tests and isolated WPF Release build passed; offline repeated-refresh checks
and bindings are recorded in ignored `artifacts/portfolio-refresh-20260909/`. Existing compiler warnings
and cached dependency advisories remain. No operational restart, database/external workflow, or full
solution/SSDT build ran. The running app needs a rebuild/restart to use this correction.

**Portfolio day gains implemented (2026-09-09):** All portfolios now has a **Gains for** date selector
and a **Day gain** column showing currency amounts and percentages. The selected-account panel shows
the session opening, saved day value and evidence time. First-day accounts can show daily gains even
when their shared-period return lacks an earlier endpoint. Cash-only valued days show zero; intraday
observations are labelled Partial and missing valuations remain unavailable. Changing the selected
day does not change the shared comparison period. Older date selections survive refresh.

Shadow uses saved session openings/closes, with current same-day saved holdings/cash available for
the latest unfinished session. Live uses saved openings and latest checkpoints/closes; incomplete
latest marks never fall back to earlier values. The calculation follows existing engine opening
baselines and recorded costs. It does not add a new snapshot cadence or unattended runtime host.
Focused validation passed 31 Core tests and an isolated WPF Release build. Offline fixtures exercise
all 15 accounts, historical dates, first-day/zero/partial results, selection preservation and existing
profile/promotion guards, with clean bindings. Evidence is in ignored
`artifacts/portfolio-day-gains-20260909/`. Existing compiler warnings and cached dependency-security
advisories remain. No database/external workflow, operational restart or complete solution/SSDT build
was run. A rebuild/restart of the user's app is needed to display this change.

**Read-only portfolio activity verification (2026-09-09, 18:19 Toronto):** all 12 current Shadow
portfolios and their generations are Active. All 12 have a completed September 9 session; Completed
is the day's session state, not account retirement. The three Delphi Live generations/assignments
remain open-ended and uncancelled, with no ledger daily-buying or capital-review holds. Their latest
checkpoint is 15:55 Toronto. The rules-only Live account has two recorded fills and two open positions;
the other two Live accounts remain cash-only. This confirms ongoing configuration, not uninterrupted
daily coverage or guaranteed fills. The desktop host must remain running for scheduled processing.

Today’s Live session is Incomplete / Blocked with HostGapObserved and no September 9 closing mark.
Its stored finish time is 15:58:47 Toronto. Missing coverage was not repaired or relabelled. Shadow's
eight fixed comparison accounts started September 9 at 13:15, have one saved closing date, and cannot
show the grid's September 4–9 period return. Since-funding closing returns are available separately:
four Normal controls 0%; rules-only Breakout Top 3 −0.2349%, Top 5 −0.2394%, Continuation Top 3 +0.4078%,
Top 5 +0.1440%. All saved history will continue to lack a September 4 endpoint for these new accounts.
An account-relative result and separate account/session status would resolve the display ambiguity;
that UI correction is not implemented in this diagnostic turn. Evidence and read-only SQL are under
ignored `artifacts/portfolio-running-status-20260909/`. No runtime, configuration, ledger or schema was
changed; no external service or operational workflow was launched.

**Unified portfolio grid implemented (2026-09-09):** the operator rejected separating fixed-gate pairs
from the standard strategy grid. **All portfolios** now opens one list containing every standard,
Normal and Portfolio rules only paper account. The category buttons and gate-only view are removed.
Daily gates has an explicit column; matched rows stay adjacent in default ordering, with pair context
in the selected-account details. Reference accounts are optional and never replace the paper list.

The shared observed-date return calculation and promotion/profile guards remain unchanged. Newer
accounts without matching period endpoints show unavailable returns. WPF Release builds successfully;
15 focused comparison/gate tests pass. Offline fixtures verify 15 paper accounts (five standard plus
ten paired), reference inclusion, gate labels, matching dates, promotion exclusions and the retained
collapsible sidebar. Nineteen renders and checkbox/menu automation checks have clean bindings.
Evidence is in ignored `artifacts/portfolio-unified-grid-20260909/`. Existing CS0168 compiler warnings
remain; tests report cached dependency-security advisories separately. No fresh dependency audit,
complete solution/SSDT build, operational restart, database workflow or external operation was run.
The current session needs a rebuild/restart to display the unified grid.

**Earlier navigation/discoverability follow-up (2026-09-09; category separation now superseded):** the operator's screenshot confirms
the redesigned Portfolios workspace is running, with the standard Paper strategies scope selected.
The fixed pairs were in a separate scope, which made the accounts that skip daily gates hard to find.
The new **Daily gates on / off** shortcut displays the loaded bypass-account and matched-pair counts;
each pair explicitly labels daily gates applied/skipped while retaining Normal / Portfolio rules only.
The side menu now collapses from 178 to 56 layout units, keeps named icon shortcuts and restores through
the menu button. Collapse state stays with the window and does not change account or workspace selection.

The isolated Release build passes. Nineteen offline WPF renders and an automation-toggle check verify
the 122-unit width gain, collapse/expand restoration, accessible names/tooltips, retained account
selection, five bypass accounts/five complete pairs and clean bindings. Fixtures cover 980×680,
1280×820 and 1600×1000; no operational app or database/external workflow was run. Existing CS0168 compiler
warnings remain. No new dependency audit or complete solution/SSDT build was performed. Core tests
were not repeated for these display changes; the preceding implementation passed 827 tests. Evidence
and the build are in ignored `artifacts/workspace-navigation-20260909/`. The currently running Debug
session needs to be rebuilt/restarted to display this follow-up.

**Main-account workspace implemented (2026-09-09):** Trading now opens on the
selected Real account's reported value/cash, recorded holdings, available gains and assigned strategy.
Attention remains supporting content. Complete account gain is explicitly unavailable without full
cash-flow/cost/income history; recorded sale gains over 30 Toronto calendar days and saved open-position
gains are labelled separately. Wealthsimple remains disconnected and execution authority remains
recommendations only.

The four WPF workspaces share side navigation. Portfolios exposes separate ordinary-paper and fixed-pair
scopes, adjacent Normal / Portfolio rules only results, account activity and direct guarded strategy
review. Delphi identifies daily source and data dates; Delphi Live displays one selected paper account's
candidates, with account-preserving navigation. Existing promotion eligibility and trading rules remain.

Validation: isolated WPF Release build passed using SDK 10.0.401; **827 Core tests passed**. Fourteen
synthetic offline WPF renders exercised 1280×820/1600×1000 layouts, unavailable and review states,
compiled main-account markup, five complete pairs/ten accounts, ordinary-scope exclusion and disabled
fixed-pair promotion, with no binding errors. Screens were visually inspected using fixtures, not
operational records. The original build is in ignored `artifacts/workspace-ui-20260909/build/`; during
that implementation the agent did not restart the host or replace its output. No database/market/model/broker workflows ran there.
Existing compiler warnings remain; cached dependency-security advisories are recorded separately,
including `System.Drawing.Common 5.0.0` critical severity. No fresh dependency audit, complete solution
or SSDT build was performed in this UI turn. See [ADR-0068](adr/0068-main-account-trading-overview.md)
and the [implementation record](concepts/trading-workspace-redesign-draft.md).

**Fixed-gate comparisons implemented and active (2026-09-09, 13:58 Toronto):** five fresh matched pairs
cover Continuation Top 3/Top 5, Breakout Top 3/Top 5 and Delphi Live. All ten accounts started with
independent CAD 1,000 cash and empty holdings/orders. Each pair has lifetime-fixed Normal and Portfolio
rules only profiles, identical pinned portfolio rules, and the same published selection scope. Existing
accounts continue separately. Manual migration 033, a verified fresh backup and hash-matched secondary
copy, rollback rehearsal and activation retry are complete. All 17 protected table hashes matched after
migration; existing-account/source hashes also matched after activation. Four invalid identity/profile/
settings writes were rejected transactionally.

Batch `849334ad-1f09-4059-863e-159474d29d0f` has a common 13:15 funded start and adopted corrected run
`9ab04fbc-e71d-4021-ba1f-20bfd5f23984` at 13:21:33 with a common 13:25 fresh-evidence boundary.
The source contains 41 published stocks and 43 daily baselines including retained observation symbols.
Normal admits zero; experimental Shadow accounts track exactly three/five published ranks, and
experimental Live admits all 41. At 13:32, the first full collection cycle settled 43 slots with 39 usable
bars and four missing observations. Both Live ledgers processed that cycle and Shadow evaluation advanced.
No comparison fill had occurred at that checkpoint. Today's coverage is partial; portfolio entry,
data/conflict checks, capital/risk guards, protective exits and the ordinary buying cutoff still apply.

Final Release WPF PID 12424 started at 13:56 with the source-boundary fix and fixed-profile comparison
view. An intermittent SQL 51246 interruption was traced to Dapper rounding evaluation timestamps into
the future; explicit `datetime2` binding fixes it while preserving the causal guard. At 13:58, both Live
comparison ledgers had processed the 13:55 checkpoint at revision 12, with one healthy lease. All 43
collection slots settled; 35 were usable and eight lacked completed bars. No Live comparison fills yet.
The four experimental Shadow accounts now hold five positions in total (1/2/1/1 across Breakout Top 3,
Breakout Top 5, Continuation Top 3 and Continuation Top 5); all four matched Normal controls remain cash-only.
The full Core suite passes 819 tests, and all 352 Delphi Live tests pass after the precision correction;
WPF Release and SSDT builds pass. A read-only WPF presentation
check verified ten distinct rows, five pairs, source explanations and disabled profile/promotion actions.
Native screen capture was unavailable, so pixel-level visual verification remains unperformed. Existing
compiler and cached dependency-security warnings remain; no fresh package audit or complete solution
build was performed. No model training, broker action, commit or new nightly ingestion was run here.
Operational evidence is in ignored `artifacts/gate-comparisons-20260909/`. See
[ADR-0067](adr/0067-fixed-gate-paper-comparison-pairs.md) and the
[design handoff](concepts/paper-source-refresh-and-gate-profiles-draft.md).

**Automatic daily-source adoption deployed (2026-09-09, 11:10 Toronto):** first milestone of the
operator's requested recovery/gate-comparison work is complete. Migration 032 and the updated Release
WPF host (PID 29128, started 11:04) adopt newer valid official publications during the session.
All four Shadow sessions and operational Delphi Live adopted corrected run
`9ab04fbc-e71d-4021-ba1f-20bfd5f23984` at 11:04:58–59, with an 11:05 fresh-evidence boundary.
Repeated refreshes retain exactly one source revision each. Shadow is Active; Live's original
`NoValidDelphiRun` freeze remains as history alongside its operational `CurrentOfficialRun` source.
Nine Live daily baselines were saved. The UI source banner and database identities agree.

The first resumed 11:05 Live collection completed at 11:07:29: all nine slots settled, eight usable
receipts and one `NoCompletedBar`; ledger revision 59 references that completed cycle. One current
lease is healthy. Six Shadow positions remain and checked fill-timing violations are zero. Original
frozen candidate/lens/baseline and capital evidence hashes match. Late host coverage remains excluded
from clean research. No new Live positions or fills were created, and daily gates still admit zero
candidates. Separate normal/portfolio-rules-only comparisons are the next milestone; no new gate
profile or comparison account was activated here.

A fresh checksum backup and its secondary copy match; cloud synchronization is unverified. Rollback
rehearsal and migration application preserved all 17 protected table hashes. Initial adoption was
safely rejected because Dapper rounded publication timestamps; explicit `datetime2` parameters fixed
this before successful adoption. WPF now awaits lease release before closing. The full Core suite
passed 806 tests, and the final focused suite passed 371; Release WPF and SSDT builds pass. Existing
compiler and cached dependency-security warnings remain, including Drawing.Common critical severity.
No fresh package audit, full solution build, commit, training or broker operation was performed.
See [ADR-0066](adr/0066-operational-paper-daily-source-adoption.md) and ignored
`artifacts/source-adoption-20260909/README.md`. Earlier cutoff-block descriptions below are historical.

**September 9 nightly recovery and WPF readiness (10:07 Toronto):** the scheduled 00:30 run built
successfully and refreshed September 8 data, but Hermes reported ARX/SES sector-detail failures and the
guard correctly stopped before Delphi/Athena. The operator authorized recovery. A first morning pass
completed Hermes/Delphi; Athena saved 284 valid outcomes and five invalid older-strategy outcomes with
`MissingSymbolSession`, returning Attention. Its current strategy still has no matured scoring cohorts.
Inspection then found unfinished September 9 US-index bars in the daily diagnostic context. Hermes now
bounds requests before the local run date and refreshes its last stored observation; Delphi bounds US
context and the saved report to the canonical market-data date. All 36 focused tests passed.

The corrected full pipeline succeeded at 09:59. Official run `9ab04fbc-e71d-4021-ba1f-20bfd5f23984`
is Valid on `v3.4-observed-benchmarks`, uses September 8 data, has 207 candidates and 414 lens facts,
and publishes 25 Hold/Breadth failures per lens. All four model-file hashes match, and all candidate
observations match stored bars. Athena's second pass wrote zero outcomes, verifying idempotence; earlier
invalid evidence remains intact. Both recoveries produced verified checksum backups with hash-matched
local OneDrive copies; cloud synchronization is unverified. ARX/SES remain unavailable and excluded,
four malformed ETF daily bars are outside the evaluated universe, and leadership movers remain 0/12.

WPF started at 09:54 (PID 25996) and is responding. Four Shadow sessions and Delphi Live have
`NoValidDelphiRun` because neither recovery met the 09:30 cutoff. Yesterday's overrides are date-specific.
Existing portfolio monitoring progresses: two nine-slot Live cycles each settled with nine usable receipts
through the 10:00 bar endpoint and matching ledger cycle identity. Two carried Shadow exits filled with
zero timing violations; fourteen holdings and eight pending protective sells remain. A tracked holding's
15-minute request is rejected with
`CompletedBarConflict`; automatic evaluation for that holding is blocked pending usable evidence.
Thus today is suitable for continued monitoring with disclosed gaps, not a normal new-entry Live trial.
No entry gates, model assignments or engineering overrides were changed. The running Debug host predates
the daily-only correction; corrected Release WPF output is ready for the next launch. Its monitor code
is unchanged, and corrected saved daily results are available through Delphi's Refresh control.
Release WPF and focused CLI builds pass. Existing compiler warnings and cached dependency-security
advisories remain (including Drawing.Common critical severity); no fresh package audit or full
solution/SSDT build was run. No migration, model training, broker operation or commit was performed.
Evidence and the detailed handoff are under ignored `artifacts/nightly-readiness-20260909/`.

**Delphi Live engineering activation applied (2026-09-08, 14:50 Toronto):** the operator authorized
today's Live test with CAD 1,000 simulated capital. Migration 031 creates one Operational Champion,
admits the 38-stock union of today's published rows, preserves all fifty original Hold/Breadth lens
facts, and freezes 39 valid daily baselines. Explicit trial references and `OperatorEngineeringTest`
distinguish this exception; real timestamps and the mandatory late-start gap remain. Normal source
selection, risk controls and causal execution are unchanged. Initial collection exposed a second
dossier-eligibility guard: evaluations saved, but portfolio processing failed on the retained Breadth
failures. A narrowly scoped Core correction admits and labels only this trial's exact source/session;
all 344 focused Delphi Live tests pass. The rebuilt app started at 15:11 (PID 13920); the old lease
expired normally at 15:24:56 and the replacement acquired fence 6 at 15:25. No lease was forced.
At 15:36, two complete portfolio cycles are verified through the 15:30 bar endpoint: all 39 slots
settled per cycle, 36 usable receipts and three missing completed bars, 38 saved candidate states,
revision 8, three valuation marks and matching completed-cycle IDs. CAD 1,000 cash remains intact,
with no Live actions/fills/positions. Confirmation restarted at 15:25 and may not mature before the
15:45 buying cutoff. The late start and runtime gaps permanently exclude today from clean research.
Shadow remains active with fourteen holdings, two ordinary reentry orders pending and zero checked
fill timing violations; collection advanced through 15:32. The five-minute read-only watch covers both
systems through closing persistence, ending at 16:15 today.
The helper and SQL/SSDT Release builds pass. Rollback rehearsal matched fourteen protected tables and
rejected five invalid states; application matched the same protected tables. Fresh checksum backup
and hash-matched secondary copy verified; cloud sync is unverified. Existing compiler warnings remain;
separately, the test build reports cached dependency-security advisories, including Drawing.Common
critical severity. No fresh package audit or full solution build was performed. See [ADR-0065](adr/0065-one-session-live-engineering-activation.md).

**Dashboard dark surfaces corrected and operator verified (2026-09-08):**
the first correction covered the tracked Trading monitor and Portfolio activity/result panels but missed
Recommendations' account-state and approval-history expanders. A shared dashboard expander style now
provides dark surfaces; selected-tab label colors are confined to headers rather than inherited by page
contents. Recommendations explicitly declares its page colors. Existing Delphi Live expander styles
already provide their own dark surfaces. Trading text fields and disabled buttons remain readable.
Synthetic offscreen renders now include Recommendations in its actual dashboard parent, both expanded
panels, tracked Trading and Portfolios at 1600×1000 and 1280×820. Dark-surface/contrast checks and zero
binding errors pass. The focused Debug WPF build passes with two existing compiler warnings. Updated
output was isolated under `artifacts/dark-ui-preview/wpf-build/`. The operator restarted again at 12:20
(PID 41948), confirmed the remaining colors look good, and valid collection resumed.
The operator restarted the first update at 12:01 (PID 44516). Valid collection resumed by 12:02; all
four Shadow sessions, sixteen candidates and fifteen open positions survived. The pending buy was
cancelled with `StaleBuyRequiresRequalification`, as the restart policy requires. Monitoring continues.

**September 8 Shadow engineering override applied (11:02 Toronto):** after the operator requested
forcing today's Top 3/Top 5 to exercise the portfolios, reviewed data migration 030 adopted the recovered
09:52 daily run into all four existing Shadow sessions. Sixteen ranked candidate assignments are now
present, with four append-only override events and visible `[TEST 2026-09-08]` account-name suffixes.
The late-source and failed daily-eligibility restrictions are bypassed for this date only; the first
permitted five-minute bar starts at 11:05. Intraday momentum, data checks, cash/slot limits, risk holds,
protective exits and causal fills remain. Existing holdings/cash and official Hold evidence are preserved.

The rollback rehearsal and application matched all fourteen protected-table hashes, portfolio financial
state and schema identity. A fresh checksum-verified full backup has a hash-matched OneDrive folder copy;
cloud sync is unverified. The SQL/SSDT Release build passes without warnings/errors. No runtime code,
model/strategy assignment, second host, app restart, broker operation or Delphi Live activation occurred.
At 11:12 all sixteen candidates were evaluated and seven ordinary Initial buy orders became pending
(Continuation Top-3/Top-5: two/three; Breakout Top-3/Top-5: one/one), with no timing violations.
The remaining candidates have explicit price/evidence skips; later fill observation continues.
The raw portfolio charts include this intervention
and its carried effects; treat them as engineering evidence. Ordinary next-session selection resumes
automatically. See [ADR-0064](adr/0064-one-session-shadow-engineering-override.md); evidence is under ignored
`artifacts/shadow-engineering-20260908/`. The block described below was the pre-override state.

**September 8 market-open observation and manual Delphi recovery:** the 00:30 pipeline stopped at the
source-stability guard while development was in progress; its builds passed, but no operational stage ran.
After the operator requested a manual recovery, a separately logged Delphi-only invocation rebuilt current
Release source with matching before/after fingerprints and published at 09:52 Toronto. Run
`9491e421-ef3e-48b4-bcb7-389c42ec25e9` is Valid, uses `v3.4-observed-benchmarks` and September 4 data,
and records 209 candidates, 418 lens evaluations and 25 Hold entries per lens: no qualifying buys.
The build passed with 258 existing compiler warnings; no dependency advisory appeared in that build log,
and no new package audit or full solution/SSDT build was performed. The original failed nightly status
remains intact; this was not a successful rerun of the full Hermes/Delphi/Athena pipeline.

The operator-started Debug WPF app is responding. The 09:47 and 09:52 collection observations have
28 Valid persisted receipts across tracked-position and Shadow monitoring. All four Shadow sessions are
`NoValidDelphiRun`: their required same-date run did not exist by the 09:30 cutoff, and a later publication
does not refreeze them. Carried holdings remain monitored. Delphi Live and Trading strategy approval are
unactivated. A five-minute read-only watch runs through 16:15 today; full-session validation is pending.
No settings, model assignment, schema, app restart or broker operation was performed during recovery.
Hermes and Athena were not run; stored completed-session daily inputs were already current. Evidence is
under ignored `artifacts/manual-delphi-20260908/` and `artifacts/market-open-watch-20260908/`.
The proposed late-start recovery contract is recorded in [open questions](reviews/open-questions.md).

**Historical Practice trial completed and deployed (2026-09-08):** the operator authorized rollout after
commit `836c393` and explicitly accepted today's stock list/sector classifications with exploratory
labels. Stored daily inputs, four preserved models and four Shadow policies were pinned. All 20 sessions
published daily Delphi picks. TMX returned 44,792 bars in 964 successful requests; 5,336 requested slots
were missing and retained. The four CAD 1,000 accounts produced 255 simulated fills, ending at CAD 950.73,
957.01, 958.57 and 924.15 (Continuation Top-3/Top-5, Breakout Top-3/Top-5 respectively). Two positions remain
open; no pending orders or forced liquidation. Incomplete held valuations withhold drawdown estimates.

An independent audit passed all 7,940 frames, fill timing/source prices, cash/shares, marks and account
isolation. Two real-data runs have identical frames, summaries and full checkpoints. The deployed Release
WPF Practice window is open and responding; real playback previews have zero binding errors. Its first
launch exposed a null-StartupUri error, which was fixed and verified by relaunch. All 76 TraderDB table
counts and selected state hashes remain unchanged; no migration was needed. Release builds, all 792 Core
tests and the final 18 focused Practice tests pass. Existing compiler/package warnings remain; full
solution/SSDT validation was not repeated. Preparation/viewer fixes after the commit remain uncommitted.
The exact runner and private data/evidence are preserved under ignored `artifacts/practice-rollout-20260908-0117/`.
See [completed rollout](adr/0063-historical-practice-environment-direction.md#completed-trial-and-deployment--2026-09-08).

**Historical Practice implemented in source (2026-09-08):** the shared daily Delphi workflow now accepts
bounded historical inputs; the shared Shadow controller accepts isolated time/data/storage. A dedicated
Practice runner uses pinned datasets/models/settings, four independent CAD 1,000 accounts and every
reviewed session in the August 10–September 4 starter period, preserving gaps and carrying holdings/orders.
The WPF Practice tab plays saved frames with a permanent exploratory label and simulated Toronto time.
Separate explicit commands prepare stored daily inputs and bounded historical TMX batches. No database
schema change, acquisition, real historical trial, operational host, model training, assignment or commit
ran during implementation. Actual dataset preparation remains follow-up; incomplete datasets are allowed.
The final complete Core suite passed all 790 tests and focused CLI/WPF builds passed. Existing compiler
warnings and separate dependency advisories remain. Two offscreen synthetic previews have zero binding errors;
real SQL concurrency, automatic crash resume and Delphi Live are not implemented by this milestone.
See [contracts, validation and commands](concepts/historical-practice-implementation.md), [ADR-0063](adr/0063-historical-practice-environment-direction.md).

**Manual complete-strategy promotion installed, awaiting operator activation (2026-09-07):** Portfolios can prepare an
exact strategy/evidence/account review and record explicit human approval; Trading shows the approved
source, reports actual account state, evaluates Shadow or Live policies against Real holdings/cash, and
records separately reported actual fills. Approval and recommendation history are immutable and account
scoped. Switching back appends a new approval. Performance evidence is advisory by the operator's decision;
automated evidence enforcement is deferred to Wealthsimple live trading. Existing pause/empty and risk
controls remain. See [ADR-0062](adr/0062-complete-strategy-recommendation-promotion.md).

After the operator committed the implementation at `f72fca5` and authorized rollout, migration 029 was
applied with SQLCMD error stopping after a fresh checksum-verified full backup and a matching SHA-256
OneDrive copy. All 71 existing table counts and 16 populated selected state hashes match before and after
both migration and runtime checks. The five new tables are empty, their five immutable triggers are enabled,
and all nine foreign keys and seven checks are trusted. OneDrive cloud synchronization is unverified.

All 774 Core tests passed during implementation; the 54 focused promotion tests passed again during rollout.
Debug and Release WPF builds and the SQL/SSDT build pass; existing compiler warnings and separate dependency
advisories remain. Read-only installed repository checks prepared all four Shadow reviews, loaded account
and assignment history, and verified that an unassigned account requests no quotes and publishes no batch.
The reviewed calendar loads and covers the next regular session, September 8. Delphi Live has no initialized
paper ledger, so its installed review path could not be exercised. TFSA has existing reported holdings,
no current account-state report, and Trading buys are not paused; reconciliation and the existing paused/empty
requirements precede explicit approval. No operational host, market call, training, actual approval/assignment,
financial report/fill or broker operation was run. The new desktop outputs are ready for the next launch;
live source capacity and actual approval/publication transactions remain unexercised. See the
[rollout record](adr/0062-complete-strategy-recommendation-promotion.md#authorized-rollout--2026-09-07).

**Portfolios comparison workspace implemented in source (2026-09-07):** Portfolios now opens to Compare,
with Performance and Strategy review tabs sharing the selected account, period and scope. Saved paper
closing history supports descriptive aligned returns and observed closing declines, with explicit gaps;
Real/operator accounts remain references. Existing account tools are collapsed. Review shows the saved
then-current daily recommendation source and tracked exit assignment. ADR-0062 above adds complete
strategy approval and the supporting paper-account identity; version-specific returns remain unproven. All 720 core tests and the targeted WPF build pass;
offscreen synthetic previews verify navigation and binding behavior. Existing warnings remain. No database
operation, operational app, market call, assignment or account activation was run. See
[ADR-0061](adr/0061-portfolio-comparison-workspace.md) and [measurement/validation notes](concepts/portfolio-comparison.md).

**Pause / close / configure revision implemented in source (2026-09-07):** strategy editors, Save and
Assign now require a durable entry pause plus zero holdings/pending orders in the affected family. Daily
shared models/gates require all dependent families paused and empty. Operational pages have Pause/Resume
and per-holding Sell/Record sale controls with immutable request/audit history; protective exits continue.
Related rule tabs retain one draft and save together, with behavioral descriptions and system banners.
Resume uses the assigned strategy, retains risk holds and rejects incompatible frozen daily picks.
Core tests pass (713); WPF and SQL/SSDT builds pass. Migration 028 was applied after a fresh verified backup
and matching OneDrive copy. All 68 existing table counts and 16 selected state hashes match the baseline;
trusted constraints, immutable triggers and runtime read-only eligibility checks pass. The new Release
app is built for the next launch; no operational host was started for validation. Cloud sync is unverified.
No holding has been sold and no actual pause or strategy assignment was changed during implementation.
The [agreed workflow](concepts/global-settings-design-draft.md) supersedes the immediate-with-holdings
behavior of the first release below. Existing compiler warnings and dependency advisories remain.

**Central Settings rolled out (2026-09-07):** the main tab bar now has one
Settings destination, with links from operational pages. Save Version and Assign are separate for daily
Delphi, Delphi Live, System Shadow and tracked-position exit policies. Assignment replaces the whole
target's rules, supersedes pending internal actions and begins reevaluation while preserving financial
facts and history. Supported controls, state conversion, research isolation and failure behavior are
documented in the [implementation review](reviews/central-settings-implementation-20260907.md).
Core tests (699), WPF/Core builds and the SQL/SSDT project pass; five isolated layouts render without
binding errors. Existing compiler warnings and dependency advisories remain. Migration 027 adds the
version/assignment audit storage and the explicit Live assignment revision authority. With explicit operator
authorization, a fresh checksum-verified backup was copied to the approved OneDrive folder with matching
SHA-256, migration 027 was applied, and the updated Release WPF app was launched. All 66 preexisting table
counts and the account/order/ledger/strategy state hashes matched across the migration; zero settings
versions or assignments were seeded. The installed catalog exposes five targets. No active strategy or
model assignment was changed. OneDrive cloud synchronization has not been independently verified.
The [system map](concepts/settings-system-map.md) explains configuration scope and a proposed narrower
research-comparison restart; that refinement is not implemented. New work remains uncommitted.

The following daily-only milestone predates and is superseded by central navigation and separate
save/assign behavior above.

**Delphi Settings implemented in source (2026-09-06):** the daily Delphi workspace now has a Settings tab
for selecting a preserved four-model set with its associated strategy, reviewing model identities and
editing eight daily decision thresholds. Explicit Apply either selects an existing version or creates a
new version for changed gates, preserving prior evidence and input-policy dispatch. Desktop and CLI
evaluations share a SQL selection lock through publication; the settings writer also honors the nightly
file lock and appends the existing immutable selection event. All 690 Core tests and affected WPF/CLI
builds pass; isolated synthetic previews have no binding errors. The read-only installed catalog check
found seven versions, three selectable preserved assignments, exactly one active version and matching
hashes for all four active model files. Compiler warnings and dependency-security advisories remain.
No actual selection, threshold update,
database migration, application run or market call was made during implementation. See
[ADR-0059](adr/0059-delphi-model-and-threshold-settings.md). Earlier work was first checkpointed at
`6868299`; settings changes are subsequent uncommitted work.

**First official corrected Delphi run completed (2026-09-06, 22:43 Eastern):** the operator authorized
the standalone daily run. `v3.4-observed-benchmarks` evaluated 209 stocks using September 4 market data
and saved run `7cd0ab32-07c1-4efb-b5c3-808f99b75887` with `audit=Valid`. Its four captured model hashes,
dated input contract, observed XIU/SPY policy and saved presentation were verified in SQL. It appended
418 lens evaluations and published 25 Continuation plus 25 Breakout entries, all **Hold**: the result is
**no qualifying trade**. Seven stocks were excluded for required-input problems. Leadership movers have
0/12 contiguous observations, so the affected signals remain explicitly unavailable; Valid here verifies
provenance and does not claim complete indicator coverage or predictive performance. Thirty-eight prior
evidence/account table snapshots retain matching counts/checksums after excluding this new cohort and
publication. The affected Release build passed. No paper positions, broker orders, Athena evaluation or
full nightly pipeline was started. Normal scheduled completion remains to be observed.
[Run verification](reviews/model-input-cutover-20260906.md#first-authorized-official-v34-run--2026-09-06-2243-eastern).

**Nightly schedule and benchmark repair completed (2026-09-06):** the task had been configured for
Monday–Friday at 00:30, so Friday's successful invocation collected Thursday and there was no Saturday
invocation to collect Friday. The task now covers all seven days, with other settings preserved; the
read-only 07:00 supervisor also covers weekends. The authorized Hermes refresh added 481 daily bars
with zero symbol-download failures. XIU, 320 of 321 stocks and supporting indicators now reach September 4.
SPY collection added 503 daily observations through that date; both refreshes completed verified backups.

The active daily strategy is now `v3.4-observed-benchmarks`
(`48864449-92F4-4E33-99CF-40039210786E`, [ADR-0058](adr/0058-daily-ingestion-and-observed-benchmark-confirmation.md)).
It reuses the exact four v3.3 models and thresholds, requires observed/current XIU and SPY inputs, and
uses the reviewed calendar to reject a stale shared XIU endpoint before evaluation. Missing confirmation
can no longer become positive under this policy. One stale stock and six incomplete/invalid 55-session
stock windows remain subject to the accepted exclusions. All 676 Core tests and the full Release solution
build (including SQL/SSDT) pass. Actual active bootstrap and benchmark validation pass; forward/rollback
rehearsals passed. All 232 model rows and 38 historical evidence tables are unchanged. Compiler warnings
and dependency-security advisories remain. No Delphi/Athena run, paper monitor, broker action, commit,
push or deployment occurred. [Repair results](reviews/model-input-cutover-20260906.md).

**Daily model-input cutover completed (2026-09-06):** `v3.3-dated-profit-inputs`
(`9EB26788-76C3-4064-9595-2373ECF6C26A`) became the sole active daily strategy at the initial cutover,
before the later v3.4 benchmark repair above. The operator authorized model
inspection, replacement training if compatibility could not be proven, new registration and the switch.
Older files loaded but lacked proof of their training-input contract, so four separate candidates were
trained and selected under [ADR-0056](adr/0056-dated-profit-model-input-contract.md) and
[ADR-0057](adr/0057-preserved-model-sets-and-explicit-selection.md). Delphi's actual bootstrap loaded all
four recorded hashes with the dated contract. The nightly CLI and WPF share this stored assignment; no
per-host binding flag is needed. The full nightly pipeline was not launched as validation.

Hercules now creates candidates by default, preserves prior files, records data/source evidence and never
automatically selects replacements. Existing strategy and signal thresholds are unchanged. The previous
set remains available for reviewed rollback; both forward and rollback transitions passed rolled-back SQL
rehearsals. All 228 earlier model registry rows are unchanged, and 38 historical tables retained matching
counts/checksums. Migration 026 was backed up, built with SSDT and applied without altering the 64 existing
tables' row counts. All 655 Core tests pass and the complete Release solution builds, including TraderDB.
Compiler and dependency warnings remain. No market collection, new recommendation cohort, paper-monitor
activation, broker operation, commit or deployment was performed. See the [cutover review](reviews/model-input-cutover-20260906.md)
and [audit checklist](strategy-alignment-implementation-checklist.md). Next: policy/version dispatch and
historical outcome-definition contracts; manual recommendation promotion is now implemented under ADR-0062; automated evidence enforcement remains deferred.

**Accepted direction (2026-09-06):** independent paper accounts evaluate complete strategies; comparable
evidence and human review select the strategy for real-account recommendations, with separately approved
Wealthsimple execution later. [ADR-0055](adr/0055-independent-strategies-to-approved-live-execution.md)
records that direction. The [repository alignment review](reviews/strategy-direction-alignment-20260906.md)
finds that account isolation and Delphi Live's internal policy-promotion framework support it, but a common
strategy identity/comparison/promotion route and broker execution were not yet implemented at that audit.
ADR-0062 now implements manual recommendation approval/routing; broker execution remains future work. The audit
identified model activation, training/inference feature parity and portfolio measurement defects; the
stabilization work and later authorized model cutover are recorded above and in the audit checklist.
The audit itself was static, with no operational validation or account activation. The build/test results
below describe the earlier milestones individually.

Delphi Live V1 is implemented in source against the frozen [concept](concepts/delphi-live.md) and
[ADR-0053](adr/0053-delphi-live-v1.md). Its independent Core engine, durable five-minute collection,
paper portfolios, research protocol and inactive WPF surface are tracked in the
[implementation checklist](delphi-live-implementation-checklist.md). All 581 Core tests passed and the
complete Release solution built successfully, including the SQL database project. Migrations 022–025 were
separately authorized, backed up, applied and verified on 2026-09-06. All 27 new tables are installed with
exactly one inactive policy definition and no operational rows; the 37 existing tables retained matching
row counts, aggregate checksums and schema fingerprints. `DBCC CHECKDB` reported no errors. The
[reviewed TSX calendar](../Operations/Calendars/README.md) is installed for the Windows user, covering
2026-01-01 through 2026-12-23, with eight focused calendar tests passing. It stops before the unsupported
December 24 short session. A separately authorized [closed-market historical source trial](reviews/delphi-live-closed-market-trial-20260906.md)
passed on 2026-09-06: all nine sampled XIU/RY/ENB five-minute bars were retrieved, with no database or
portfolio actions. Live freshness/durability, full-capacity shakedown and simulation activation remain
outstanding; the historical trial counts as zero clean engineering cohorts.

The [daily-picks preview and Friday replay](adr/0054-delphi-live-preview-and-historical-replay.md)
are now implemented. The simpler WPF view shows the latest published picks before activation, including
blocked daily rows, and loads a separate historical account from local artifacts. The authorized September 4
replay produced 78 checkpoints and six estimated fills, with missing source intervals reported explicitly;
see the [replay review](reviews/delphi-live-friday-replay-20260906.md). All 592 Core tests and targeted
Sandbox/WPF builds pass. No operational session or portfolio was activated by the replay.

Portfolios now includes a SELECT-only Delphi Live account projection: queued, active and ended accounts,
an explicit inactive placeholder, and per-account holdings/actions/fills. Daily Shadow commands are
guarded from Delphi Live selections. All 600 Core tests pass, including eight account-valuation and
status regressions; isolated WPF checks cover selection and command separation. This display change
does not activate an account or alter either execution engine.

TraderVI is an advisory-mode TSX momentum-rotation system with an immutable paper-calibration ledger and deterministic Continuation/Breakout scorecards. ADR-0040's delayed-intraday outcome is complete in source, including a continuity guard: Athena rejects proven missing 15-minute bars/sessions, receipt-order conflicts, and missing exact symbol/XIU fill bars instead of silently replaying across them; an unproven end-of-data tail remains pending. Migration 015 is applied and its fifth definition is active. Athena has produced the first 112 valid three-session marks and 112 valid excursion outcomes; the longer prediction and delayed-intraday definitions still have zero outcomes. Operational Real exits remain manually reported Wealthsimple fills and are never substituted into official outcomes. The Trading tab keeps open Delphi-linked positions and unlinked operator-reported Real holdings in Tracked positions while retaining closed lifecycles in Trade history. There is no broker integration.

ADR-0042 now defines and implements the post-ADR-0041 strategy/code boundary in source. Comparative
reports and export-schema-v2 artifacts are scoped to one explicitly identified active strategy, while
earlier runs remain immutable and are counted as excluded. Manual migration 016 was backed up,
authorized, applied, and verified on 2026-09-02. `v3.1-rs-date-aligned` became the sole active strategy at
that boundary, with zero included official runs and seven earlier-identity runs excluded.

ADR-0043 now preserves leadership-source missingness in source. Movers-derived breadth requires an exact
same-date XIU anchor and a complete 50-symbol observation, while scoring uses a contiguous canonical XIU
session suffix and reports unavailable inputs as neutral/no-data rather than zero or falling breadth. The
fixed successor is `v3.2-leadership-missingness` at behavioral commit `fad4b96`. Manual migration 017 is
applied and verified: v3.2 became the sole active strategy at that boundary, the nullable leadership contract is trusted, and
all protected evidence counts and references remain unchanged. The first post-migration Hermes ingestion
completed on 2026-09-02: its 2026-09-01 observation stored 11 new highs, 18 new lows, and 467 eligible
issues while preserving unavailable movers breadth and same-session price anchors as null. Athena does not
collect or repair leadership inputs. The first deliberate official v3.2 Delphi run is valid for recommendation
date 2026-09-02 using market data through 2026-09-01; it added 219 candidates, 438 lens rows, and 50
published picks. The official ledger now contains 8 runs across 6 distinct market-data cohorts and 1,729
candidates.

The repository retains a coherent June/August feature foundation plus the September correctness and
identity stabilization sequence. It includes multi-lens ranking, Granville indicators, relative-strength
ranking, ghost/real trade logging, historical sector data, per-symbol On-Balance Volume (OBV), market-wide
Climax (CLX) reporting, and read-only desktop documentation and scorecard surfaces.

ADR-0046 adds the unattended operations boundary: a Windows task builds the current source and then runs
the resulting hash-verified Release artifacts for Hermes, Delphi, and Athena at 00:30 Toronto/Eastern time
every calendar day after the ADR-0058 correction, with source-stability verification, exclusive locking, same-date suppression, stage timeouts,
atomic status, and no automatic retry. A separate Codex heartbeat
reads only that durable status and stays quiet unless a run is missing, stale, long-running, failed, or
needs attention. The pipeline does not include Hercules, migrations, WPF monitoring, Oracle, Sandbox, or
broker activity. The Windows task and weekday 07:00 Codex heartbeat were installed and verified active on
2026-09-02. The first scheduled pipeline completed on 2026-09-04 with Attention rather than failure:
Hermes and Delphi completed, while Athena reported six invalid delayed-intraday outcomes.

ADR-0051 adds System Shadow V1 as a separate, capital-constrained paper-execution layer over frozen daily
Delphi recommendations. Four independent virtual portfolios compare Continuation and Breakout Top-3 and
Top-5 baskets using causal five-minute fills, whole-share equal slots, provisional loss/profit exits, and
daily/drawdown guards. The WPF Portfolios workspace keeps Real, operator-selected Ghost, and System results
visibly separate. Migration 019 was backed up, manually applied, and verified on 2026-09-04; the new ledger
was initially empty and off, then the operator activated the first same-session generation with manually
entered TFSA capital. It has no broker connection and remains entirely separate from Athena's official
calibration evidence.

The first live Shadow session exposed a nullable SQL `BIT` projection defect: market evidence kept arriving,
but the first pending fill stopped the decision phase because `COALESCE(DailyLossGuardActive, 0)` was returned
as an integer and read as a Boolean. The fill query now casts the expression back to `BIT`, with a regression
contract covering the database-reader boundary. The three already-persisted pending orders were preserved;
the repair did not invent a historical fill or alter the live ledger.

The completed 2026-09-04 Shadow session is retained as an engineering shakedown record, not clean strategy
performance evidence. All four sessions closed and reconciled, but one CRDL fifteen-minute bar was consumed
twice: it first armed a trailing stop, then its earlier low was tested against that newly armed stop on the
next poll. A software interruption also left MFC and CRDL buys pending for roughly two and a half hours; the
restart filled them without current requalification. Source now persists the last consumed fifteen-minute
bar identity and treats repeat or older bars as idempotent. Pending buys are valid only for their exact
immediate fill bar; a restart or later bar cancels the stale order so current evidence must create a new
one. Migration 021 is additive and deliberately leaves existing bar identities null, preserving the ledger
without inventing history. It was manually applied and verified on 2026-09-04 after a fresh checksum backup
and hash-matched OneDrive copy. Postflight preserved every Shadow row count and the portfolio cash/high-water,
position share/cost/P&L, and order-status totals exactly; the schema check now reports ready.

ADR-0052 separates source provenance from evidence quality. Delphi continues to store `Clean`, `Dirty`, or
`Unknown`, but tree state alone no longer degrades an otherwise complete run; missing code or model identity
still makes it invalid. Guarded migration 020 preserved today's `Dirty` marker while changing only official
run `463416D8-8229-4C51-B255-107F96DF21D4` to `Valid`, then removed the empty initial Shadow generation so
the operator can start it again against today's 25 Continuation and 25 Breakout picks. Delphi was not rerun.

## Verified development baseline

- Delphi Live validation and final integration status are recorded in its
  [phase checklist](delphi-live-implementation-checklist.md); the earlier operational baseline below
  remains a historical record and does not establish Delphi Live readiness.

- Active branch: `master`, with upstream `origin/master` configured.
- Nightly operations: Windows task `TraderVI Nightly` is Ready for daily 00:30 execution under the
  signed-in user, with wake/start-when-available enabled and no automatic retry. Codex automation
  `TraderVI Nightly Watch` is active for daily 07:00 read-only supervision, including weekends.
- SDK: .NET 10 (`10.0.400` verified on 2026-09-02).
- Complete Release solution build: successful after the Shadow execution-causality fixes using Visual Studio
  2026 Insiders MSBuild 18.10 and SSDT; `TraderDB.dacpac` was produced only as a build artifact and was not
  deployed on 2026-09-04.
- Core tests: 266 passed, 0 failed, 0 skipped after the duplicate fifteen-minute-bar and stale pending-buy
  regressions were added on 2026-09-04. The test project is explicitly marked as a test project so .NET 10
  discovery cannot silently report success with zero executed tests.
- Migration 021 parses with the SQL Server 2019 (`TSql150`) offline parser with zero errors. It was manually
  applied and verified on 2026-09-04. The 39.34 MB checksum backup and its approved OneDrive copy are both
  41,237,504 bytes with SHA-256 `DC6826BC176B7787DD89D476640DE1FAD66D29711981E1D1119DF5698B37B265`.
  OneDrive cloud-sync completion remains user-observed.
- Migration 017 parses with the SQL Server 2019 (`TSql150`) offline parser with zero errors. The final canonical SQL project and complete Release solution both build successfully after the strengthened identity constraint; neither build deployed the DACPAC.
- Known dependency advisories remain; see build output before updating packages.
- Local database engine: SQL Server 2019 Developer RTM (`15.0.2000.5`); the project now targets `Sql150` and blocks database deployment.
- Database recovery: `TraderDB` uses SIMPLE recovery with page checksums. `DBCC CHECKDB` completed without errors on 2026-08-22.
- Backup baseline: a 31.00 MB compressed checksum full backup completed and passed `RESTORE VERIFYONLY` on 2026-08-22. Its staging and OneDrive copies have matching SHA-256 hashes.
- Automatic backup validation: the first full post-Hermes path completed on 2026-08-22; both 31.09 MB copies independently matched SHA-256 `48B655E9BC1D402E85CA6F8698F548115FC074F7ED0B74AD40C9699E61482CB6`.
- Calibration evidence backup: the final post-run 32.51 MB checksum backup and approved OneDrive copy matched SHA-256 `62CB244339235D555830CD93B139AD19182B160B4B4C7CBF9429B5A52CCB08BB` on 2026-08-23. Cloud-sync completion remains user-observed.
- Intraday-ledger backup: the pre-migration checksum backup and approved OneDrive copy matched SHA-256 `CBDDB1E31877CA36E7B798867B5AC924DE0C9F09373F382191F30AE815C4A5B7` on 2026-08-26; migration 012 was then applied and verified.
- Outcome-definition backup: the pre-migration 34.98 MB checksum backup and approved OneDrive copy matched SHA-256 `A01FFCECAD236C967D8BA68AA4DCC8387BFB49DFA9C5B22F533A69C995D48F27` on 2026-08-28; migration 014 was then applied and verified without creating outcomes.
- Strategy-identity backup: `TraderDB_FULL_20260902_002015_223.bak` passed `RESTORE VERIFYONLY WITH CHECKSUM`; its 37,508,096-byte staging and OneDrive copies matched SHA-256 `33C5A08493BE4A2941341CC22EFECAB773BD854AA908C289DB6D6F5E15573EFF`, and the operator confirmed synchronization before migration 016.
- Leadership-missingness backup: `TraderDB_FULL_20260902_012154_689.bak` passed `RESTORE VERIFYONLY WITH CHECKSUM`; its 37,497,344-byte staging and OneDrive copies matched SHA-256 `D74FF2F3F3B18AED0C8C72BCCB99D6214497B7BFA3738B6E9420B7FA5EACF658`, and the operator confirmed synchronization before migration 017.
- First post-migration Hermes backup: `TraderDB_FULL_20260902_013547_367.bak` passed Hermes' SQL checksum verification; its 37,552,640-byte staging and OneDrive copies independently matched SHA-256 `B4C7126D851B20704C37B3A8D4EE43497AFE9AD3A1B8B9E20F779F3EB51AE625`. OneDrive cloud-sync completion remains user-observed.
- System Shadow backup: `TraderDB_FULL_20260904_100139_426.bak` passed `RESTORE VERIFYONLY WITH CHECKSUM`; its 39,577,600-byte staging and approved backup copies matched SHA-256 `29C87EAFAE76E870BF0D8CB3F73C28A059562FCB99085C22F21209769204694C` before migration 019 was applied.
- Delphi/Shadow correction backup: `TraderDB_FULL_20260904_104732_010.bak` passed `RESTORE VERIFYONLY WITH CHECKSUM`; its 40,429,056-byte staging and approved OneDrive copies matched SHA-256 `06FE957D08012D9FC07ECABF8F4D5F4B76AE03A2493CF2A4001E57D9FDE5BBE7` before migration 020 was applied.
- A/D integrity: the incremental lookback double-counting defect was fixed, and all 262 stored rows were repaired and re-audited with zero plurality, cumulative, or step mismatches. The final 2026-08-21 cumulative is `7,307`.

## Programs and responsibility

| Program | Current responsibility | Side effects |
|---|---|---|
| Hermes | Daily OHLCV ingestion and derived market-data maintenance; verified post-success database backup | External TMX/Yahoo reads; writes multiple SQL tables and a local/OneDrive backup pair |
| Hercules (`ML.Train`) | Trains enabled profit models and records experiments/models | CPU-intensive training; writes model artifacts and SQL registry rows |
| Delphi | Evaluates the universe through Continuation and Breakout lenses and emits reports | Reads models/data; rewrites daily picks, dossiers, narratives, and Granville logs for the evaluation date |
| TraderVI | Manual ghost CLI plus shared durable paper monitor | Writes simulated positions, trade logs, and intraday evidence; does not place live orders |
| TraderVI.WPF | Tabbed desktop shell for Trading, Portfolios, Data Audit, Delphi, official Scorecards, and Project Docs | Trading refreshes SQL history, polls TMX during the regular monitor window, auto-closes operator Ghost positions only, and records confirmed operator-reported Real fills after migration 013. Portfolios hosts the explicitly started System Shadow V1 paper controller and its durable audit ledger. Scorecards, Data Audit, saved Delphi sessions, and Project Docs are read-only; confirmed official Delphi runs have their documented SQL effects; no broker integration |
| Oracle | Optional LLM narration over deterministic decision dossiers | May call a configured LLM service and write narrative records |
| DataAudit | Read-only full-local-universe classification, freshness, mapping, and bar-integrity diagnostics | Local SQL reads only; no external calls or writes |
| Sandbox | Manually selected probes for reconnaissance, calibration, and controlled backfills | Probe-specific; some call external services or mutate SQL |
| Nightly runner | Builds current source, orders the resulting Hermes, Delphi, and Athena artifacts, and records durable local status for supervision | Inherits only those three programs' documented effects; never restores dependencies, retries automatically, or places broker orders |

## Decision engine

### Active trained profit models

The code registry currently enables four profit models:

1. `BinaryUp10` — upside-tail probability and direction input.
2. `BinaryDown10` — downside-tail probability and veto/penalty input.
3. `VolExpansionRelative10` — volatility-expansion confirmation.
4. `BreakoutEnhanced` — breakout/setup probability.

`RelStrengthCont10_2pct` is retained in code but disabled. The active v3.4 strategy selects exactly four
reviewed replacements through its immutable stored assignment, independently of global SQL enable flags.
Historical `ModelRegistry` rows remain unchanged. Only unbound legacy strategies use the global enabled
rows filtered by the code's currently allowed tasks.

### Active deterministic pattern signals

- `Trend10`
- `Trend30`
- `MaCrossover`

These are rule-based detectors evaluated directly in code, not trained ML models.

### Ranking lenses

- **Continuation** is the executed lens. It requires trend confirmation and ranks primarily by live relative strength plus the OBV tilt.
- **Breakout** is journaled as a comparison lens. It retains the breakout setup gate and ranks by `DirectionEdge + relative strength + OBV tilt`.
- Delphi now scores every symbol once and applies both lens gate/rank definitions to those exact shared facts. Profit-model results are also reused for composite calculation and reporting instead of invoking each model twice inside one score.
- CLX is diagnostic-only in v1; it does not gate or rank candidates.

### Active Granville groups

- Plurality (#1–#4)
- Disparity (#5–#6)
- Leadership (#7–#10)
- Most Active / Features (#11–#14)
- Weighting (#15–#16)
- Genuity (#17–#20)
- Light Volume (#25–#28)

Dullness (#21–#22) is deferred by ADR-0005. Overdueness (#23–#24) and groups after #28 remain future work.

## Local data snapshot

Read-only aggregate inspection on 2026-08-19 showed:

| Dataset | Rows | Latest date |
|---|---:|---|
| `DailyBars` | 758,870 | 2026-08-18 |
| `AdvanceDeclineLine` | 259 | 2026-08-18 |
| `SectorIndices` | 18,315 | 2026-08-18 |
| `LeadershipData` | 106 | 2026-08-18 |
| `SymbolObv` | 61,271 | 2026-08-18 |
| `MarketClimax` | 1 | 2026-08-18 |
| `RelativeStrengthFeatures` | 0 | — |
| `UsIndexBars` | 5,161 | 2026-08-19 |
| `DailyPick` | 327 | 2026-06-06 |
| `GranvilleIndicatorLog` | 76 | 2026-06-06 |

Additional state:

- 386 stock-sector mappings are present.
- All six Ghost lifecycles are closed. The Delphi-linked Real lifecycle is also closed after an
  operator-reported all-shares broker exit on 2026-09-02. One historical Real holding without an
  original-pick link remains open. These operational records are not official Athena outcomes.
- 27 historical task types have enabled registry rows, with no duplicate enabled row within a task type.

Post-restart Delphi verification on 2026-08-22:

- 25 Continuation picks persisted with ranks 1–25.
- 25 Breakout picks persisted with ranks 1–25.
- 25 Continuation decision dossiers persisted with ranks 1–25.
- 11 Granville diagnostic rows persisted for the recommendation date.
- The 19 RS fallbacks were audited and corrected: 14 funds were reclassified as ETFs, BITF/GLXY/NGD/NVA were marked inactive after their TSX listings ended, and active GDI was mapped to Industrials (`^TTIN`). The guarded migration updated 18 symbol rows and inserted/updated one mapping without deleting price history.
- `PickDate`/Granville `EvalDate` remain the recommendation run date; reports separately identify the latest completed TSX session used as the market-data-as-of date.

First full-local-universe DataAudit run on 2026-08-22:

- 1,592 symbol rows inspected; 496 active (375 stocks and 121 ETFs).
- 10 errors: nine severely stale active symbols plus one empty active symbol key.
- 118 warnings: one lagging symbol, one missing active-stock map, 43 unmapped active stocks, 24 stale mappings, and 49 fund-like rows classified as stocks.
- No non-positive prices, inverted high/low ranges, open-outside-range rows, negative volumes, duplicate symbol/date bars, orphan bars, invalid sector prices, or referenced sector-index coverage failures were reported.

Full-local-universe reconciliation completed on 2026-08-22:

- Final audit: 1,591 symbol rows; 485 active (321 stocks and 164 ETFs); zero errors and zero warnings.
- Ten reviewed ended or suspended listings were marked inactive without deleting their price history: BLX, ECN, FOOD, GDI, KSI, OLA, QIPT, SOY, URC, and WNDR.
- The single invalid empty-symbol metadata row was deleted only after a dependency scan found no related data and the exact deletion was authorized.
- Eighty-eight additional fund rows were reclassified from Stock to ETF; active status, leverage/inverse flags, mappings, and price history were preserved.
- The apparent active-stock mapping and stale-mapping warnings were classification errors, so no artificial sector mappings were created for those ETFs.

Shared-workflow DataAudit verification on 2026-08-26:

- Both the retained CLI and the new WPF tab use `MarketDataAuditWorkflow`.
- The read-only CLI run inspected 1,591 symbols and reported zero errors plus one warning: active RCTR's latest bar was 2026-08-21, two XIU sessions behind the 2026-08-25 market-data date.
- No database correction or external market call was performed.

Shared-workflow Delphi tab implementation on 2026-08-26:

- `DelphiWorkflow` now owns the evaluation once; the retained CLI and WPF tab are adapters over it.
- Opening or refreshing the tab only reads the latest persisted Continuation and Breakout picks plus their matching immutable Delphi presentation snapshot.
- ADR-0035 adds Overview, Picks, Market, Granville, Diagnostics, and Full Report views backed by typed report facts instead of parsed console text.
- New official runs store the versioned presentation snapshot inside the existing `CalibrationRun.RunContextJson`; older saved runs use a clearly labelled, date-aligned reconstruction and never substitute current market values.
- An official run requires a warning confirmation and explicitly states that it appends calibration evidence and replaces same-date operational records.
- Core, Delphi, and WPF focused builds succeeded; 110 core tests passed. Delphi itself was intentionally not run and no database record was changed during implementation. No database migration is required for ADR-0035.

Native Project Docs implementation on 2026-08-27:

- ADR-0036 adds a fourth WPF tab that discovers repository Markdown, groups it by folder, searches titles/paths/contents, and defaults to this project-status document.
- A native `FlowDocument` renderer presents headings, prose, emphasis, code, lists/checklists, blockquotes, tables, rules, and links without an embedded browser or new Markdown dependency.
- Core owns testable catalog discovery, exclusions, heading identifiers, and safe link resolution. Relative Markdown links stay inside the tab only when their canonical path remains under the repository root and the target is in the catalog; external HTTP(S) links open only when clicked.
- Refresh reloads files edited outside TraderVI. The feature is read-only and performs no SQL, model, market-data, or trading operation.
- 120 Core tests passed and the focused Release WPF build succeeded. The application itself was intentionally not launched because its Paper Trading tab can poll TMX and write SQL during market hours. No database migration is required for ADR-0036.

Advanced official prediction scorecard implementation on 2026-08-27:

- ADR-0038 adds a pure calculator over `PredictionLabels10` version 1 plus a read-only official-evidence query used by Athena.
- The four model reports include cohort-weighted Brier score, supported AUC, fixed reliability buckets, expected calibration error, and probability-decile event lift.
- Separate eligible Continuation and Breakout reports include Spearman rank IC and Top-1/Top-3/Top-5/top-decile ten-session return lift.
- Diagnostic slices cover OBV, regime, sector, first lens-gate result, published lens, observation dollar volume, and observation range. They are descriptive and cannot change weights.
- Five export-schema-v2 CSV artifacts are available through an explicit non-overwriting Athena option; every artifact carries the selected strategy ID/name, initial code commit, and decision reference.
- 138 Core tests and the focused Release Athena build passed. Athena itself was intentionally not run, so no outcome row or export file was written.
- Migration 014 was applied on 2026-08-28 to seed the four canonical definition contracts that were previously initialized only by Athena. Post-migration inspection found 6 official runs across 4 market-data cohorts, 1,292 official candidates, 2,584 correctly paired lens rows, no duplicate run/lens ranks, and zero outcomes. The WPF query can therefore load coverage while remaining read-only; Athena was not run merely to initialize rows.

Verified operational calibration state on 2026-09-01:

- Migration 015 was operator-applied and read-only inspection verified all five active definitions, including `DelayedIntradaySwing` version 1. A migration-015 backup was not independently verified in this inspection.
- The latest official Delphi run, `B46479FF-23CE-4E25-9842-C0609AE29362`, is valid for recommendation date 2026-09-01 using market data through 2026-08-31. It persisted 218 candidates and 436 lens rows. The ledger now contains 7 official runs across 5 distinct market-data cohorts and 1,510 candidates.
- Both lenses published 25 picks. The first five Breakout symbols were MATR, GTE, BTE, ATH, and ESI; the first five Continuation symbols were MATR, BTE, ESI, GTE, and ATH.
- Athena wrote 112 valid matured `SwingMarkToMarket3` outcomes and 112 valid matured `SwingExcursion3` outcomes. `PredictionLabels10`, `PredictionPath20`, and `DelayedIntradaySwing` remain at zero outcomes.
- The operator launched WPF, but the durable ledger still ended on 2026-08-28: SGY had 76 five-minute bars and 26 fifteen-minute bars, XIU had 78 and 26, and no later receipt was present. The launch therefore does not yet verify a new market-hours collection cycle.

Strategy/code identity activation on 2026-09-02:

- Migration 016 activated fixed identity `99D52317-8D16-4F2A-8B97-AE9698972F55` / `v3.1-rs-date-aligned`, initial behavioral commit `c51c0849fd1311b3797cc664a19988e553bbe122`, decision `ADR-0041`. The prior `v3.0` row remains immutable and inactive.
- The first execution exposed a SQL Server same-batch column-resolution defect before activation. `XACT_ABORT` rolled the schema transaction back completely; all captured schema and row counts were unchanged. The migration was split into two transactional dynamic batches, rebuilt with SSDT, independently reviewed, and committed at `d391db8` before the successful execution.
- Postflight found both identity columns with the expected nullable `nvarchar` shapes, an enabled/trusted `CK_StrategyVersion_CodeIdentity`, no constraint violations or partial identities, one exact active successor, and zero bidirectional threshold or model-mapping differences from `v3.0`.
- Preservation counts remained 8 calibration runs, 1,723 candidates, 3,446 lens evaluations, 5 outcome definitions, and 224 outcomes. No historical run references the successor; the active report scope is 0 included and 7 excluded legacy official runs.
- No TraderVI application, market service, training, outcome, publication, or model-artifact workflow ran during activation.

Leadership-source missingness stabilization on 2026-09-02:

- ADR-0043 makes `ActiveAdvancers`, `ActiveDecliners`, and `ActiveN` an all-null-or-valid observation. A genuine tied basket remains numeric zero; an unavailable, stale, partial, malformed, or unanchored movers response remains null.
- Hermes requires a current-date computed row, exact current-date XIU anchor, and exactly 50 distinct nonblank symbols with explicit price changes before assigning movers data. It does not attribute undated responses to another session or overwrite a valid same-date observation with missingness.
- Granville leadership and light-volume scoring uses a 12-session contiguous canonical XIU suffix. Missing sessions break the suffix, post-calendar rows are ignored, and unavailable or insufficient mover coverage is neutral and explicitly reported.
- The fixed successor identity is `2BD1A7D0-D144-4A7B-9FA4-49606AB7E963` / `v3.2-leadership-missingness`, initial behavioral commit `fad4b968244d57a6224cb7ab137774a9bff4b645`, decision `ADR-0043`. Existing evidence remains immutable under its original identity.
- All 202 Core tests passed in Debug and Release; focused Core, Delphi, Hermes, and WPF Release builds and the complete SSDT-inclusive Release solution build succeeded. Migration 017 also passed offline `TSql150` parsing and independent static review. Existing dependency advisories and compiler-warning backlog remain separate.
- Migration 017 was separately authorized, applied, and independently verified after the synchronized backup. It converted exactly 101 legacy `0/0/0` sentinels to null while preserving all 115 leadership rows; both checks are enabled, trusted, and not-for-replication; v3.2 is solely active; threshold/model differences are zero; and 8 runs, 1,723 candidates, 3,446 lens rows, 5 definitions, and 224 outcomes remain unchanged. No application, external call, training, outcome, publication, or artifact workflow ran.

Unified Trading and WPF scorecard implementation on 2026-08-28:

- ADR-0039 adds a read-only Scorecards tab over ADR-0038's exact official query and pure calculator; it displays coverage, model, reliability, decile, lens, and slice reports without requiring CSV.
- Migration 013 and synchronized canonical schema add `ExecutionMode`, `AccountLabel`, and immutable `PositionExecutionAudit`. Existing rows are deliberately backfilled as Ghost.
- The Trading tab labels every row as `GHOST` or `REAL`, shows accounts, and separates Ghost/Real open counts plus realized/unrealized P/L.
- Saved Delphi picks can be tracked as Ghost or as an operator-reported Real fill. Existing Ghost positions can be confirmed as Real, and actual all-shares Real exits can be recorded manually.
- The shared monitor evaluates both modes, but its tested execution guard permits automatic closure only for Ghost. Real alerts remain manual-action signals; no broker client or order action was added.
- Migration 013 was applied manually on 2026-08-28 after a checksum-verified full backup and hash-matched approved secondary copy. Verification found all expected columns, defaults, enabled/trusted checks, primary/foreign keys, an empty execution audit, and preserved row counts; all 6 existing position rows and 11 trade rows remain Ghost.
- 143 Core tests and the complete Release solution build passed with Visual Studio 18.10 MSBuild, including the SSDT database project. The WPF application, Athena, Delphi, and TMX were intentionally not run during the migration rollout; migration 013 was applied and verified separately as recorded above.

Historical Real-position monitoring correction on 2026-09-02:

- ADR-0044 includes active Real holdings in the Trading dashboard and shared monitor even when they have no Delphi pick link; unlinked Ghost rows remain excluded.
- Monitoring begins at the durable enrollment record rather than pretending earlier intraday evidence exists. Unlinked Real holdings use ordinary conservative loss handling and cannot receive ADR-0028's fresh-Delphi loss exception.
- The existing hard execution boundary remains unchanged: Real alerts require manual broker action and an operator-reported fill; no broker call or automatic Real closure is possible.
- All 213 Core tests passed in Debug and Release, and the focused WPF Release build succeeded. The corrected Release binary was built separately while TraderVI.WPF was closed; no broker operation was used for source validation.

Discretionary Real-entry correction on 2026-09-03:

- ADR-0047 allows an operator to record an actual Real broker fill from a saved Delphi row whose direction is not Buy, but only after an explicit discretionary-override warning.
- The resulting position is deliberately unlinked: `OriginalPickId` and `EntryComposite` remain null, while the selected row's ID, date, lens, rank, and direction are retained in notes. It therefore remains outside official calibration and cannot receive the fresh-Delphi loss exception.
- Ghost entries remain restricted to saved Buy recommendations. Real exits remain manual, and TraderVI still has no broker-order capability.
- All 221 Core tests passed, including direct attribution-policy cases. The focused Release WPF build succeeded to an isolated output because the open app held its deployed `Core.dll`; the running application was not stopped or replaced, and no trade/database operation was performed.

Consequential transaction-boundary stabilization on 2026-09-03:

- ADR-0048 now inserts a tracked BUY and its `ActivePosition` in one serializable transaction, with the BUY linked to its preallocated position ID at insert time and the duplicate-symbol check held under the same lock.
- A same-date Delphi operational publication now replaces narratives, dossiers, picks, and Granville logs in one serializable transaction. A successful zero-result run clears stale same-date rows; append-only calibration evidence remains a separate contract.
- The existing Real-exit transaction now returns the one durable attached SELL on retry after an ambiguous commit instead of creating a duplicate. The repository still rechecks active Real mode and rejects an exit date before entry.
- Monitor snapshot writes now require `IsActive = 1`, so a cycle holding a stale in-memory row cannot overwrite a manual exit snapshot after the close commits.
- Version 1 still supports only one all-shares, zero-commission Real exit. The UI and workflow require explicit affirmation and direct the operator to cancel for partial or fee-bearing fills; their lifecycle/accounting policy remains open.
- Basic Windows GitHub Actions runs all Core tests and focused Release builds for Delphi, Hermes, Athena,
  TraderVI, and TraderVI.WPF.
- Read-only SQL verification found CS (35 shares) and GGD (50 shares) open as operator-reported Real TFSA positions, and SGY closed with exactly one attached 20-share Real SELL. No position or trade row was changed.
- All 225 Core tests passed in Debug and Release. Focused Release builds succeeded for Core, Delphi, Hermes, Athena, TraderVI, and TraderVI.WPF. The final WPF check used an isolated output because an existing process held the deployed Release `Core.dll`; that process was not started, stopped, or replaced. Existing dependency advisories, nullable-context warnings, Hermes' unused local-function warning, and WPF's unused-variable warning remain separate backlog items.

Canonical database write-contract reconciliation on 2026-09-03:

- ADR-0049 records the source/live audit. Read-only metadata and integrity queries found zero duplicate
  `DailyBars` natural keys, zero orphan Quotes or tracked-position references, and zero invalid Leadership
  counts or benchmark prices.
- The SSDT project now represents the identities, defaults, relationships, checks, and indexes relied on by
  runtime writers. In particular, `DailyBars.Id` is an identity and `(Symbol, Date)` is unique; Symbols and
  Quotes expose the defaults omitted by their writers; and tracked positions, trades, picks, models, and
  operational publications retain their live relationships.
- Two dormant symbol-write methods now target explicit `dbo.Symbols` columns instead of an obsolete Ticker
  contract or a mismatched positional insert. Leadership repository validation now rejects counts above the
  stated denominator and non-positive optional benchmark prices.
- Guarded migration 018 was manually applied and verified after a fresh checksum-verified full backup and
  hash-matched OneDrive copy. It preserved 117 Leadership rows and 462 Quote rows, found zero invalid
  Leadership rows and zero Quote orphans, and left both new checks plus the Quotes-to-Symbols foreign key
  enabled and trusted.
- All 236 Core tests passed in Debug and Release. The focused Release Hermes build and the SSDT Release
  database-project build succeeded; the latter produced a DACPAC only as a build artifact. Migration 018
  passed offline `TSql150` parsing with zero errors. No DACPAC was deployed.

Build-only SSDT CI boundary on 2026-09-03:

- ADR-0050 adds an independent database-project job to the existing Windows workflow. It is pinned to the
  documented Visual Studio 2026 image, verifies the expected MSBuild and SSDT targets, and invokes only the
  Release `Build` target with `DeployOnBuild=False`.
- The job receives no database credentials, runs no SQL Server or migration command, and deliberately does
  not upload the generated DACPAC. A Core safety test guards those workflow properties.
- All 237 Core tests passed in Debug and Release. The exact build flags succeeded locally with Visual Studio
  18/SSDT. No database, migration, application, market service, or deployment operation ran. The first hosted
  GitHub Actions result remains pending until this change is pushed.

First v3.2 operating day on 2026-09-02:

- The first deliberate official `v3.2-leadership-missingness` run is valid for recommendation date
  2026-09-02 using market data through 2026-09-01. It persisted 219 candidates, 438 lens rows, and 50
  published picks. The official ledger now contains 8 runs across 6 distinct market-data cohorts and 1,729
  candidates; all earlier identities remain immutable and outside the active scorecard scope.
- The 09:47 and 10:02 Toronto WPF cycles produced valid five- and fifteen-minute receipts for XIU and the
  unlinked historical Real holding, but SGY failed both times with `Tmx15FetchFailed`. A read-only probe of
  the exact production window exposed conflicting values for the shared 09:30 boundary bar returned by two
  overlapping five-day requests; the position record and symbol were not the cause.
- ADR-0045 separates a five-minute collection schedule from the unchanged completed 15-minute policy bars,
  records new receipts as `IntradayEvidenceCollectorV2`, and makes adjacent wide requests minute-contiguous
  without overlap. The corrected SGY production-window probe succeeded with 82 returned / 81 completed
  15-minute bars.
- The rebuilt WPF app started visibly at 10:21. Its 10:22 startup cycle and 10:27 scheduled cycle were both
  fully valid: each stored six valid receipts for XIU and the two Real holdings. The cycles began 4 minutes
  59 seconds apart, verifying the five-minute runtime cadence. The first corrected SGY cycle added 55
  completed 15-minute bars and 164 completed five-minute bars; the next cycle added only the newly completed
  five-minute bar. TraderVI sent no broker operation or automatic Real exit.
- After the 10:22 cycle raised an SGY Real sell signal, the operator reported an all-shares broker fill.
  TraderVI stored one attached Real SELL and closed the tracked SGY lifecycle atomically; preflight and
  postflight confirmed one BUY, one SELL, and no active SGY row. The visibly restarted WPF app's 10:35
  cycle then stored four valid collector-v2 receipts for XIU and the remaining Real holding only.

System Shadow V1 implementation and migration on 2026-09-04:

- ADR-0051 implements four independent System-selected Ghost portfolios: Continuation Top-3/Top-5 and
  Breakout Top-3/Top-5. Each starts with the same operator-entered TFSA value as a separate what-if account;
  positions use equal whole-share slots, an initial 75% allocation, and one possible 25% Delphi-supported
  add-on while profitable and rising.
- The controller freezes only the newest valid same-date official Delphi run available by the policy
  boundary, requires fresh completed five-minute evidence after same-session activation, and records a
  pending order before using the open of a strictly later completed five-minute bar. It does not manufacture
  historical fills or carry unfilled daily candidates into the next session.
- Version-1 controls include a 5% hard loss, a protected-profit trail, Session-2 non-mover rotation/exit,
  a -3% daily new-risk guard, and a -10% capital-review drawdown guard. Pause and capital review stop new
  risk while allowing protective exits. Shadow has no maximum holding period and no broker path.
- Migration 019 added eight canonical Shadow tables. Post-application verification found all 18 foreign
  keys and all 30 checks enabled and trusted, the filtered open-position uniqueness index present, and zero
  generation, portfolio, session, position, order, or event rows. Shadow is therefore installed but off.
- ADR-0052 and migration 020 changed today's exact official run from `Degraded` to `Valid` while preserving
  `WorkingTreeState = Dirty`, 211 candidates, 422 lens evaluations, and 50 published picks. The reviewed
  empty generation contained four portfolios, four `NoValidDelphiRun` sessions, eight startup events, one
  capital snapshot, and no candidates, positions, or orders; migration 020 removed only that graph.
- The Portfolios tab now exposes the selected System portfolio's candidate state, prior/latest five-minute
  evidence, distance from the previous session close, plain-language decision, and evaluation timestamp.
  Portfolio freshness uses that timestamp before a holding exists, making active no-entry polling visible.
- All 266 Core tests passed after the Shadow causality regressions were added. Focused Core/WPF builds, the
  SSDT database-project build, and the complete Release solution build succeeded without deploying the
  DACPAC. Migration 021 parsed with zero `TSql150` errors, then its separately authorized script was applied
  and verified without changing application rows. No workflow called a market, model, or broker service.

## Known gaps and risks

1. **Historical relative-strength features are empty.** This blocks the documented RS-to-Hercules training path and Z-score backfill analysis.
2. **Backup operations still need repeated observation and restore testing.** The first automatic post-success Hermes backup/copy completed successfully, but retention is not automated, OneDrive cloud-sync completion remains user-observed, and no test restore has been performed yet.
3. **Database deployment is intentionally manual.** `TraderDB.sqlproj` is a `Sql150` build/reference artifact and blocks Deploy targets. Live changes use dated scripts under `TraderDB/Migrations`; unresolved project/database drift must be reconciled without broad DACPAC publish.
4. **Model registry hygiene.** Retired experiments remain enabled in SQL even though runtime filtering prevents them from loading.
5. **Universe hygiene requires continued monitoring.** The reviewed full-universe audit is clean, but upstream metadata can classify newly listed ETFs as stocks. Run DataAudit regularly after ingestion changes. Delphi now independently excludes any symbol history that does not match its canonical XIU session before scoring.
6. **CI validates SSDT without deployment.** Windows GitHub Actions runs Core tests, focused .NET application
   builds, and ADR-0050's separate build-only `TraderDB.sqlproj` job. CI has no database credentials and never
   executes migrations, publishes a DACPAC, or contacts the live database.
7. **Outcome feedback has started but is not broadly mature.** Eight official runs across six market-data
   cohorts now contain 1,729 candidates. Athena has written 112 valid three-session marks and 112 valid
   excursion outcomes, but 10-session labels, 20-session paths, and delayed-intraday outcomes remain at
   zero. The delayed evaluator now converts proven evidence gaps into audited invalid outcomes while leaving
   an unproven tail pending. Fresh valid SGY/XIU five- and fifteen-minute evidence now exists after the
   ADR-0045 correction, but Athena has not been rerun against it. Track progress in
   `Docs/calibration-implementation-checklist.md`.
8. **Compiler warning backlog.** A clean Athena/Core rebuild succeeds but reports 236 warnings, primarily nullable annotations outside a nullable context plus existing unreachable/unused code warnings. Treat this separately from the dependency-security advisories and from build failures.
9. **Ghost/Real source support is rolled out, but Real records remain operator-reported only.** Migration 013 is applied and verified. The EDR Ghost mirror completed its paper exit at $15.62 and remains immutable; the operator declined a separate Real entry on 2026-08-28 because EDR is no longer in the current Delphi picks. Version 1 explicitly accepts only one all-shares, zero-commission Real exit and refuses partial/fee-bearing use; the version-2 lifecycle/accounting decision remains open. There is no broker verification, balance, partial-fill, commission, or order integration. No policy signal may be interpreted as a completed real sale.
10. **System Shadow V1 needs clean live observation before policy tuning.** The first session is preserved as
    shakedown evidence and excluded from performance conclusions. The duplicate fifteen-minute trailing-bar
    and stale pending-buy restart fixes are installed through migration 021. Collect several uninterrupted
    sessions before changing the provisional 5% hard loss, Session-2 rotation, add-on, re-entry, or
    daily/drawdown thresholds. Dividends, splits, automatic account synchronization, headless hosting, and
    broker execution are deferred.
## Immediate direction

Stabilize the existing daily advisory loop before adding indicators or automation:

1. Treat the valid 2026-09-04 Delphi run as the newest official `v3.2-leadership-missingness` cohort; run
   Delphi again only when another deliberate cohort is wanted, and do not count repeated runs over one
   market-data date as independent evidence.
2. Migration 021 is applied and verified. At the next real TSX session, keep exactly one `TraderVI.WPF`
   instance open during regular market hours and observe the four virtual portfolios, skipped-entry reasons,
   pending/fill causality, and risk exits without starting a second monitor. This still cannot place a broker
   order.
3. Monitor the remaining open historical Real position, but record a sale only from the operator's actual
   broker fill. A Real alert cannot auto-close it.
4. Run Athena only after new eligible daily or intraday evidence exists. It does not initialize or repair
   leadership data. Confirm that new valid outcomes mature, incomplete tails stay pending, and any proven
   continuity gap is stored as invalid rather than bridged.
5. Add Ghost positions only from current saved Delphi Buy picks. Record an already-executed discretionary
   Real fill from a non-Buy row only through ADR-0047's explicit override confirmation; preserve it as
   unlinked and do not reopen closed Ghost lifecycles.
6. Observe additional Hermes backups and perform a test restore.
7. Continue to larger workflow extraction only after the CI jobs have produced their first successful hosted
   run. ADR-0048's transaction boundaries, ADR-0049's source/live reconciliation, and ADR-0050's build-only
   SSDT boundary are complete in source.
