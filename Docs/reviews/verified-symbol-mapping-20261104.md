# Verified symbol mapping and buy-to-protection repair — November 4, 2026

Owner: [issue #24](https://github.com/JosephMawer/TraderVI/issues/24).
Contract: [ADR-0088](../adr/0088-source-symbol-aliases-for-monitoring.md).
Scope: source changes, disconnected validation, separately authorized restored-copy rehearsals and
the authorized live handoff/installation below. Operator build approval and normal runtime acceptance
remain distinct. No SQL migration or alias seed is required.

## Problem and repair

The November 4 read-only investigation found that an opening buy had already been applied to compact
storage under its market symbol. The managed account projection used the broker listing spelling.
Publication tried to rename the existing position and hit `Position cycle identity changed.` before
ordinary protection could proceed. The shared observation then lacked the advanced managed head
needed to prove the new tracked holding. Separately, Paper, research and desktop construction omitted
the Real-observation dependency used by the old fallback resolver.

The repair preserves the original position and order identities. The common verifier checks the
original purchase, broker security/listing, deterministic position cycle and fill boundary before
adapting a derived reference. It retains quantity/cost, permission, protection owner, stops and first
exit. The storage identity guard remains enforced. Every holding consumer now has the same mandatory
mapping reader, independent of whether that consumer includes broker portfolios.

## Consumer inventory

| Boundary | Shared contract and retained checks |
| --- | --- |
| Managed purchase, tracked holding and Real subscription | `BrokerMarketSymbols.From` / `ForTrackedHoldings`; original BUY, security, listing, cycle and unchanged current-head proof. |
| Market collection, Paper, research, desktop | `HoldingMarketSymbolResolver` through each service's constructed `DelphiLiveHoldingSource`; canonical stock, reviewed alias or verified tracked correspondence; owner IDs and action flags retained. |
| Valuation, signals, duplicate-buy prevention, independent bars | Recommendation repository and evaluator retain `BrokerMarketSymbols`; canonical market inputs, original broker recommendation/protection identity. |
| Quotes | Market quote requests use the canonical reference; held broker bids accept its proved correspondence while retaining exact security, inventory, exchange and time checks. |
| Compact publication and progress | Preserve the symbol on the committed position. Group entry/exit progress under that same verified reference; reject conflicting owned memory. |
| Protection and closing order | `RequirePosition` plus current-book checks; preserve the original exit and use the compact position's symbol for closing orders and native preflight. |
| Sale recovery and reviewed build successor | Same identity proof when comparing broker projection with original compact position; unchanged financial approval and no repeated fill. |
| Opening/closing risk valuation | Existing scoped order reconciliation uses the same listing verifier; reads market bars under the verified original purchase name. |

Provider security search and legacy broker import retain their own broader Canadian listing contracts;
they do not establish managed ownership or write a market alias. No suffix-only fallback was added.

## Offline validation

`BrokerManagedSessionTests.BuyMappingPath.cs` exercises the actual service constructors with synthetic
current-head reads, and the ordinary managed workflow with synthetic broker clients, an in-memory
compact database and temporary protected checkpoints. No installed journal or broker is used.

- Opening buy fill, managed publication, position protection, canonical stock/benchmark inputs,
  valuation, verified bid, sell preflight, original sell reconciliation and reload.
- Lost commit reply and repeated publication: one purchase charge, original shares/cost and no
  duplicate submission. The fixture explicitly reproduces rejection of the old unadapted position.
- All four constructed holding consumers resolve a newly purchased holding without an alias.
- All four reject absent/changed heads, wrong position/security/exchange, unmanaged records and
  conflicting aliases. Existing tests cover successive new purchases, canonical/manual cheap paths,
  missing canonical targets and unchanged observation permissions.
- Existing compact recovery, original-sale interruption, build-successor, quote freshness, account
  identity and order-preflight regression tests remain part of validation.

Validation commands use `--artifacts-path artifacts/build --no-restore`; builds do not replace the
running applications. The final counts and build results are recorded in project status.

## Remaining acceptance

This is source/offline evidence, not evidence that today's running positions are protected. The
installed engine and collector were not restarted or replaced. No broker/market request, live trading
record write, nightly job, permission change or order was performed. The separately authorized copy
rehearsal below took one production copy-only backup.

**Transition prerequisite:** the last investigated managed head still marked the already-filled buy
unreconciled. `BrokerImplementationReviewWorkflow.RequireSession` / `RequireAccount` and
`CompactBuildSuccessor` deliberately require a settled, unchanged account. The offline ordinary
workflow and restored-copy rehearsal prove the corrected handoff under the same assignment. They
initially did not provide a live maintenance entry point for installing a new implementation over that
interrupted state. The guarded command described below passed its exact Release copy rehearsal and
the live reconciliation. This [installation question is resolved](open-questions.md#issue-24-installation-transition-from-the-interrupted-buy--november-4-2026);
operator build review and resume subsequently completed as recorded below.

For live acceptance, verify the existing filled buy advances the managed head once,
current protection/bid timestamps advance for each holding, shared publication and Paper resume on
current-session inputs, and subsequent new holdings resolve without alias repair. Keep
[issue #20](https://github.com/JosephMawer/TraderVI/issues/20) native acceptance open. Generic diagnostic
work under [issue #23](https://github.com/JosephMawer/TraderVI/issues/23), the earlier bid-source
investigation and the collector's unexplained exit are not resolved by this code change.

## Filled-buy transition on the restored copy

Joseph separately authorized this rehearsal with no broker access, installation or live restart.
`Operations/New-PaperRestartCopy.ps1` took and verified a fresh compressed/checksummed `COPY_ONLY`
backup at about 14:29 Toronto and restored it into a new token-marked LocalDB instance. The harness
uses `RestartRehearsalDatabase` for every connection and a separate protected-checkpoint directory.
The two installed compact checkpoints were copied and hash-compared; their original bytes remain
preserved. Restore changes database identity, so only the test-local checkpoint was explicitly paired
to the copy after verifying its original digest and managed revision. No financial field was remapped.

`BrokerManagedSessionTests.BuyCopy.cs` runs the real managed repository, compact writer and ordinary
account/protection workflow against the copied data. Its account source uses the copied complete broker
import and a clock frozen at that observation; the approved no-outside-change reconciliation checks
still run and risk history is preserved. It constructs no broker or market client. Strategy-input reads
end the handoff phase before evaluation, and its order transport always throws. The source's old build
identity is a **simulation of the existing assignment**, not permission to run a new normal engine under
the old approval. The simulated build review uses a clearly synthetic fresh source receipt.

Three opt-in phases passed in separate test processes:

| Phase | Verified result |
| --- | --- |
| Handoff | The original pending managed BUY matches the already-filled compact receipt. All four holdings reconcile; original request, fill time, shares, cost, purchase allowance and existing protection remain intact. The new holding receives its normal fill-based managed protection. Repeating the handoff does not duplicate its fill or charge. |
| Review | Unreconciled starting state and an enabled simulated host grant reject review. After reconciliation, the existing review workflow creates its immutable settings successor with unchanged financial permission and position protection. Its expected successor grant remains disabled. Repeating the original saved review does not append another approval. |
| Reload | A fresh process reloads the actual SQL state and protected checkpoint and rechecks the original fill, all positions, protection and disabled successor grant. The existing bounded historical-hash validator also accepts the retained review across processes. |

Initial harness assertions needed value comparison after deserialization, filtering active holdings
while retaining historical protection, and nullable unearned-floor handling. No trading gate was
relaxed. A recreated review had a different whole-session hash from process-dependent memory dictionary
ordering; retry correctly uses the **original saved request**, not a newly generated approval. The
existing bounded historical-hash contract governs validation of that retained review across processes.

Reproduce only under isolated-copy authority: build the Core test project in `artifacts/build`, create
the private copy, copy/hash the compact checkpoint slots, then run `Operations/Invoke-BuyCopyPhase.ps1`
with that token and phases `handoff`, `review`, `reload`. The repository's new internal publisher seam
keeps SQL publication transactional while directing protected files to the copy. Ordinary construction
continues to use the installed publisher. Default tests skip this fixture.

Private evidence: `artifacts/paper-rehearsal/8acaa02f899a43e8a2058df6116fb5ea/`, including the original
checkpoints, private baseline, phase logs and `buy-copy-*-passed.json` aggregate receipts. Full offline
regression passed **3,695 tests with 18 opt-in skips and zero failures**; the three native copy phases
were run separately. Core/test compilation retained existing warnings, with no new warning in the
rehearsal file and no dependency-security warning emitted; `--no-restore` did not refresh security
auditing. The SQL project/full solution and a new Release package were not built for this rehearsal.
Documentation checks passed 864 links with zero errors.

The private LocalDB was stopped after validation. Its database files and dedicated backup total about
33.8 GB (decimal) and remain preserved for follow-up/explicit cleanup; nothing was installed. At the
final process check, engine PID 32368 retained its 01:51:41 start, collector PID 32356 its 08:53:59 start,
and dashboard PID 33432 its 01:40:48 start (Toronto). This establishes no restart, not current trading
health. This copy
does not prove fresh live quotes, protective submissions, broker inventory today, current market
publication or the live engine's restart behavior. The operational original-buy maintenance boundary was
subsequently prepared below; fresh broker checks, operator build review and separate resume remain outstanding. Do not run the test
harness against production or reuse its synthetic identity/source evidence there.

## Guarded installation preparation

Joseph subsequently authorized the live switch and engine restart. `SettledBuyTransition` and the
explicit `--reconcile-filled-buy` command implement the original-request maintenance boundary before
installation: exact old build/grant, exclusive clean engine ownership, matching compact seal, resolved
original journal, zero remaining fill delta, fresh broker account/history and unchanged risk evidence.
No scheduler, evaluator or submission transport is constructed. The original account import uses the
ordinary validated linked-account refresh; financial permission and all original protection remain.

Twelve focused checks pass, covering a valid idempotent compact write and changed revision/request,
quantity, cost, security, symbol, risk, position, listing, freshness and permission. The final Release
Core suite passed 3,707 tests with 18 opt-in skips and no failures; WPF/engine build passed with two
existing CS0168 warnings and no errors. No dependency-security warning was emitted, and no fresh audit
was run with `--no-restore`. The tested Release Core hash matches the prepared WPF package.
The exact repair, normal simulated review and fresh-process reload passed on fresh private copy
`780662c582a4483fab49ac33021efac0`, preserving the original fill and all four holdings/protection through
the real SQL writer. Its captured account/source facts
remain copy evidence, not live readiness. No SQL schema migration is required.

## Live transition

At Joseph's explicit installation/restart authorization, the fresh copy-only backup was verified,
restored, hash-matched at the established backup destination and confirmed synchronized. The old
runtime and protected checkpoints/order journal were copied and checksum-verified after a clean
engine/dashboard stop. A timestamp-type mismatch in the wrapper initially refused before any change;
the exact owner timestamp comparison was corrected without relaxing ownership checks.

The guarded Release maintenance succeeded at approximately 15:25 Toronto, using fresh ordinary broker
reads/imports. Read-only SQL verification confirmed one successor revision (`FilledBuyReconciled`),
the unchanged original terminal fill receipt, all 22 managed orders reconciled, four open positions
with saved protection and zero active compact restrictions. The guard preserved financial assignment,
prior holdings and protection and initialized the missing managed protection from the actual fill.
No order submission, schema change or new financial permission occurred.

The 140-file tested package from source commit `32bd5119` was installed and hash-verified. Core SHA256:
`A6B891CDD7C96D21BF01216295BEB48447D5028C2E236AE43D3551702C977043`.
Engine PID 24284 started in build-review mode at 15:25:55; dashboard 30724 opened for the operator.
At 15:26 the broker was connected, execution Off and no integrity incidents were reported.
Collector PID 32356 retained its 08:53:59 start. This review-mode check alone did not establish active
position monitoring; normal runtime evidence follows.

Joseph then completed the app's exact-build approval and separate resume. SQL receipt hash and the
protected restartable grant both matched the installed Core. Normal engine PID 22780 started at
15:28:25. All three Paper portfolios qualified from saved state within 3,263 ms; incremental verification
was enabled, with no new full historical scan, restart failure or integrity incident. Existing older
incomplete audit notices remain historical and are not a new scan. Broker connection and all 15 worker
checks passed, with zero consecutive worker failures. The collector owner remained unchanged.

The verified version-2 execution diagnostics showed fresh broker bids for all four holdings at
15:28:40–42, each with matching saved protection. The ordinary execution worker selected a
`HardLoss5Pct` protective sale, received acknowledgement, then confirmed and reconciled its fill by
15:29:14. The original morning buy receipt remained filled/applied; the sale was a separate ordinary
protective action after Joseph's resume, not a resubmitted buy or installation order. At 15:29:24 all
23 managed orders were reconciled and the three remaining holdings retained matching protection and
the fresh bids. SQL showed zero unfinished orders. This establishes the existing-position protection
path, not prospective acquisition of a new holding or continuing protection at all future times.

**Remaining entry-data blocker:** the collector had stock and benchmark observations through 15:28,
but shared publication still ended at 10:20 (last published 10:22). Paper's saved consumer receipts
remained `NotActionableLate`; fresh Paper entry evaluation is not established. Real completed a new
evaluation at 15:29:24 with zero fresh supplied inputs, `NoRecommendation`, and
`NetAssetValueUnavailable`; its current status explicitly waited for fresh shared market inputs.
This is not a healthy no-signal evaluation. The mapping installation and saved-state restart passed,
but the shared publication gap and entry valuation require investigation before full entry readiness
can be claimed. No further source change, restart, model change or historical recovery was attempted.

Native capture directories under `artifacts/market-watch-20261029/`:
`20261104T202904695Z-9edba38a39f244428d64a405d12b3be9` (four fresh bids and acknowledgement),
`20261104T202954885Z-f091a030503f4b39a54f11a94761226b` (settled sale, three remaining holdings).
Their `diagnostic-location.txt` pointers identify the verified execution receipts. The bounded
`market-and-orders-result.json` query hash is
`749FFD4D18D361D423A81DF0EA1C32EC0546BD47B9BE2CFF9DB511D353C25D18`.

Private receipts are retained under `artifacts/mapping-install-20261104/`: `package.json`,
`backup-verified.json`, `backup-sync.json`, `live-handoff.json`, `live-handoff-verified.json`,
hash-verified `after-result.json`, `installed-review.json` and unique review control receipts.
The initial read-only postcheck required an explicit SQL collation match; its corrected query hash is
`DF3995848D2AB6D261BC5BC1E6CFC7F921724F7C49D64828EE88DEA01A180CAA`.
The prior runtime and both private rehearsal copies remain retained; none was deleted by this switch.
