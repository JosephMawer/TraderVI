# ADR-0117: Compact trading current-state storage

- **Status:** Accepted for the first disconnected implementation increment; not installed or activated
- **Date:** October 30, 2026
- **Domains:** architecture, data-pipeline
- **Related:** ADR-0103, ADR-0109; [restart design](../reviews/restart-recovery-design-20261030.md), [#19](https://github.com/JosephMawer/TraderVI/issues/19)

## Context

Repeated cumulative snapshots make storage and recovery depend on account age. The immediate task is
a compact persistence boundary; its parent task is ordinary restart without lifetime reconstruction.
The root goal is prompt resumed operation with current instructions and broker facts, preserving
original order identities and protection. Joseph accepted the compact proposal and authorized this
first coding step, with test results for review before further implementation. Live database changes
and engine restarts are explicitly excluded.

## Decision

Implement seven isolated tables and matching scalar records:

| Table | Purpose and ordinary read |
|---|---|
| TradingAccountCurrent | One current account row, current approval reference, buy/risk controls and running financial totals |
| TradingApprovedSettings | Immutable approved definition; resolve only the account's referenced row |
| TradingPositionCurrent | Current quantity, cost and protection for each position cycle; read open positions |
| TradingSymbolCurrent | Current bounded confirmation/re-entry progress; read relevant symbols |
| TradingOrder | Original request/decision identities and cumulative observed/applied fill totals; read only orders needing work |
| TradingRestrictionCurrent | Specific unresolved cause, affected scope/action and retry information; read active causes |
| TradingCheckpoint | Two reusable commit-marker slots per account; first save initializes one, second initializes the other |

All ordinary queries are account-scoped. Filtered indexes isolate active rows. Completed order rows
remain compact receipts, excluded from the ordinary read. They are not automatically deleted.
New approvals, new position cycles, receipts and inactive records can accumulate until separately
reviewed SQL maintenance under [#26](https://github.com/JosephMawer/TraderVI/issues/26), coordinated
with #16. This increment bounds repeated unchanged-book updates, not total lifetime database size.

The repository accepts an explicit open SQL connection factory. No existing engine registration,
broker calls, default production connection or startup migration are added. The account and changed
rows are saved with the next commit marker in one SQL transaction. An account application lock
coordinates readers/writers using this repository, and an expected version rejects stale updates.
Unchanged rows are skipped; a complete no-op makes no marker or historical row. Read batches return
one coherent current account boundary. Missing or inconsistent account/marker state rejects reads
and writes; the store neither fabricates repair nor clears a live recovery hold.

A marker contains version, operation identity/hash, operation kind and UTC time, not a snapshot.
The previous marker identifies the previous committed save; it cannot reconstruct that old account.
An identical retry still represented by either marker returns its saved version without rewriting.
After both markers rotate, the original expected version rejects stale replay. This is a SQL-save
contract, not proof of broker submission idempotency or recovery from an old backup.

Approved settings and original order intent cannot be rewritten. Unique account/request, decision and
broker-order identities prevent alternative rows for the same recorded action. The October 31 writer
change gives the compact store ownership of purchase allowance totals. Callers supply the account's
last saved totals (zero on first creation). For each changed buy the writer looks up the original
receipt and applies the difference between its old and new contributions. It commits the account,
receipt, caller-supplied position changes and checkpoint together. Sales contribute zero.

An open buy counts its original quantity × reference price against the ceiling. A terminal buy counts
its latest cumulative net cost. Within that charge, the applied portion counts as committed and the
balance as reserved. The receipt retains the full applied fill cost even if an open buy exceeds its
estimate. Reconciliation then charges the full terminal cost. For a $100 buy with $38 applied, the
account carries $38 committed and $62 reserved. Cancelling the balance clears the $62 reservation.
An identical cancellation arriving with a new check time preserves the completed receipt and totals.
The offline recovery component maps opening buys and protective sells from original broker
observations and applies cumulative position deltas. Terminal orders leave the work set only after their recorded fill totals
agree and reservations are cleared. Contradictory/regressing facts are rejected for explicit
reconciliation, not silently resubmitted. These are local invariants, not a new broker retry policy.

String sizes and numeric precision are explicit. JSON fields permit bounded scalar configuration
only; nested objects/arrays and duplicate keys are rejected by the repository. No cumulative orders,
snapshots or historical fills are serialized into current rows. Payload budgets measure returned
UTF-8 JSON separately from SQL allocation, indexes, transaction logs and broker responses.

## Validation and limits

The disconnected account rehearsal restores these inputs together from the compact book.

| Saved input | How the offline restart uses it | Production source to qualify before switching |
|---|---|---|
| Account binding, assignment, strategy, approval, phase and execution flag | A new simulated sale requires active execution and a current sell permission. Order-status reads continue when submission is disabled. | Effective broker approval, installed build and valid host grant under the existing execution host |
| Stop-buying flag, risk holds, purchase ceiling and reservations | Preserve each control and reconcile each original buy independently. The pass has no new-buy operation. | Current risk evidence and buying-mode writers |
| Position ID, security, remaining shares, protection owner and saved exit decision/reason/time | Dispatch the already triggered protective exit first. An existing sell reservation or overlapping original request prevents a second sale. | Strategy protection state, original trigger identity and local broker journal |
| Scoped restrictions and dated broker account observations | Read the account snapshot once after exit handling. Record cash and buying power, compare security quantities, and restrict a mismatch to that security. A newer agreement can clear only the comparison's own restriction. | Authenticated account binding, observation ordering, external trades and broker coverage |

Only the pass that successfully reserves a **new** protective request may dispatch it. The receipt and
share reservation commit together before the simulated send. Later passes look up that request.
An ambiguous save before dispatch may leave zero submissions and an unresolved reservation.
Its original ID stays visible for reconciliation. Rejected or partially cancelled exits retain
the trigger on the unsold shares and report the original outcome for review.

Position, receipt, allowance and existing symbol exit memory update together on a sell fill.
The rehearsal uses a simulated transport, synthetic opening terms and current-state reads after changes.
Production admission still requires native single-owner fencing, real grant/build validation, final
authorization through dispatch, broker timeouts, journal coordination and end-to-end measurements.

Offline tests cover interrupted transactions, lost commit replies, stale writers, original identities,
repeated partial fills, current settings/protection/stop values, active-set boundaries and schema/migration
parity. The SQL project compiles with Visual Studio Build Tools/SSDT; the manual migration054 is
prepared only, with original binding prerequisite, no pre-existing compact objects and fresh verified
backup preconditions. Builds and tests do not execute it.

Native SQL locking, rollback/crash behavior, Dapper round trips, query plans, permissions, allocated/log
growth and p95/p99 timings remain unverified. They require a separately authorized isolated rehearsal.
There is no whole-solution health, production restart latency or production database reduction claim.
The implementation checklist owns dated test counts and measurements.

Before activation, map every existing reader/writer and derived policy value to this contract; prove
that original permission and provenance references do not pull legacy graphs into current reads.
Then review local-journal coordination, broker lookup coverage, action-specific eligibility and the
controlled swap as separate increments. Expanded damaged-state/old-backup recovery remains deferred
to #25. This ADR does not supersede installed ADR-0103/0109 recovery or execution contracts: a later
reviewed integration decision must identify the exact superseded boundaries and installation evidence.

## Installed input verification

On October 31 the release directory was identified from the October 30 monitor. Its on-disk engine DLL reports
source revision `1c299f04` and Core reports `4203b456`. The engine DLL also matched the October 29
installation manifest. The Core DLL differs from that older manifest, so verification used the
debug symbols paired to each current DLL by build ID. All 18 selected source documents matched
their compiler checksums exactly or after normalizing line endings and the UTF-8 prefix. This
identifies the relevant installed code on disk. The last execution evidence used here is the
October 30 capture at 14:46 Toronto.

| Rehearsal input | Verified installed source | Remaining bridge |
|---|---|---|
| Authority to resume | The installed reader validated the Windows-protected host grant. Its binding and session matched the saved engine diagnostic. Its build identity matched the installed Core hash. Enable and ongoing restore flags were set at this read. The engine also reads the SQL session and checks its effective approval, exact strategy, continuation/risk association, build review, pause, regular session and connectivity. It rereads the grant before delegating a cycle. | Keep the assignment ID, effective permission request ID and strategy version distinct. Compact execution and sell flags alone cannot prove these checks. Supply current, revocable authority through final dispatch. A current SQL-head match was outside this offline comparison. |
| Position and protection | The saved diagnostic came from a verified managed-session revision. It contained holding protection, and every reported match checked Live position identity and average cost against the holding. The source preserves ownership, carried floor, Live floor, progressive stop, price evidence, processing boundaries and latched reason in position protection and symbol memory. | A diagnostic match does not certify every floor or a healthy current quote. Preserve the strategy-specific state and its evidence. Establishment time, last processed bar and last bid are different from the original trigger time. Before reservation the saved latch alone does not supply a proven `ExitDecisionId` / `ExitTriggeredUtc` pair. Do not invent either during restart. |
| Original order and outcome | The installed public journal reader validated one committed encrypted buy and one sell envelope on isolated copies. Both contained an original intent, transmission status and broker observation. Their request IDs were distinct and both referenced the same broker account. The managed SQL order supplies decision ID, position attribution and original request ID. The journal filename and envelope validate the request ID, and the broker correlation is `order-{RequestId:D}`. | Journal files also contain manual orders. Join each managed intent by exact original request and verify account, side, security and quantity before attribution. These samples prove the file contract, not a complete SQL-to-journal match or broker acknowledgement for all requests. Broker acknowledgement IDs and submission status cannot substitute for a verified fill. |

The installed write order matters during interruption. The workflow first commits the SQL reservation.
The transport then flushes and renames the protected local intent file, sends once and records the
submission result locally. The workflow subsequently saves its SQL transmission observation. A crash
can leave a SQL reservation without a local file, or a flushed file without a known broker outcome.
Both windows need explicit original-ID reconciliation.

Two installed gates currently prevent the rehearsal's independent sale. The managed workflow returns
early for unfinished orders or trial review. The shared preflight separately checks unresolved
orders across the local journal and enumerates both sides for duplicates. Its sell path also uses the
integrity lease and broker sell-context/quote checks. Substituting the compact tables alone would
leave these waits in place. Changing the gates requires a reviewed policy change that preserves
existing manual-order ownership and share reservations.

**October 31 offline qualification.** On the first newly qualified protective sell recommendation,
the managed-session writer saves its decision ID, effective permission ID, reason and UTC decision
time on that position in a separate revision before order reservation. The timestamp records when
the strategy made the decision. The managed order path reuses this ID after a rebound. The
compact input bridge checks the binding, session, permission, strategy, position, owner, remaining
quantity and cost before accepting the saved trigger. A legacy reason without an original receipt
remains ineligible for this bridge. The optional receipt is omitted from historical shapes when absent.

The offline test interrupts immediately after that revision commits, restores it through the managed
archive codec, and uses synthetic protected grant and journal files. Eligibility is checked before
reservation and again before simulated dispatch. At dispatch, the grant lock is shared with the
grant writer's exclusion protocol and the journal owns its normal directory lease. Revocation or a
conflicting manual sale at either boundary prevents the simulated send. An exact request already in
the journal requires original-ID reconciliation. An unaccounted manual fill on the same position
cycle also needs reconciliation even if the manual journal considers the sale complete. A journal
sale on another security leaves the independently supported exit eligible.

The tests cover an interruption following a committed save. A crash before any durable trigger exists
still requires qualified original evidence. Production integration still needs a bounded journal
lookup, native writer/dispatch coordination, full attribution of manual fills and a reviewed cutover.

Source entry points: [engine composition](../../TraderVI.Engine/Program.cs),
[host permission checks](../../Core/Trader/Promotion/BrokerExecutionHost.cs),
[protection diagnostics](../../Core/Runtime/EngineBrokerDiagnostics.cs),
[Live protection](../../Core/Trader/Promotion/BrokerLiveProtection.cs),
[managed order reservation](../../Core/Trader/Promotion/BrokerManagedSessionWorkflow.cs),
[shared broker preflight](../../Core/WSTrade/Orders/BrokerManagedOrderTransport.cs) and
[protected journal](../../Core/WSTrade/Orders/ManualBuyFileJournal.cs).

## Release rehearsal and rollback boundary

In the offline candidate, the compact position rows own the watched holdings. An opening or final
sell fill commits its position and receipt first. The writer then projects the current position
IDs, securities and entry times to the protected journal under the account read lock. Restart
repeats this comparison to repair an interrupted handoff. An unchanged holding set leaves the
watch file untouched. An ambiguous pair of open cycles for one security has no eligible watch.

A delayed broker buy fill can reveal a holding after a manual sale has already been recorded for
that security. The version 2 journal index therefore also keeps unresolved or ambiguous sale IDs
and one latest dated completed fill (time and request ID) per security with fill evidence.
The protected catalog grows with distinct sold securities and outstanding work. Repeated completed
sales on a security replace its latest fill. Ordinary reads with unchanged watches skip the catalog.
Preparing the index from legacy receipts is explicit. A version 1 index must be prepared again
before using the candidate. A completed fill on or after the new entry stays scoped for attribution.

Migrations 054 and 055 ran from their reviewed files on isolated SQL Server 17 LocalDB instances.
Each had its own TraderDB and checksum-verified pre-migration backup. SQL initially read 39,479
pages to return ten unfinished orders at one million finished receipts. The filtered index took
48 logical reads on that book. The current reader now selects the active indexes explicitly.
The measured native fixture and connection-loss results are in the implementation checklist.

The candidate executable accepts a dedicated rehearsal token before constructing the normal host.
The token selects a named LocalDB whose database marker must match. The ordinary installed
`--verify` command can disable the saved grant and is unsuitable for this test.
The comparison uses one saved TFSA revision captured through the installed read-only SQL helper
with receipt hashes verified. Preparation validates the manifest, all shared content and the
revision identity, then checks a copy of the protected host grant against the installed build.
Preparation decodes that single revision once. Subsequent verification processes read only
the copied grant and current SQL rows. They have no broker transport or dispatch workers.

The settled-account fixture compares grant and strategy identity, the buy stop, holdings,
protection owners/floors/stages and progressive state, cash and allowance, risk references/holds,
and the unfinished managed request set. It rejects an unsettled source. A saved observation
cannot establish current Wealthsimple availability or cover a sale made outside the managed
workflow. That initial comparison omitted symbol evaluation progress and kept buying and selling disabled.
Capture and timing files containing private information stay under ignored local artifacts.

### Abrupt-stop candidate

Build the isolated engine with `-p:CompactRestartCandidate=true` and a separate artifacts path.
Its ordinary `--run` entry compiles in the LocalDB and read-only broker composition. This binary
has no installed-host fallback, broker order transport, or live control pipe. The default release
build retains its existing startup policy. The candidate uses an independently supplied synthetic
account observation and regular-session clock.

The current-state writer seals a digest of the pending SQL rows and the exact assignment in one
of two protected files before committing. SQL's current version selects the file on restart.
An interrupted commit can leave a newer unused file. The preceding version still has its own
receipt. The account lock covers the seal and SQL commit. Only a matching local and SQL run
witness, database identity, compact version, operation ID and digest qualify the compact path.
An unclean stop with matching evidence can proceed. A mismatch requires recovery. A saved
incident remains active even if the witness file later reappears.

At preparation, the approved Delphi assignment supplies original opening request IDs, target,
protection owner and progressive policy. A new fill begins with no earned trailing floor. The
writer records one symbol entry on the first applied fill and preserves the progressive state
while updating average cost on later fills. The current symbol rows carry session/bar and input
identities, entry/exit counts or times, confirmation start/expiry, minute policy and the saved
evaluation reference. Fresh signal evaluation and the ordinary live strategy's ongoing publication
of these rows still require integration before enabling broker dispatch.

For the process-level check, stage a synthetic account with `PrepareCompactCandidateProcessBoundary`
in a newly marked rehearsal database. Run [the interruption check](../../Operations/Test-InterruptedCompactCandidate.ps1)
with that token. It kills its own child between sealing a protective-sale reservation and SQL commit,
restarts with the unanswered buy, repeats the restart, then removes the isolated local witness.
The old managed-session archive is absent throughout these launches.

**Proposed controlled release**

1. Complete the missing symbol, strategy and journal mappings and exercise a real startup pass
   with a read-only broker adapter and dispatch disabled. Compare ready/held decisions,
   including an unfinished buy and an unrelated protective sale. Time the pass through broker reconciliation.
2. Before a separately authorized production installation, record the current build, settings,
   grant and journal boundary, keep the old binary, and take and verify the required fresh backup.
   Apply each additive migration manually. Keep legacy records available throughout the swap.
3. At the agreed switch, drain the old writer, fix the comparison boundary, verify identical
   account and unfinished-request ownership, and obtain approval for the exact new build before
   allowing its one order writer to start.
4. If comparison fails before dispatch, keep the old writer authoritative and restart it with
   its own verified grant. After any possible new dispatch, first stop new submissions, look up
   original request IDs at the broker and carry every new receipt/reservation back to the
   fallback writer. Resume only after quantities, cash, protection and request ownership agree.
   Restoring a pre-switch database alone could repeat an order already received by Wealthsimple.

## Alternatives and consequences

### November 1 release boundary and operator approval

Joseph selected the compact restart for the first release. Delphi Live remains the trader.
Its ordinary strategy evaluation temporarily continues the original account journal and broker
history checks. The operator subsequently clarified that reducing those regular reads and
cumulative writes remained a separate proposal. Keep production data and fix forward if the
installed restart encounters a fault. See the [November 2 investigation](../reviews/wealthsimple-auth-and-engine-diagnosis-20261102.md).

For this build the operator reviews the scoped restart permission in the desktop application.
The receipt binds the terms, original permission, previous strategy revision and implementation.
An older build approval cannot silently acquire those terms. After approval, an uncertain buy
retains its reservation. A verified partial fill is counted once. A different position may use its
original protective decision while the buy is unresolved. This supersedes the full-fill intervention
rule in [ADR-0109](0109-explicit-ongoing-delphi-live-management.md) only for the reviewed scope.
Daily risk boundaries, outside-account changes, the purchase ceiling and revocation remain effective.

During the transition the normal strategy writer publishes changed compact rows and its current
revision in the same SQL transaction. The protected receipt is written to the alternate slot
before commit. A restart verifies the selected slot and source revision. The engine and dashboard
must run the reviewed package. An unexpected source writer or receipt mismatch requires investigation.
The initial transfer requires a settled account and the exact enabled permission. A second transfer
cannot overwrite the first. The production schema was installed November 1. A successful migration
does not mark the account transfer or engine activation complete.

The first regular-trading broker read change keeps fresh verification of historical order
details. The activity fingerprint includes status and quantity but cannot establish that an
older fee or net fill value is unchanged. The offline candidate groups discovered order IDs
in one read and checks each result against the original ID, account, owner, security and side.
An incomplete or conflicting group cannot produce a successful history. Manual reconciliation,
original-order recovery and the installed read-only helper retain their individual reads.
The November 1 read-only comparison verified broker support and agreement for the observed
account. The trading engine still uses individual reads. See the
[execution checklist](../strategy-alignment-implementation-checklist.md) for the offline and live measurements.

### November 3 clarification — original protection ownership across builds

An implementation review changes the strategy version ID even when the financial permission,
strategy definition and source stay the same. A position may still carry the version that
protected its original purchase. During first transfer or a build review, restore a missing
position-specific owner only when the opening managed buy, reconciled broker fill, position
cycle and saved protection agree. Read the original immutable reservation under that binding
and verify the financial permission, source, strategy definition and original owner. Missing
or ambiguous evidence leaves the position ineligible. A conflicting explicit mapping blocks
the review.

Write the verified owner into the new settings. Preserve the old settings, original trigger,
position and order history. The ordinary sell path uses the same request ID derived from the
original decision, reserves once, and only looks up that original on a retry. Approval and
separate host enable still apply. No purchase-history scan is added to normal trading cycles.

### November 2 implementation — managed protection after a closed sell

When compact recovery has closed a position still present in the managed account, verify the
original sell and its remaining broker inventory under the existing account lease before running
managed protection. A pending order on a different, identified security can coexist with this
inventory check. A pending order on the sold security, unknown scope or conflicting fill keeps the
handoff incomplete. Ordinary manual sell checks retain their account-wide pending-order rules.

The managed revision records the confirmed cumulative fill once and removes the sold cycle from
its protection inputs. The last complete import and risk evidence keep their original identities.
The scoped report can use recent compact cash for valuation when the remaining position books agree.
Purchase cash, NAV and account-wide pending count require the next ordinary complete synchronization.
The compact writer compares the terminal receipt inside the managed transaction and advances the
checkpoint with its existing financial rows. This preserves the original symbol exit memory and
prevents a second application of the fill. The [offline evidence and release dependency](../reviews/wealthsimple-auth-and-engine-diagnosis-20261102.md#offline-implementation-and-retry-results)
are separate from the November 1 installation.

For the first upgrade of an already compact account, Joseph selected an immutable settings
successor. Only a validated dashboard `ImplementationReviewed` revision can create it. The build
review ID keys the new row. The original financial permission remains bound to the same assignment,
baseline, ceiling and source. Each new compact order references its actual settings row. Earlier
orders keep their original references. The approval transaction advances the account pointer and
checkpoint together. Existing position records retain their owners, floors and latched exits.
An eligible carried owner is recorded by exact position and owner. The writer rejects any change
to the eligibility of a remaining holding. The checkpoint describes the expected successor in its
disabled state until the operator separately enables an exact matching host grant.

Retrofitting the cumulative session blobs would retain history-dependent reads and obscure field
ownership. Reusing two full snapshots would still copy the active book on every change. Separate scalar
rows and two markers make growth and changed-row costs measurable, at the cost of an explicit future
mapping and native SQL qualification. Keeping every checkpoint or applying cleanup in the engine was
rejected; SQL job design and retention remain separate reviewed work.
