# Verified symbol mapping and buy-to-protection repair — November 4, 2026

Owner: [issue #24](https://github.com/JosephMawer/TraderVI/issues/24).
Contract: [ADR-0088](../adr/0088-source-symbol-aliases-for-monitoring.md).
Scope: source changes and disconnected validation only. Installation, engine restart and live
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
installed engine and collector were not restarted or replaced. No broker/market request, live database
read/write, nightly job, permission change or order was performed for this implementation.

**Transition prerequisite:** the last investigated managed head still marked the already-filled buy
unreconciled. `BrokerImplementationReviewWorkflow.RequireSession` / `RequireAccount` and
`CompactBuildSuccessor` deliberately require a settled, unchanged account. The offline ordinary
workflow proves the corrected handoff under the same assignment; it does not qualify installing a
new implementation over that interrupted live state. Rehearse an explicitly scoped reconciliation
before normal build review, without weakening its gates. This remains an
[open installation question](open-questions.md#issue-24-installation-transition-from-the-interrupted-buy--november-4-2026).

After separately approved installation, verify the existing filled buy advances the managed head once,
current protection/bid timestamps advance for each holding, shared publication and Paper resume on
current-session inputs, and subsequent new holdings resolve without alias repair. Keep
[issue #20](https://github.com/JosephMawer/TraderVI/issues/20) native acceptance open. Generic diagnostic
work under [issue #23](https://github.com/JosephMawer/TraderVI/issues/23), the earlier bid-source
investigation and the collector's unexplained exit are not resolved by this code change.
