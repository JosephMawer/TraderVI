# ADR-0102: Bounded immutable Live evaluation-save recovery

**Date:** October 21, 2026. **Status:** Accepted within the operator's explicit SQL reliability fix request.
**Domains:** architecture, data-pipeline, risk-management.
**Related:** ADR-0053, ADR-0082, ADR-0086, ADR-0098–0101.

The immediate problem is a transient evaluation-save failure interrupting the monitor and restarting
signal continuity. The parent problem is distinguishing storage failures from expensive paper history
processing and genuine late/missing evidence. The root goal is reliable execution with unchanged risk,
source, collection and dated approval controls. The operator explicitly chooses **Fix SQL only for now**;
no paper-workload switch, scheduling change or simulation shutdown is part of this increment.

## Decision

Permit one retry of the same immutable evaluation-save request after 250 ms, on a newly acquired
connection after the failed connection is disposed. Serialize input/result once. Preserve evaluation
ID, session/policy, bar/evaluation times, epoch, lease/owner/fence, validity and evidence IDs exactly.
Re-read the immutable stored policy, policy assignment and observed SQL clock; execute the original
transactional lease/policy/epoch/causal/on-time-evidence/checkpoint checks on every attempt. Existing
same-checkpoint content validation handles an acknowledgement lost after commit. No new evaluation,
reconstructed market evidence, reference reset or continued authority follows from a failed save.

Only SQL error lists containing -2 (timeout), 1205 (deadlock) or 1222 (lock timeout), with no other
numbers except companion 3621, qualify. Retry at most once. Mixed, permission/login/network/schema,
identity, policy, lease, evidence, deadline and custom application-lock failures do not qualify.
Cancellation before a call or during backoff prevents replay. A terminal failure retains the
existing monitor interruption/lease-release/continuity-recovery behavior. Orders never use this helper.
Do not raise command/lock timeouts, widen freshness/deadlines, suppress guards or change any risk rule.

Add fixed informational SQL stage messages and client stages for opening/policy/clock/assignment.
The diagnostic records the last observed fixed stage, client elapsed times, attempt number, exception
type, SQL numbers and whether identical replay is scheduled. It does not claim a server wait/plan
measurement. Unknown server messages are ignored; error events remain errors. Microsoft documents
[severity 10 and below as informational InfoMessage events](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconnection.infomessage?view=sqlclient-dotnet-core-6.1).
No exception text, provider payload, SQL text, symbols, account IDs/amounts or credentials are logged.

Diagnostic I/O runs on one background worker with a 64-entry queue and a 16 Ki-character entry cap;
each of the two diagnostic files rotates at 512 KiB with one previous file. Drop excess/failed
diagnostics rather than blocking recovery or replacing the original exception. This is best-effort
evidence, not an order acknowledgement, durable financial journal or proof that a SQL save succeeded.

## Evidence and boundaries

Read-only metadata confirms enabled expected indexes, without identifying the historical SQL wait
or execution plan; the installed helper has neither VIEW DATABASE STATE nor SHOWPLAN. Do not widen
its authority or guess a missing-index fix. No schema change or native write rehearsal is required.
Offline SQL parsing and comparison verify that, after removing informational messages and ELSE
grouping, commands and financial/evidence guards match the previous batch. Synthetic recovery tests
include failure before commit, lost acknowledgement, cancellation, persistent failure and preservation
of the monitor epoch on successful recovery; fatal evidence errors still interrupt normally.

The previous lag/snapshot/decision changes remain independent. Pausing paper entries does not stop
history work; stopping the shared Live producer also stops inputs needed by real execution. Expensive
history reads and the existing admission barrier are not removed by this SQL correction. Historical
coverage gaps remain, and native SQL cause/capacity acceptance is still separate from offline tests.

Normal operator stop/closure precedes installation. The October 22 review/approval and separate host
enable remain operator actions under ADR-0101, with the same source/risk/history, 15:45 entry cutoff
and 16:00 expiry. No app launch, market collection, SQL deployment, trading approval or order is implied.

## Review questions

1. Why must a timeout retry preserve the exact original evaluation ID, contents and observation times?
2. Why must lease/evidence failures still interrupt even when a transient timeout can recover?
3. Why does this storage correction not establish that paper CPU cost or a historical SQL wait is fixed?
