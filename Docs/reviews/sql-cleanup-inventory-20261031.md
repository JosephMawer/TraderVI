# SQL cleanup inventory and review proposal — October 31, 2026

Review only. No deletion, job installation, service change, database write, host restart or broker
operation performed. [#26](https://github.com/JosephMawer/TraderVI/issues/26) owns database maintenance;
[#16](https://github.com/JosephMawer/TraderVI/issues/16) owns retention policy. This report feeds that
policy; it does not settle retention periods, backup disposition or disk budgets.

The immediate problem is accumulating database records. Its parent problem is recurring storage
and maintenance cost; the root goal is reliable trading with preserved instructions, protection,
order identities and recoverability. Cleanup remains independent of #19's fast-restart increment.

## Measured inventory

Approved read-only helper v2, October 31 at approximately 21:55 Toronto (November 1 01:55 UTC).
Three successful receipts had their query SHA-256 checked against the submitted UTF-8 mailbox.
The checkout was clean at entry. No operational application was launched.

| Table | Rows | Reserved MiB | Used MiB |
|---|---:|---:|---:|
| DelphiLiveEvaluation | 167,268 | 13,857.2 | 13,855.1 |
| DelphiLiveResearchOutcomeRevision | 196,674 | 3,425.1 | 3,424.2 |
| MarketCollectorRequest | 36,380 | 2,798.4 | 2,798.1 |
| DelphiLiveLedgerEvent | 45,240 | 1,309.3 | 1,308.9 |
| BrokerManagedSessionRevision | 6,836 | 875.5 | 875.2 |
| EngineInputPublication | 20,123 | 850.4 | 850.0 |
| DelphiLivePortfolioRevision | 45,240 | 729.7 | 727.7 |
| BrokerAccountImport | 8,287 | 565.2 | 564.5 |
| EngineEvidenceContent | 146,483 | 427.5 | 427.2 |
| IntradayCollectionSlot | 108,311 | 370.8 | 369.8 |
| BrokerTradeSync | 8,284 | 304.2 | 303.5 |
| EngineStrategyAssignment | 15 | 294.9 | 294.8 |
| IntradayCollectionReceipt | 74,070 | 188.2 | 187.9 |
| CalibrationCandidate | 14,157 | 113.9 | 113.4 |
| DelphiLiveSnapshotContent | 51,992 | 101.8 | 101.5 |
| IntradayPollObservation | 115,890 | 100.7 | 99.8 |

MiB means 1,048,576 bytes. Allocation figures include indexes and large-value storage, measured
from sys.allocation_units. Row figures come from sys.partitions base indexes only; direct COUNT_BIG
also confirmed evaluation, outcome, request, broker revision, publication and evidence counts.
The initial exploratory query multiplied metadata row counts across allocation units; those counts
were discarded and replaced with the separate base-index query. Allocation totals were unaffected.
These sequential reads are not one transactionally consistent snapshot.

The data file is 26,880 MiB, with FILEPROPERTY SpaceUsed reporting 26,825.1 MiB allocated
(approximately 54.9 MiB unallocated). The log file is 1,032 MiB. Its SpaceUsed result is not a
measurement of active transaction-log utilization. No disk-volume free-space measurement was made.

Observed rows timestamped October 30 UTC: collector requests 14,095; evidence content 36,902;
publications 5,524; broker revisions 1,266. These are one day's recorded inserts, not net growth,
a Toronto-session boundary, or measured bytes/day. Applying current average used bytes per row
would estimate respectively 1,084, 108, 233 and 162 MiB/day; changing payload sizes make that a
planning estimate only. Legacy evaluations last recorded October 21; research outcomes October 16;
neither has October 30 inserts. Large legacy storage and active growth are different priorities.

## Writers, consumers and eligibility proof

| Candidate family | Writes it | Still reads it / actual purpose | Removal proof required |
|---|---|---|---|
| Legacy evaluations | DelphiLiveEvaluationRepository; live retired-writer trigger now present | EngineInputHistory unions legacy/shared inputs; experiment diagnostics, AI decisions and EngineHistoryVerifier use historical evidence | Retire/revise those consumers for an explicitly approved interval; preserve current confirmation and exact evidence references. Retired writes alone do not prove unused reads. |
| Research outcome revisions | DelphiLiveExperimentRepository.AppendOutcomeAsync | ReadLatestOutcomesAsync selects newest per slot; DelphiLiveResearchCoordinator uses it for research continuation and scorecards | Keep latest per slot; identify any retained revision IDs in saved reports/experiments and diagnostic evidence before removing older revisions. Historical reproducibility policy belongs to #16. |
| Collector requests | MarketCollectorRepository.SaveAsync, after attempt/lease checks | Save checks prior RequestId/hash for idempotency (safe repeated submission); observations/conflicts reference requests; collector ReadAsync uses observations and conflicts for recovered minute books | No retained child or embedded reference, no retry/reconciliation obligation, and approved diagnostic window. No relational child is only a preliminary filter. |
| Paper portfolio revisions and ledger events | DelphiLiveLedgerRepository, settings/comparison/experiment repositories | Current portfolio reconstruction, EnginePaperAuditReader adjacent history, experiment diagnostics and attribution | Preserve current roots, required membership/content, retained assignment and experiment evidence; revise audit/reconstruction contracts first. Events have an FK to revisions. Do not apply Real's two-marker target to Paper history. |
| Broker session revisions | BrokerManagedSessionRepository | Latest state, original approval/risk/reference evidence and EngineHistoryVerifier adjacent revisions | #19 replacement installed and qualified, legacy writers/readers retired, original unresolved order identities migrated, rollback bridge and retained receipts proven. Self-referencing previous-revision FK prevents simply deleting all but two. |
| Shared publications/content | EngineMarketCycleRepository, EngineEvidenceStorage and archive writers | Real observation/confirmation, Paper/Research consumption, input history, archive reconstruction and integrity verification | Mark all retained roots, then recursively retain their content hashes. EngineArchiveStorage follows nested manifests; EngineMarketCycle.ContextHash FK is only one edge. JSON references are not all FKs. No direct-FK-only orphan deletion. |
| Account imports/trade sync | BrokerAccountImportRepository and BrokerTradeSyncStorage | Current reviewed financial state, exact receipts, risk baseline/continuation and historical broker reconciliation | Preserve original baseline/anchor imports, all referenced receipts and self-linked chains until replacement contract proves independence; age is insufficient. |
| Strategy assignments | EngineSettingsRepository | Current target selection, version lookup, Paper revision attribution and EngineHistoryVerifier; PriorStateJson preserves transition evidence | Keep current assignment/version/settings and all referenced historical assignments; assess prior-state payload separately after rollback policy. Fifteen rows consume 294.8 MiB: row-count limits alone are inadequate. |
| Intraday slots/receipts/observations, snapshot content, calibration | Collection/evidence repositories, snapshot codecs and calibration pipeline | Causal evidence, recovered confirmation, Paper reconstruction, frozen candidate lenses and calibration comparisons | Retained-root dependency closure, current indicator horizons, research acceptance and model provenance must survive. Lower storage priority; no blanket market-history expiry proposed. |

Source anchors: [evaluation repository](../../Core/Db/DelphiLiveEvaluationRepository.cs),
[research repository](../../Core/Db/DelphiLiveExperimentRepository.cs),
[collector repository](../../Core/Db/MarketCollectorRepository.cs),
[history verifier](../../Core/Db/EngineHistoryVerifier.cs),
[archive traversal](../../Core/Db/EngineArchiveStorage.cs),
[Paper decoder](../../Core/Db/EnginePaperAuditReader.cs),
[broker repository](../../Core/Db/BrokerManagedSessionRepository.cs).
The table above is a source-based dependency inventory, not a complete proof of every JSON reference.

## Dry-run pools and estimated benefit

No rows are approved or fully proven eligible for deletion today. Counts below identify review
pools, without pretending a retention policy or complete reachability proof already exists.

| Pool predicate actually counted | Measured pool rows | Estimated used-space share | Proposed initial batch | What blocks removal |
|---|---:|---:|---:|---|
| Outcome has a newer CalculatedUtc for the same SlotId | 117,124 | 2,039.2 MiB | 100 rows | #16 historical revision policy and saved-reference audit; preserve 79,550 latest outcomes |
| Request has no MarketCollectorObservation or MarketCollectorConflict child | 13,274 | 1,020.9 MiB | 100 rows | Retry/hash receipt use, embedded references and diagnostic policy; immutable trigger rejects deletion |
| Broker revision ROW_NUMBER by BindingId, Revision descending exceeds two | 6,834 | 874.9 MiB | 25 rows, only after transition | #19 installed/qualified first, chain FKs, immutable guard, exact receipt/rollback migration. These are snapshots, not compact checkpoints |
| All legacy evaluations, as an investigation ceiling only | 167,268 | 13,855.1 MiB whole table | 25 rows after dependency retirement | Live historical consumers and immutable archive trigger; eligible subset unknown |
| Paper revisions/events; publications/content; imports/sync; assignments | See inventory | Not estimated for a deletion subset | 25 large roots / 100 small children | Retained-root graph, policy and consumer changes unresolved |
| Compact TradingCheckpoint excess | Not applicable: tables absent live | 0 currently attributable | No cleanup needed for two-slot design | ADR-0117 disconnected implementation; do not infer installation |

Partial-pool savings use used MiB × pool rows / table rows, assuming uniform storage. They are
estimates, not measured candidate payloads or guaranteed freed pages, and must not be added as a
promised reclamation total. Remaining references can reduce any pool to zero. No candidate payload
scan of the 13.5 GiB legacy table was needed. Reclaimed database pages become reusable; immediate
operating-system file savings from ordinary deletes are expected to be zero. No shrink is proposed.
Backup-size reduction is unmeasured and backup expiry remains separate under #16/#12 and ADR-0082.

## Proposed SQL Agent arrangement

Recommend **one job with ordered, separately reported steps**, initially disabled. The measured
server is SQL Server 2019 Developer Edition 15.0.2000.5; Windows service SQLSERVERAGENT exists,
Stopped, Manual. Scheduling capability exists but is not running. No msdb job inventory, Agent
identity/permissions or scheduling health was inspected through the dbo-only helper; qualification
and any service enablement require separately reviewed installation. Do not substitute an app worker.

One job gives one concurrency owner and enforces root-before-content dependency order. Multiple
concurrent jobs would complicate shared evidence protection with no demonstrated benefit yet.
Keep independent family budgets and metrics; reconsider separate jobs only if measured runtime or
distinct approved cadence warrants it. Integrity auditing and backup retention stay separate.

Proposed order: preflight and dry-run counts; eligible research revisions; eligible collector
receipts; qualified legacy transition cleanup (disabled until #19 acceptance); unreferenced content
last; final backlog/metrics. A safety preflight failure stops the entire job. A family failure
stops subsequent deletion, especially shared content, records failure and preserves committed batches;
the next scheduled run may resume by key after revalidation. No unbounded immediate retry loop.

Propose daily 22:00 Toronto, outside trading, subject to collector/nightly/backup scheduling review;
skip conflicting maintenance or active recovery/audit passes. Start with a 5-minute whole-job budget,
60 seconds per family, 1-second lock timeout, 5-second statement timeout and low deadlock priority.
Use batches capped by both rows above and an initial estimated 8 MiB payload budget; one oversized
row is skipped and reported. Allow a short pause between transactions. These are proposed limits,
not performance-tested safe production values. Log-write, blocking and free-space thresholds need
capacity qualification before enabling deletes; pause if those metrics cannot be obtained.

Recheck eligibility inside each short transaction using indexed keys and a reviewed shared locking
protocol with every relevant writer. A job-only application lock cannot prevent a writer from adding
a reference. Never scan then delete an old unprotected candidate list. Current immutable triggers
must remain enforced until an individually reviewed maintenance contract replaces their deletion
boundary; disabling guards is not an implementation shortcut. Fail closed on missing roots, unknown
manifest version, changed current version, incomplete graph or an unresolved dependency.

Persist run/step start/end, policy version and cutoff, considered/blocked/deleted counts, estimated
bytes, actual allocation delta, batches, oldest eligible backlog, last success, failure and skip
reason. Distinguish a skipped run from successful cleanup. Alert on failure and increasing backlog.
Only a later reviewed package will define the maintenance schema, permissions and exact delete SQL.

## Preservation and restart dependency

Always retain unresolved orders regardless of age, including ambiguous submission, pending cancel,
partial fill, unapplied fill totals, outstanding reservations and original request/broker/decision
IDs. A terminal label alone is insufficient until fills and financial/protection effects are applied
and all receipt consumers are satisfied. Preserve current strategy/version/approval, risk holds,
restrictions, position quantities/costs, raised protection floors/highs, triggered exits, settings
and current confirmation state. No retention window overrides these roots.

Retain referenced completion receipts, original financial baselines and permission anchors, nested
content and the records needed by retained research/diagnostic outputs. Rollback requires a verified
restorable backup and an explicit bridge preserving orders and state changes after cutover; restoring
a stale backup over later trades is not rollback safety. Existing specially retained backups remain
protected until their disposition is explicitly accepted. No backup was created or restore tested here.

Legacy Real revision cleanup is gated on #19 installation, native read/write/restart qualification,
retired accumulating writers, and rollback/receipt proof. Paper cleanup needs its own decoder/audit
contract review even after #19. Research supersession and collector orphan investigation can advance
without #19, but their policy/reference/guard requirements still apply.

Current-plus-previous is the accepted checkpoint target. ADR-0117 uses two reusable marker slots,
not append-only snapshots, so that design has zero checkpoint excess when installed correctly.
Markers cannot restore an old account state. Before installation there is no two-row cap on the
legacy store: 1,266 broker revisions were recorded in the observed UTC day. A daily job provides no
hard maximum backlog if arrival rate is unbounded or maintenance fails. Do not claim otherwise;
require writer-side bounded markers and measured service capacity, and alert on the first missed
run. No maximum safe cleanup outage can yet be inferred from a single-day row count and 54.9 MiB
unallocated inside the data file; autogrowth/disk headroom remain unqualified.

## Review boundary and evidence

Review the proposed job topology/budgets and #16's family-specific retention rules before writing
an executable deletion package. Next proof work is a complete retained-root manifest, request retry
horizon, exact research reference checks, allocation/log capacity and disconnected concurrency tests.
No new retention period is accepted by this report. No ADR is added for this unaccepted proposal.

Private aggregate receipts under ignored artifacts/read-only-db/:

- result-20261101T015500880Z-ab56a1152b1744edadf41764b17fdbe0.json;
  query SHA-256 9B1241861A4C9B315B3CC7423B122FABABDE5B95F3AC4C9DB78ECA23A64E6DB2.
- result-20261101T015520087Z-d7431dacf4154ab28e23107caa8d73c7.json;
  query SHA-256 21AE3990B43F286C0E44D57494D7F43D61733B2090CE7FFB6AC0E07349FC9E27.
- result-20261101T015539742Z-9069a714e25e4fdc8d4acd653163f3d9.json;
  query SHA-256 833E6C7F6A253DDA3D14AEAA5F84046D51AC17422D1C23727E743C31082D26FB.

Only aggregate storage/count findings are recorded here; no account payloads or model artifacts.

## November 1 follow-up on backup rotation and database growth

### Why eight backup files remained

At 00:56 Toronto, staging and the local OneDrive folder each contained eight matching filenames
and lengths, totaling 19.48 GiB per folder. Five specially named `COPYONLY` recovery checkpoints
accounted for 7.49 GiB per folder. They stay outside the two-generation rotation. The other three
were regular `FULL` files dated October 30, October 31 and November 1.

- The October 30 and 31 Hermes logs record deletions in both locations. On October 31 Hermes removed
  the remaining October 29 full backup from each folder and completed a 4.07 GiB backup.
- The November 1 00:26 full backup protected migrations 054 and 055. The standalone SQL backup
  procedure uses the same filename pattern and skips rotation. The installation receipts identify
  the exact file, SQL checksum verification, matching copy hash and local OneDrive in-sync state.
- The scheduled 00:30 run retried after source changed during capture. Windows Task Scheduler
  event 201 records the action ending at 00:31:24 with `0xC000013A` (`STATUS_CONTROL_C_EXIT`).
  The runner still reported Running, but its exclusive lock was free and the scheduler showed Ready
  and enabled. The run ended before Hermes. The records checked do not identify who or what interrupted it.
  The next successful Hermes backup and copy should rotate the registered regular files back to two
  per location. That subsequent rotation remains to be verified.

Sources include the [backup procedure](../database-operations.md#onedrive-copy-and-retention),
[retention implementation](../../Core/Db/TraderDbBackupRetention.cs) and
[standalone SQL script](../../TraderDB/Operations/Backup-TraderDB.sql). The local nightly logs are
under `%LOCALAPPDATA%\TraderVI\Nightly\logs`. The manual backup verification and sync receipts are
under `artifacts/restart-release/live-backup*-20261101.*`.

### What grew

| Measurement | Earlier | Later | Change |
|---|---:|---:|---:|
| Compressed full backup, October 13 to November 1 | 2.22 GiB | 4.07 GiB | +83.6% |
| Compressed full backup, October 18 pre-migration to November 1 | 2.81 GiB | 4.07 GiB | +44.8% |
| Compressed full backup, October 31 to November 1 | 4.0653 GiB | 4.0696 GiB | +4.38 MiB (+0.11%) |
| SQL data-file pages in use, October 21 evening to November 1 | 21.217 GiB | 26.197 GiB | +4.98 GiB |

The November 1 SQL backup log reports 26,827 MiB before compression and 4,167 MiB after.
At 00:56 the data file had 54.75 MiB free within its 26.25 GiB allocation. Windows reported
about 427 GiB free on C: during inspection. The log file was 1.008 GiB allocated both on
October 21 and November 1. [October 21 storage baseline](paper-workload-architecture-20261021.md#measured-evidence-and-limits)

**Earlier October growth.** The legacy evaluation and research-outcome tables occupied 16.87 GiB
combined on both October 21 and November 1 (64% of current used data-file space). From October 13
through 21 UTC, the retained rows contain 3.79 GiB of evaluation inputs/results and 0.38 GiB of
research outcomes. The old evaluator repeatedly saved price-bar histories for the stock and benchmark
with each policy decision. The last recorded evaluation was October 21 and the last research outcome
was October 16. These logical payload sizes exclude indexes and cannot be apportioned directly
to compressed backup bytes.

**Late October growth.** Three tables whose retained records start after the October 21 measurement
now use 3.98 GiB, about 80% of the 4.98 GiB increase. Collector request receipts use 2.73 GiB,
published engine inputs/results 0.83 GiB and shared engine evidence 0.42 GiB. Ordinary daily
price bars use 87 MiB in total.

The collector requests minute bars from the session open on each poll and persists both the raw
response and the parsed bars. Successive receipts repeat much of the same history. Across October
28–30, 36,380 receipts held 2.44 GiB of JSON. By UTC day the payloads were 846, 746 and 907 MiB.
The mean receipt grew from 14.3 KiB for first-hour polls to 121.6 KiB for last-hour polls.
13,274 receipts had no linked new observation or conflict and still held 608 MiB of JSON. Those
receipts also support retries and audits. Their byte count alone does not establish deletion eligibility.
[Collector request construction](../../Core/Runtime/TmxCollectorSource.cs) and
[receipt persistence](../../Core/Db/MarketCollectorRepository.cs)

**First engineering target.** A read-only SQL experiment losslessly compressed the exact stored
JSON for 96 receipts selected in request-ID order. Payload size fell from 6.71 MiB to 0.37 MiB
(94.5%). All 96 decompressed byte lengths and SHA-256 hashes matched. This was a payload sample.
The effect on an already-compressed backup, production read/write latency and actual allocated pages
remains unmeasured. A disconnected storage rehearsal can test those costs while preserving the
original receipt and retry identity before any decision about production storage or expiry.

Five read-only helper receipts passed query-hash verification. The submitted SQL is under
`artifacts/backup-growth-20261101/`. Under `artifacts/read-only-db/` the result files use the prefix
`result-20261101T` and UTC times `045655131Z` (allocation), `045800563Z` (recent writers),
`045823195Z` (legacy writers), `045920780Z` (session windows) and `050110489Z` (compression sample).
The investigation performed no database writes, deletions or operational rerun.
