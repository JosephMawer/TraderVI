# Paper workload, execution waits and storage — October 21, 2026

Status: investigation for discussion, not an accepted redesign or implementation authority.
The immediate problem is multi-minute waits and an apparently blocked desktop. The parent problem
is research/account history sharing the time-sensitive execution path. The root goal is responsive
supervision and timely execution of the approved strategy with unchanged financial controls.

## Plain explanation

The expensive paper cycle already runs on a worker thread. Setup waits asynchronously, but real
execution deliberately queues behind admitted paper work. A responsive window and an execution
host that can proceed are different requirements. Moving more work to a worker cannot remove this
dependency. Current setup code also disables its action buttons while its operation is pending.

The cycle does much more than calculate a signal: it repeatedly loads complete account histories,
protects holdings, validates transitions, packs/hashes snapshots, persists changes and reads them
back. A small new observation can therefore cause work proportional to accumulated account history.
Full-cycle settings locking makes that cost an execution delay. Separately, saved evaluations copy
the session's growing stock and benchmark bar arrays into each policy/symbol/checkpoint record.
These are distinct processing and storage amplification problems.

## Relationship to the pinned architecture

Read the complete available turn and original visual from
[Visualize TraderVI architecture](codex://threads/01a0f510-b0b3-7740-8c36-8d84022846a7), completed
September 30 Toronto / October 1 UTC. Its main structure still applies: separate application hosts,
one shared Core library, and SQL/file persistence. Paper, broker and official research evidence
retain different owners. Core boxes are logical responsibilities, not isolated running services.

The missing edge is inside TraderVI.WPF: shared Live collection/evaluation, paper protection/actions,
and real execution coordination share one host and a broad settings fence. Paper and real balances
are separate, but their scheduling is coupled. In the visual this sits between the desktop host,
Core portfolio engines/repositories and SQL. Hermes collection, Hercules model training and Athena
outcomes are separate routes; none explains the measured eight-minute desktop cycle by itself.

The visual's dated labels saying automatic strategy execution is off and its host pending are no
longer a current implementation summary: a scoped host was approved/enabled and evaluated October 21;
the dated permission expired at 16:00. No October 22 approval is inferred. This investigation found
no running TraderVI.WPF process, and installed Core identifies c4d094fd. It did not launch the app.
AI integration labels require their own current-status review; this investigation does not requalify them.

## Source evidence

- `Core/Runtime/DelphiLiveDesktopService.cs`: `TickAsync` uses `Task.Run`; `TickCoreAsync` holds
  `EngineSettingsLease` across the monitor and returned snapshot/protocol preparation.
- `TraderVI.WPF/Viewmodels/BrokerSetupCycleBarrier.cs`: setup defers new paper cycles and awaits
  admitted cycles. `ManualBuyCoordinator.ExecutionHost.cs` applies this admission to actual polling
  and consequential setup operations. Interactive waiting updates elapsed time and can be cancelled
  by closing setup; it does not cancel admitted financial work. Poll admission defaults to two minutes.
  Opening/readiness refresh and grant enable have existing exemptions; not every setup button waits.
- `Core/Db/EngineSettingsLease.cs` takes shared `TraderVI.EngineSettings` ownership;
  `TradingControlRepository.FenceAsync` requests exclusive ownership by default. Historical error
  51413 establishes a real account-import conflict. Removing the barrier alone is unsafe.
- `DelphiLiveMonitorWorkflow.TickAsync` loads all active portfolios, synchronizes sources, runs
  protection before collection, loads portfolios again, saves evaluations, then processes portfolios
  serially. Progressive protection and checkpoint protection are separate passes. The final runtime
  snapshot rereads portfolios/evaluations. Some protection passes serve causal requirements; their
  repeated full-history loads are not automatically necessary.
- `DelphiLiveLedgerRepository.CommitAsync` rereads the previous complete state inside its transaction,
  validates the transition, saves the next state and appends events. `DelphiLiveSnapshotStorage`
  packs complete collections, stages their contents/membership, inserts only new/changed values,
  then reads the saved parts back and validates the complete fingerprint. Disk deduplication does
  not eliminate processing of unchanged history. A checkpoint and each relevant protection/quote/
  action transition can repeat this path; counts depend on holdings and events, not just trade fills.
- ADR-0098's cache still fetches and compares all exact stored parts on every read. A changed
  revision requires full verification. ADR-0099 reduces allocation/report escaping but preserves
  the full-history contract. Neither bounds work by new data alone.
- `DelphiLiveMonitorWorkflow` builds stock and XIU session bar arrays for each policy/symbol input;
  `DelphiLiveEvaluationRepository.PersistAsync` serializes the entire input and result. The canonical
  bars already exist in collected evidence. Shared benchmark histories are therefore copied across
  symbols/policies and successive checkpoints. An alternative needs exact immutable references,
  original policy/state/timestamps and reconstructibility, not simply omission of evidence.

## Measured evidence and limits

The [earlier primary report](delphi-live-sql-reliability-20261021.md) records a 502.90-second admitted
cycle, 438.82-second setup wait, 3.34 seconds collection, 1.49 seconds evaluation and 188.39 seconds
paper actions. Snapshot stages nest inside other stages; their totals must not be added as separate
wall time. Later approximately 264–297-second cycles are still material against a seven-minute
freshness limit, especially with collection scheduled two minutes after the bar endpoint.

These are historical observations on earlier builds, not a benchmark of the current c4d094fd build.
Source confirms repeated history work and broad admission; exclusive CPU/SQL/GC cost and native
dispatcher responsiveness on the latest build remain unmeasured. The source's remaining runtime
snapshot/protocol reads inside admission also merit separate timing. No claim of a memory leak,
missing index, or precise SQL execution-plan cause follows.

Two installed-helper reads, with receipt/query SHA256 verified, establish current storage:

| Area | Measured size | Meaning |
|---|---:|---|
| TraderDB data file | 21.250 GiB allocated / 21.217 GiB used | Database storage, not process memory |
| Live evaluation table | 13.530 GiB used | Largest table; 167,268 evaluation rows |
| Live research outcome revisions | 3.344 GiB used | Second-largest table; 196,674 revisions |
| Live ledger events | 1.123 GiB used | Additional immutable evidence |
| Broker session revisions | 0.809 GiB used | Separate real-account history format merits later review |
| Live portfolio revisions / shared snapshot content | 0.659 / 0.066 GiB used | Earlier compacting has not simply reverted |
| Repository artifacts | 56.58 GiB logical file lengths | Includes 31.76 GiB nightly and 7.44 GiB October 18 TFSA work |
| Backup staging / OneDrive copies | 13.62 GiB each in logical file lengths | Each has two regular full backups plus five special recovery backups |
| Local application directory | 0.16 GiB logical file lengths | Not the dominant disk consumer |

The SQL log file is 1.008 GiB allocated, with 0.002 GiB reported used. C: had about 543.17 GiB free
at inspection. Folder lengths are not allocated-cluster measurements; OneDrive logical sizes do not
prove local physical residency or completed cloud synchronization. This is not a full-drive inventory.
The first table-size query's row count joined multiple allocation units and overcounted rows; disregard
that field. The second query counts only base heap/clustered partitions; byte totals remain valid.

The latest session has 6,105 evaluations: three policies × 37 symbols × 55 checkpoints. Their input
JSON is 0.271 GiB and result JSON 0.134 GiB, about 415 MiB total before indexes/row overhead. This is
one observed session, not a daily growth forecast. Three active Live portfolios retain 16,607 quote
records; they are only the Live subset, not all paper accounts.

Artifacts contain 25.069 GiB DLLs, 16.263 GiB Linux `.so` libraries and 10.348 GiB macOS `.dylib`
libraries: about 91% of their logical size is those library files. Much of this is repeat build/runtime
material, not trading records. Nightly source/build retention is explicitly manual; September 23's
one-time cleanup installed no recurring policy. The two-backup rule covers regular full backups,
not those five specially named recovery checkpoints. Neither category was deleted or pruned.

Private receipts under `artifacts/read-only-db/`:

- `result-20261022T015446093Z-64d38ce404fb4e4a8336d36a9691aaa0.json`, query
  `8E20FEE3B860D61DAD970FF098F0FC94033AD778167D23CB25C3292697C89526`.
- `result-20261022T015612408Z-2d51966ad12245de8b71c8c46cd3998f.json`, query
  `C9159FC8E5493573BFCC0520120962F83A65F07747B4269D60456F0FCA3A2472`.

## Recommended direction — open for discussion

1. Make fresh market inputs and real-account management independent of optional paper comparison/
   research/report completion. Keep paper-held-position protection running. Replace the whole-cycle
   dependency with reviewed coordination around genuinely conflicting commits; retain one execution
   owner, version/lease checks, lock ordering and recovery. A new process alone cannot remove SQL coupling.
2. Separate compact current account state from historical evidence. Process new events incrementally;
   use verified checkpoints plus later events for restart, and full history for audit/research. Design
   corruption detection, reconstruction, transaction visibility and rollback behavior before changing
   ADR-0082/0098 integrity contracts. Simply skipping validation is not this proposal.
3. Store common market evidence once and reference it from immutable evaluations. Preserve every
   original judgment and its exact inputs. Review research revision growth separately. Conversion,
   retention and deletion are distinct decisions and require their own authority.
4. Adopt explicit retention for reproducible build outputs, plus a separate policy for special
   recovery backups. Preserve source identities, evidence, models, active copies and rollback needs.
   Review Windows-specific runtime packaging so copies need not carry irrelevant platform libraries.

First discussion question: should timely real execution be independent of optional paper/research
completion, while sharing verified inputs and retaining independent protection? Recommendation: yes.
No choice of implementation, retention limits, cleanup, paper-off mode or permission change is accepted.

Acceptance for a later implementation must measure bar-to-real-evaluation age, admission delay,
dispatcher gaps and cycle capacity separately; prove equivalent decisions/history, corrupted-evidence
rejection, concurrent setup/settings changes, crash/restart recovery and long-history scaling. Use
representative retained-history sizes and an explicitly authorized regular-session observation.
No runtime code/tests/builds, database mutations, cleanup, external market/broker calls, app launch,
trading approval/enable, order or push occurred in this investigation.
