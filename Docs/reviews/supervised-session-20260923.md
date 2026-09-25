# September 23 supervised TFSA activation review

**Verdict: NO-GO for real strategy-managed orders.** Target date: September 23, 2026 Toronto.
Review finalized September 22 evening Toronto. This is a concrete readiness review, not order authority.
Do not turn the date target into an exception to a failed check.

**23:34 Toronto update:** operator-authorized migrations 035/039/040 are installed and verified.
The operator also clarified that promotion must support any portfolio and persist until pause/switch
([ADR-0085](../adr/0085-portfolio-wide-ongoing-tfsa-management.md)). Common-path implementation resumed
September 23 after the operator deferred rare same-stock manual cases;
this document's Breakout target remains an initial candidate. The launch verdict remains NO-GO.

## Source integration — September 25

The root checkout had newer memory and broker-trade synchronization fixes, while the strategy controls
remained in `codex/supervised-tfsa-20260923`. Restarting the root checkout could not load work from the
other branch. The integration preserves both parents, combines the account refresh paths and retains
one five-minute scheduler. It uses the already-reviewed automatic import operation rather than the
manual review operation's second market-value read, with SQL revalidation and atomic trade projection.

All 1,729 Core tests pass in Release; WPF passes with 261 existing compiler warnings and no errors;
the combined SSDT project builds. No migration contents changed. Both independently numbered 040
scripts are retained under their full original filenames. The supervised-session ADR is renumbered
0086 to distinguish it from the independently accepted external-trade ADR-0084; behavior is unchanged.
This pass includes no broker call, SQL mutation, app restart or trading activation. The earlier
native acceptance dates apply to their earlier builds. The later observations below qualify part of
the combined native refresh/control path and isolated SQL recovery; real-order launch remains NO-GO.

## Native desktop and isolated SQL recovery — September 25

The operator confirms the Strategy management panel in the normal checkout. Their screenshot reports
successful account/history refresh at 16:40:55 Toronto and successful trade synchronization, with no
approved strategy and disabled stop/resume controls. The running executable is the normal
`C:\src\TraderVI\TraderVI.WPF\bin\Release\net10.0-windows\TraderVI.WPF.exe`. No further restart or
binary copy is needed for this observation.

Read-only SQL at 17:02:22 Toronto confirms one linked account, zero initial baselines, zero risk
continuations and zero managed-session revisions. All three immutable triggers remain enabled.
Eight imports between 16:18 and 16:57 each have a trade-sync receipt, spaced approximately 5m30s apart.
This supports recurring persistence in the integrated app; it does not qualify uninterrupted
coverage, rate limits, real partial orders, the setup dialog or a populated assignment's restart.

The opt-in `BrokerManagedSessionSqlTests` rehearsal at 16:55 Toronto uses the actual session repository, canonical
session table and immutable trigger in a newly named, retained SQL database on localhost. It passes:

- Exact JSON and seven-digit timestamp readback, exclusive lease contention and release.
- Same approved strategy, account references and stopped-buy mode after repository recreation.
- SQL connection loss while an insert is uncommitted: rollback, ownership release and recovery of
  the last committed revision. The test verifies the connection's database/application before killing it.
- A committed intent followed by a lost caller acknowledgement: recovery keeps its ID and sends nothing.
- An uncertain synthetic submission: restart reads the original ID without a duplicate or buy.
- A partial exit across restarts: a residual sale follows terminal reconciliation; all required exits
  reconcile before the first purchase; an unresolved purchase is not duplicated.
- Rejection by the canonical immutable trigger.

**Limits:** parent binding/baseline tables contain synthetic IDs only. Baseline/continuation repositories,
production schema compatibility, process/machine power loss and real broker lifecycle are not qualified
by this rehearsal. Broker account facts and transport are the existing synthetic fixtures. Production
connections remain unchanged; only an internal, instance-scoped repository constructor accepts a test
connection factory. No risk policy, strategy selection, production financial record, migration or
automation activation changes.

Validation: 71 focused offline cases pass; the one opt-in native case passes all checks above. The
ordinary test run skips native SQL unless explicitly enabled. Existing compiler warnings remain;
there was no new full-suite/WPF/SSDT run or dependency-security audit for this narrow test increment.
The first sandbox connection failed before database creation; the unchanged encryption settings worked
when the same prepared test ran with approved local SQL access. Two isolated test databases are retained
from the initial and expanded rehearsals; no cleanup/drop was performed.

Private evidence: `artifacts/launch-validation/sql-rehearsal-offline-tests.log`,
`native-session-sql.log` and `native-setup-state.json` in the isolated worktree.
See [running instructions](../running.md#isolated-managed-session-sql-rehearsal).
The next storage check is baseline/continuation save and recovery, alongside native cash-event and
boundary-value qualification. Initial setup and an exact activation review follow those checks.

## Desktop rollout preparation — September 23 evening

**Superseded launch instruction, September 25:** the source integration combines this branch with the
newer main-checkout memory and trade-synchronization fixes. After merging into the main checkout, use
`dotnet run --project TraderVI.WPF -c Release` from `C:\src\TraderVI`; a separate binary installation is
unnecessary. The September 23 isolated executable below lacks those newer fixes and should not be
used as the current combined build. See the [running instructions](../running.md#build-and-run-the-local-desktop-checkout).

The operator restarted `C:\src\TraderVI\TraderVI.WPF\bin\Release\net10.0-windows\TraderVI.WPF.exe`.
Read-only process-path and binary checks confirmed that this remains the older installed build.
No update had been installed before that restart. The normal launch path is unchanged.

A Release build from source commit `114e093294ae1ba0d077dc9b6e89c6c692e7616e` is now prepared in
the isolated worktree at `artifacts/launch-validation/wpf/bin/TraderVI.WPF/release/`.
WPF compiled with 261 existing compiler warnings and zero errors; all 1,702 Core tests also passed
in Release. There was no fresh dependency-security audit or complete-solution validation.
Logs are `artifacts/launch-validation/rollout-release-wpf.log` and `rollout-release-tests.log`.
The private `artifacts/desktop-rollout-20260923/prepared-build.json` records all 118 output-file hashes.

Keep the executable in its isolated checkout and use that checkout as its working directory when
switching hosts: repository-discovered documentation and code provenance must match the source.
Do not merely copy these binaries into the older checkout. Close the older host before opening the
updated one. No agent restart, normal-shortcut change, broker call, database mutation or execution
activation ran during preparation. Required migrations remain installed and must not be replayed.
Native account setup, refresh and recovery checks remain pending, and the launch verdict remains NO-GO.

## Exact target and decision

The operator selected the ungated Breakout three-position portfolio:

- Paper portfolio: `77bcbaa9-43fe-4259-aff7-25f30554e450`.
- Strategy: `BreakoutTop3`, Daily Shadow, `PortfolioRulesOnlyV1` (Portfolio rules only).
- Pinned daily version: `48864449-92f4-4e33-99cf-40039210786e`.
- Shadow policy: preserved V1; no policy settings, thresholds or sizing rules changed.
- Read-only definition review at 22:55:29 Toronto: managed version
  `9dd7d37c-b5fc-10d5-cc9a-ab20010b5056`, fingerprint
  `69568623C5B1F0DD7DFAD64D9C610F4F38C880BDAE369F8045B2FB40960F50A1`, implementation
  `CoreSha256:52855CD1E0E62300B2F8C7BD05E554571C6B595AA8F43A68B03DDEF5320196D1`.
- The final build's exact implementation/strategy fingerprints must be regenerated with
  `BrokerReadiness strategy-review` before a later activation. Build identity is part of approval.

The pending cash-reference question was already accepted September 22 (ADR-0083). Keep measured loss
percentage and holds: reference 10,000, value 9,800, then deposit 1,000 gives value 10,800 and adjusted
reference 11,020.408163…; measured loss remains 2%. Matched before/after books and complete effective
cash-event coverage are mandatory. A net total or occurrence timestamp alone is insufficient.

## What passes, and its evidence limit

| Check | Evidence | Limit |
|---|---|---|
| Existing work preserved | Local checkpoint `761fb78`; isolated `codex/supervised-tfsa-20260923` worktree based on it | No push or operational deployment |
| Existing reliability changes integrated | Compact-storage/backup controls, UI worker/quote changes and broker history controls in the checkpoint | Earlier installation evidence remains September 17; not reinstalled here |
| Exact selected strategy resolved | Native read-only SQL review identifies the comparison portfolio, daily identity and pinned policy | Definition identity is not strategy performance qualification |
| Signed-in native broker reads | September 22 **22:25:27 Toronto**: history, holdings, pending orders and regular day graph succeeded; stable activity checkpoint and matching holdings | Local order journal not independently checked; no mutation/lifecycle test |
| Historical day points | 79 points, September 22 09:30–16:00 Toronto | Does not prove September 23 morning publication timing or the Shadow valuation method |
| Core regression suite | Sep 25 integration: **1,729 tests passed**. Later recovery increment: 71 focused offline cases plus one opt-in SQL case pass | Full-suite result precedes the narrow repository test increment; synthetic broker transport does not prove native fills |
| Shared account refresh | Sep 25: same completed broker read for display/history; native imports and trade-sync receipts observed about 5m30s apart | Bounded observation; native rate/failure coverage and risk-evidence qualification remain. No automatic first link, order submission or uncertain-order recovery. |
| Desktop and database projects | WPF builds (261 existing compiler warnings, zero errors); separate Visual Studio SSDT project builds | No full-solution health claim or app restart |
| Migrations 035/039/040 | Installed at 23:34:11 Toronto after a checksum-verified full backup and SHA-256-matched copy; three empty tables, enabled immutable triggers, trusted constraints and six native zero-row update/delete rejections | No operational baseline/session; Sep 25 rehearses managed-session SQL only, with baseline/continuation integration still pending |
| Durable workflow | Tested approval, intent before send, uncertain/lost responses, concurrent ticks, partial terminal exit/residual, exit-to-buy sequence, restart protection, scope/stale/conflict blocks and reference continuation | No production host is enabled |
| Stop/resume and restart | Source/tested ongoing approval, checked multi-day recovery and saved TFSA controls. Sep 25 native empty-assignment panel and synthetic SQL stopped-mode recovery observed. Missed closing exits survive rebound; missing closing evidence blocks further orders. | Existing one-day approvals still expire; full pause/invalidated reviews remain. The cumulative ceiling persists. Populated real-assignment recovery, automatic execution-host startup and family-wide adapters remain; no activation. |

Private, ignored evidence: original checkout `artifacts/first-real-session-20260923/`; isolated
worktree `artifacts/launch-validation/` including `native-readonly.json`, `strategy-review.json`,
`all-tests.log`, `managed-tests.log`, `wpf.log` and `sql-build.log`. Sep 23 adds
`synchronization-tests.log`, `synchronization-all-tests.log` and `synchronization-wpf.log`, followed by
`buying-mode-tests.log`, `buying-mode-all-tests.log` and `buying-mode-wpf.log`. The latest ongoing/control
increment is in `ongoing-all-tests.log` and `ongoing-wpf.log`. Duration and purchase mode use existing
JSON snapshots; no migration or native operation was needed. No account identifiers, balances,
credentials, raw broker responses or model artifacts are committed in this review.

Installation receipts are in isolated-worktree `artifacts/promotion-storage-20260923/`:
`backup-receipt.json`, individual migration logs, `verification.json`, before/after preservation
hashes and `installation-receipt.json`. Binding/import and account-report/promotion rows were unchanged.
Backup size: 1,049,611,264 bytes; local SQL checksum and destination SHA-256 passed. Cloud synchronization
and a restore drill were not verified. The manual fallback created a new generation without retention
cleanup. No trading records were inserted by installation.

## Shortest remaining dependency route

1. **Qualify the native risk source.** Produce complete external-cash event intervals, effective cash
   timing and matching books. Verify Shadow's existing saved-price boundary method and morning
   availability; recover missing closes in sequence. The supplied capture/current graph do not establish
   this. Implement the qualified native `IBrokerRiskEvidenceSource`; the shipped default blocks it.
2. **Qualify installed storage at runtime.** Migrations 035, 039 and 040 are installed; do not replay them.
   Schema, immutable-trigger rejection and preservation checks passed at 23:34. September 25 rehearses
   managed-session append/readback, precise timestamps, competing leases and interrupted/uncertain saves
   in isolated SQL. Still rehearse baseline/continuation repositories before operational setup;
   the session rehearsal's ID-only parents do not qualify that integration.
3. **Establish account setup.** In the built app's TFSA account panel, use **Account risk setup…** to
   refresh the linked account and review preserved references, holds and protection. Save explicitly
   only after storage is installed. Verify recovery returns the original saved request and confirm daily
   and cash-flow continuations use real source evidence. The integrated strategy panel is observed;
   the setup dialog and baseline save remain unverified.
4. **Complete a current transition review.** Use the target session's compatible adopted publication
   and current broker facts to identify exact non-overlap exits and retained cycles/quantities/costs.
   Name the current protection owner for each retained position and verify its carried floor/state.
   Set the supervised cash ceiling and identify the exact build, baseline and evidence records.
   These financial details remain pending a fresh qualified account/session snapshot; yesterday's
   candidate list cannot authorize tomorrow's liquidation.
5. **Qualify evidence and native execution.** Review comparable strategy periods/capital/costs/coverage,
   downside and untouched evidence under the established automation contract. Obtain separate explicit
   authority for native small-order lifecycle verification, including partial/uncertain/restart recovery.
   Offline fake broker tests cannot replace that evidence. No requested operational approval is pending.
6. **Verify and roll out the supervised host.** The composition factory joins the existing connection,
   shared journal/semaphore, source, reviewed calendar, SQL store and Core workflow without starting
   any loop. Supply the qualified native source and install the reviewed supervision entry point. Verify old protection
   ownership and manual/advisory coordination, pause/recovery, current inputs and refresh cadence.
   Prepare the final exact activation packet and only then request explicit execution/activation
   approval. Do not schedule or start automation in this preparation step.

Baseline/continuation SQL runtime rehearsal, native source implementation/qualification, supervised host rollout and
the fresh financial activation packet remain development/verification work. These are not completed
merely by introducing interfaces. Core supports new explicitly approved ongoing Daily Shadow assignments;
existing one-day approvals retain their expiry. Strategy replacement, resolving externally invalidated
reviews, manual ownership and the Delphi Live protection adapter remain unfinished. Shared synchronization,
ongoing recovery and TFSA saved-mode controls are source/tested. The controls restore selection but
start no execution loop. September 25 confirms their native empty state and recurring account persistence;
populated assignment recovery, native qualification and automatic host recovery are still required.

## Demonstration without operational effects

Run the focused tests from the isolated checkout:

```powershell
dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --filter FullyQualifiedName~BrokerManaged --verbosity minimal
```

`ReconciledExitFundsFirstBuyAndRestartProtectsItsRealPosition` exercises actual Core workflow calls:
save approval → reserve/send synthetic exit → import confirmed exit → transfer/activate → wait for
prospective evidence → reserve/send synthetic buy → restart → recover the same ID → protect its real
synthetic fill. Other cases demonstrate blocking partial/unknown orders, duplicate prevention, stale
and conflicting data, external changes, session expiry and preserved loss references. No SQL/broker
service is called by these tests.

`BrokerManagedSessionTests.BuyingMode.cs` adds a stop/restart demonstration: persist the purchase mode
and strategy → reconstruct the saved session → synchronize current facts → continue applicable exits
without replacement buys. It also exercises restart with buying enabled, blocked fresh reads, lost
mode-save replies, partial required exits, protection breaches and a stop during an in-flight buy.
The original uncertain order is never resent; a confirmed fill still acquires protection. Legacy
snapshots retain their previous buying behavior, while restart/resume cannot clear holds, invalidate
history or extend an existing one-day approval.

`BrokerManagedSessionTests.Ongoing.cs` demonstrates explicit ongoing scope across checked days,
reference/hold preservation, original uncertain orders, missed protection and closing exits, missing
close recovery, cumulative purchase commitments, and rejection of changed builds/stale accounts.
`BrokerManagedStrategyViewModelTests` and the account polling integration tests demonstrate saved
selection restoration, stop/resume, lost-save readback and discarding late results after logout/shutdown.
The native bar query now includes missed prior-day protection history and uses reviewed calendar
boundaries for closing rules; its SQL/runtime and actual market coverage still need qualification.

The read-only console in `Operations/BrokerReadiness` resolves the chosen strategy and verifies the
saved signed-in broker session. It has no order, refresh-token, import, migration or activation mode.
The running app remains the only owner of its saved login.

## Operational approval packet still required

The final packet must include exact session/build/strategy identities, current account fingerprint and
baseline revision, reviewed exits/retained holdings and protection owners, usable funds and estimated
purchase ceiling, scoped evidence references, migration/readback receipts, native lifecycle receipts,
one host owner, pause procedure and supervision window. Name the explicit SingleSession or Ongoing
duration and cumulative purchase ceiling; preserve older one-day expiry. Name the operator responsible
for carried holdings whenever execution is unavailable, and review close-time exits against
regular-session order availability. Ongoing scope cannot replenish spending or qualify absent evidence.
Market orders have no guaranteed price.
Explicitly approving that packet is separate from selecting this paper strategy or signing in.

No real broker order, automation activation, model training, market-data collection, agent-initiated app
restart, scheduler installation, push or PR ran. The operator restarted the integrated app themselves.
Production storage installation created the three empty risk/session tables and constraints/triggers;
no operational baseline or assignment was created. September 25 adds writes only to new synthetic
rehearsal databases and read-only production verification. Dependency-security advisories were not
freshly audited; existing compiler warnings are not test failures.
