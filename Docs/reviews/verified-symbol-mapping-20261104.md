# Verified symbol mapping and buy-to-protection repair — November 4, 2026

Owner: [issue #24](https://github.com/JosephMawer/TraderVI/issues/24).
Contract: [ADR-0088](../adr/0088-source-symbol-aliases-for-monitoring.md).
Scope: source changes, disconnected validation and a separately authorized restored-copy rehearsal. Installation, engine restart and live
acceptance require separate operator authorization. No SQL migration or alias seed is required.

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
workflow and restored-copy rehearsal prove the corrected handoff under the same assignment. They do
not provide a live maintenance entry point for installing a new implementation over that interrupted
state. Prepare and qualify that original-buy-only boundary before normal build review, without weakening
its gates. This remains an
[open installation question](open-questions.md#issue-24-installation-transition-from-the-interrupted-buy--november-4-2026).

After separately approved installation, verify the existing filled buy advances the managed head once,
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
publication or the live engine's restart behavior. The operational original-buy maintenance boundary,
fresh broker checks, operator build review and separate resume remain outstanding. Do not run the test
harness against production or reuse its synthetic identity/source evidence there.
