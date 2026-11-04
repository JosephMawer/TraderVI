# ADR-0127: Paper checkpoint restart and incremental verification

- **Date:** November 4, 2026
- **Status:** Approved, implemented and offline validated; not installed
- **Domains:** architecture, data-pipeline
- **Amends:** [ADR-0103](0103-real-execution-independent-of-paper-research.md), [ADR-0104](0104-scoped-recovery-and-sequential-history-audits.md) and [ADR-0116](0116-temporary-paper-continuation-during-reboot-recovery.md), for Paper restart, operational qualification and automatic historical verification only
- **Related:** [issue #28](https://github.com/JosephMawer/TraderVI/issues/28), [ADR-0117](0117-compact-trading-current-storage.md)

## Decision and authority

The immediate problem is Paper recovery requiring lifetime verification. The parent problem is coupling
current simulation to historical research coverage. The goal is reliable short-term simulation with
truthful evidence and independent Real trading. Joseph reviewed and approved the six-part scope in
the Paper checkpoint review chat: adopt tonight's completed recovery, reuse current-state saves,
qualify restart without lifetime replay, verify new history overnight, investigate failures at their
affected scope, and separate Paper health from historical coverage and Real health.

This authorizes implementation and offline validation. Installation, production database writes,
engine restart and financial operations require separate approval. Retention/deletion and measuring
database size remain deferred. Issue #24's symbol correspondence is separate. The recovery monitor
and clearance remained with their original chat and have completed.

## Existing verified baseline

Use recovery `28f8f730-76ce-471f-94d2-cde1904cef0d`, completed November 3 at 23:42:09 Toronto with
212,508 verified units. Guarded Paper clearance passed at 23:57:44; subsequent readback showed zero
unresolved incidents. [Completion evidence](../reviews/paper-recovery-bottleneck-20261103.md#completion-and-scoped-clearance).
These are recorded operational facts, not fresh checks performed by the implementation chat.

An explicit offline preparation command must reread that successful receipt, its database identity,
clearance and current guards. No Paper/global operational finding may remain. Each current checkpoint
must match its original revision and observation membership. When the head is still the verified head,
adoption does not hydrate the archive. If older installed writers advanced afterward, preparation
hydrates the single preceding snapshot and verifies only the intervening revisions before adopting
the resulting current boundary. This one-time legacy bridge can read cumulative snapshot content;
it does not replay earlier verified revisions and has no measured native latency guarantee.
No old root, audit outcome or incident is rewritten. Preparation cannot reset an existing adoption.

## Storage and trust

Keep `EnginePaperWorkingStore` and the existing atomic commit of current state, history, events,
observation projection and replay identities. Save the audit's compact working boundary in one
`EngineCurrentState` row per portfolio (`PaperVerifiedBoundary`), with one `PaperVerificationPolicy`
row identifying the recovery/database and enrolled owners (extended atomically when new portfolios
are first verified). These are verification receipts, not
another authoritative account store. No schema migration or per-revision working-state archive is
required. New full/admin saves publish existing version-3 collection commitments, like ordinary
working appends; original version-2/3 records are preserved.

Trust relies on the explicitly verified baseline, matching SQL/local continuity, database identity,
validated atomic writers, original revision agreement and immutable guards. Startup rereads current
checkpoint/content hashes, identities, cash consistency and observation membership. It rejects
missing/regressed/disagreeing boundaries. A matching interrupted run can qualify Paper current state;
an interruption alone does not initiate an archive scan. Missing continuity or changed database
identity requires investigation and does not receive the current-state exception.

These checks do not prove that guards were never bypassed between observations. A privileged edit,
restore or known loss of continuity invalidates the affected trust assumption. Without periodic
rereading, silent changes to untouched old bytes may remain undetected until read. Independent
storage/backup checks and retention policy remain separately reviewed work; no cadence is selected here.

## Restart and pending work

Load the committed current checkpoint. A save whose acknowledgement was lost is already represented
there; original fill/action/evaluation identities prevent replay. Preserve pending sells, protection
and original quote-attempt progress. Capture pending buy IDs during qualification and expire those
original buys through the existing `BuyRestartExpired` transition before ordinary Paper cycles.
Retries of that cleanup are harmless; new actions created after startup are not expired by it.
The separate consumer receipt can be missing after portfolio work committed. Existing per-portfolio
completed-cycle markers and action IDs determine what remains, never receipt absence alone.

Current-state qualification may supersede the operational effect of a recognized startup Paper
finding or this qualifier's previous failure, after the exact current checks pass. It preserves the
incident and does not clear historical or dependent Research uncertainty. Unknown findings, current
continuity loss and active verification-pending restrictions are not exempted. New/changed incidents
invalidate the process-local exception for their affected scope. Invalid individual checkpoints are
held by portfolio; other qualified portfolios and their shared-input worker may continue. Shared
database/continuity failures still affect all Paper work. Real permission, readiness and recovery are unchanged.

## Overnight verification

A separate Paper `NewHistory` plan starts at each saved verified revision plus one. For each new
version-3 revision, read the header, changed items, their referenced content and the preceding compact
working boundary. Validate the financial/settings transition and original new-record identities,
recompute collection commitments, and compare the exact expected root. New references are checked
even if their content is older. Persist the new compact boundary and audit progress in the same SQL
transaction. An interrupted save retains either the old pair or the new pair; retry cannot skip work.

No new data means no semantic history replay. A resumed audit uses its saved working boundary and
fixed plan. Budgets remain 4,096 steps/five seconds, checked between records, with the existing worker
yield. This bounds admission, not the duration of one SQL read or large revision. Startup/audit timing
must distinguish synthetic measurements from native SQL and contention measurements.

Automatic weekly Archive plans cover Real/shared history only. Earlier combined scheduled runs remain
visible and incomplete, but are not automatically resumed over Paper. Existing explicitly requested
Recovery plans retain their fixed boundaries and verifier; no new full Paper pass is started by restart,
elapsed time or a failed incremental check. Full Paper verification requires an explicit separately
reviewed reason and operator request. Findings identify the affected portfolio/revision and require
targeted investigation first. A change to an already archived record outside the retained working
window is exceptional, not silently accepted as a normal append.

## Presentation and acceptance

Show current Paper qualification and incremental history coverage separately. Retain failed/incomplete
audits and original findings; current-state qualification never marks them successful or qualifies
dependent performance. A Paper-only warning does not imply that Real trading is held. Shared/Real
restrictions remain prominent and effective.

Offline acceptance covers clean/cold restart, lost acknowledgement at action/fill/cycle boundaries,
duplicate prevention and expiry, changed/missing/regressed identity/checkpoint evidence, changed or
missing new content, continuity and scoped permissions, incremental resume and scope scheduling,
and equivalent working sets with small/large archives. Native SQL atomicity, operational adoption,
installed startup latency and market-hour Paper behavior remain separate installation acceptance.

November 4 offline evidence: 3,655 full-suite passes/16 native skips, then 84 focused passes after
adding workflow-wide delta checks and failed-owner presentation coverage. The isolated Debug WPF
build also built the engine successfully. Existing compiler warnings remain; dependency-security
auditing was disabled. The SQL project/full solution was not built; no schema definition changed.
In a synthetic in-memory test, four cold restores plus incremental checks took 20.1 ms for two closed
trades and 27.7 ms for 10,000, with four content records in either case. These are illustrative single-run
measurements, not SQL latency or an installed service guarantee. The original archive remains intact.
Evidence logs and pending native acceptance are in the [owning checklist](../strategy-alignment-implementation-checklist.md).
