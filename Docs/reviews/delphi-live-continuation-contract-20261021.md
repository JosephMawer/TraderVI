# Delphi Live Normal — accepted October 21 continuation

**Status: explicitly accepted October 20; source implemented and offline validated.
Development work saves no financial permission or host enable; subsequent operator pre-open approval
and Armed UI confirmation are observed October 21 in [current status](../project-status.md#october-21-pre-open-approval-implementation).**

**October 21 accepted pre-open amendment:** [ADR-0096](../adr/0096-pre-open-october21-approval-and-armed-host.md)
replaces the regular-session-only review/enable timing in the original flow below. The operator may
approve on October 21 before 09:30 and separately arm the running host. Pre-open source review checks
stored Normal definition and today's Valid daily/config/model input; execution waits for the open,
fresh matching Normal evidence and renewed account/order/risk/storage checks. No pre-open order,
automatic application startup, guaranteed opening fill or October 22 renewal follows. The reviewed
16:00 Toronto cutoff and original financial history/holds/ceiling remain. See the updated [operator flow](../running.md#october-21-only-normal-continuation).

**October 20 accepted simplification:** [ADR-0095](../adr/0095-current-readiness-without-daily-verification-replay.md)
keeps completed broker tests in development records. Current approval no longer depends on a new
qualification packet, individual rereads of the four test orders, or a verification checkpoint/restart.
The original financial history, risk controls, current account/source/storage checks and dated
approval/separate enable remain. Earlier development validation below records the superseded flow.

The immediate problem is tomorrow's buying permission: today's managed Normal assignment is durable,
but its deliberately limited buying scope expired at October 20 close. The parent problem is safe
next-session continuation without repeating initial account setup. The root goal is recoverable
management of the same real account while preserving financial history and risk controls.

## Accepted decision

The operator explicitly approves implementation of **one supervised October 21 continuation** on the existing managed
assignment, with a fresh dated permission that expires at the reviewed October 21 regular close.
This is an explicit amendment to ADR-0092's October 20-only/no-further-carry restriction, not an
interpretation of the expired permission or a conversion to ordinary production qualification.
No October 22 or recurring buying permission follows.

The operator's clarification is resolved before acceptance: original order IDs are historical
verification records to read again, not a whitelist of stocks. Normal may buy eligible stocks never
owned before, subject to its existing gates, available capital and risk checks. Cash-only is the
pre-activation condition; it does not prevent qualifying purchases after manual activation.

The recommended path is restricted to an account that is still entirely cash-only, with the same
cash, no added/missing/changed financial orders, no pending orders and no holds. October 20's final
16:04 read found no automatic orders/positions; tomorrow's fresh native checks must establish that
this remains true. If it does not, this shortcut is blocked and the account requires a separate
review; do not liquidate, reset references or reinterpret a mismatch to qualify it.

## Financial and evidence invariants

1. Preserve the original October 19 baseline/import, both original full-sale records, verification
   buy/sale records and their original IDs. Ordinary financial reconciliation retains those facts;
   never repeat verification trades or use their completed test as another daily permission.
2. Preserve the original daily/drawdown reference objects, amounts, provenance, observation times,
   holds, prior trial carry, account ledger and all managed-session revisions. Never overwrite the
   October 20 approval, qualification packet/checkpoint or expiration evidence.
3. Append a distinct October 21 risk association and session permission linked to the original
   baseline, actual October 20 close/expiry records and latest saved managed-session revision.
   Original reference values remain setup observations, not reconstructed historical market opens,
   closes or lifetime highs. Preserve daily/capital holds and all engine risk memory; this operation
   cannot reset a trading loss, release a hold or refill an already-used spending allowance.
4. Require explicit operator confirmation of no deposits, withdrawals, outside trades or other
   account changes since the original cash-only baseline, continuing through October 21 close.
   Equal cash alone does not establish absence of outside activity. Fresh native identity, original
   order facts, cash/buying power, inventories and pending orders must reconcile exactly.
5. Retain the full-fill-only trial limits, ordinary Normal
   gates, real capital ceiling, whole-share sizing, one execution owner, durable intent-before-send,
   original-ID recovery and independent protection. Unobserved partial/cancel/uncertain/power-loss
   paths remain unqualified and stop automatic progression for operator review.
6. Review the exact current Normal profile, policy/daily/model definition, prospective source and
   running build for October 21. Existing evidence is historical input, not automatically fresh
   qualification for another date/build. Incompatible/changed strategy definitions require explicit
   review rather than silent substitution. Missing source/storage/account evidence blocks approval.
7. Preserve historical native proof with its true dates/builds and qualification limits in development
   records. Current continuation checks account/risk/source/build and immutable storage directly;
   it does not publish another test packet or require a verification restart. Ordinary account-history
   reconciliation still rejects changed or missing financial orders. Never relabel old proof or replay a trade.
8. New permission ends at the calendar's reviewed October 21 regular close. Recheck expiry before
   reserving a buy and immediately before transmission. No replacement/residual order, automatic
   scope renewal or ordinary Resume buying action may bypass that deadline or an order-review hold.

## Intended operator flow

These controls are built in normal Release output; actual October 21 native checks remain separate.

1. Keep the existing saved records. No Sell all, test buy/sale or Account risk setup save is needed.
   Before changing the installed build or saving a continuation, stop the old execution host normally
   and confirm it is stopped; original orders and independent holds remain authoritative.
2. Run the reviewed build from the normal checkout/Release output. No separate launcher or full
   copied runtime is needed. Review the exact running build; do not relabel an October 20 test
   observation as native qualification of changed code.
3. During October 21 regular trading, confirm the no-outside-change condition and choose
   **Review next session** action. It recovers the existing assignment and original references/
   orders automatically, show current readiness checks, cash ceiling, preserved holds, exact Normal/build/
   current source and October 21 expiry. Show any changed fact or blocker before requesting approval.
4. Review the current cash-only account and orders, preserved references/holds, exact fresh Normal
   source/running build, installed immutable storage and dated permission. Completed manual tests
   remain development evidence; no new checkpoint, test trade, old-sale selection or verification
   restart is required. A normal app restart to load a newly built program is ordinary installation,
   not another qualification exercise.
5. Explicitly **Approve October 21 continuation** after review. This appends the dated permission
   and linked risk association, preserving the existing assignment/history. Save/read back under
   the same account/revision fences; recover an uncertain/partial save by its original request ID.
   Fail closed until both records agree. Approval submits no order and must not inherit an enabled
   old host grant, even if strategy identity is unchanged.
6. Separately **Enable reviewed host** for this exact saved October 21 permission/build/assignment.
   This remains the operator's activation action. Stay available for the supervised session.

### Accepted operator-path simplification — October 20

The operator accepts removing the desktop paper-work wait from setup opening, readiness refresh
and host enable only. The reviewed time window and actual execution poll's admission remain unchanged.

| Finding | Current operator burden and purpose | Existing protection and smallest change | Tradeoff |
|---|---|---|---|
| Unnecessary paper-work wait (implementation choice) | Opening setup, refreshing saved readiness and enabling an already approved host each drain admitted paper work, although they neither prepare fresh financial authority nor execute a cycle. The shared wrapper was intended to avoid competition during financial review/save. | Skip that wrapper's paper wait for these three entry points. Saved reads retain SQL fences/leases; enable rereads and matches the exact assignment/build/continuation and creates no cycle. The actual execution poll still drains paper work. Review/approval retain their admission and checks. | Saved readiness can be displayed while paper work continues. It is not fresh financial approval; storage reads can still block/fail and actual execution can still wait. |
| Stale running instructions (factual documentation defect) | The running guide still says no baseline/assignment exists and directs readers toward expired October 20 copied-runtime/checkpoint steps. These described first-day setup and its then-unverified state. | State the observed scoped setup/activation facts and label the first-day sequence historical, with the current October 21 route first. Keep original evidence and its limits. | General production and unobserved paths must still be distinguished from the observed limited trial. |

No further removal is justified by this inspection. The no-outside-change confirmation covers
activity equal cash cannot establish; approval's fresh account/risk/source checks protect against
changes since review. Separate approval/enable and all deadlines remain agreed boundaries. The
two-minute review lifetime is retained; no native timing measurement or deadline change is claimed.
ADR-0094 owns the separate status/layout design; this increment changes no layout or status wording.

## Implementation and readiness blockers

- [x] Operator explicitly accepts this complete dated continuation contract after the order-ID clarification.
- [x] Add append-only risk/session continuation with previous-revision fencing, original-reference/
  memory preservation, idempotent recovery and no authority inherited from old host grants.
- [x] Implement initial all-nine-check/native build review (historical; superseded by ADR-0095).
  Preserve expired October 20 packets/checkpoints/approval and their actual evidence limits.
- [x] Expose continuation review/explicit approval instead of routing an existing assignment through
  first-execution setup. Keep host enable separate and display the exact permission expiry.
- [x] Validate changed financial facts, holds, stale source/build, existing orders, conflicting prior
  revisions, uncertain save/restart recovery and both cutoff guards with meaningful offline tests.
- [x] Build the desktop project and coordinate normal-output installation without full runtime copies;
  no SQL migration/native rehearsal or external workflow is implied by preparation.
- [x] Validate/build the accepted current-readiness simplification: 243 focused/2,723 full serial
  offline passes, nine native SQL skips; standard WPF Release passes after verified zero owners.
  No app launch or broker verification is inferred. Current status owns the build evidence.
- [ ] Operator performs actual October 21 current-account review/approval/enable; observe activation and cutoff.

### Current readiness validation — October 20, ADR-0095

Twenty-one added offline cases plus the existing managed-session suite pass (243 focused); full serial
regression passes 2,723 tests with nine native SQL skips. Tests cover current source/build/account/
storage/permission blockers, changed/missing financial history, approval on a reviewed current build
without historical qualification references, unchanged first-session qualification and existing
original-request lost-acknowledgement recovery. Workflow/repository guards reject using a continuation
to create a new initial session. Normal WPF Release build passes with 262 warnings/
zero errors after an outside-sandbox process read confirms no running TraderVI owner. The app is
not launched. Logs and hash receipt are ignored under `artifacts/continuation-current-readiness-*`.
Actual October 21 account approval/activation and automated fills/protection remain unverified.

### Earlier development validation — October 20, before ADR-0095

The focused managed-session/transport/carry set passes **252 tests**, including 26 new continuation/
native-transmission tests. Synthetic tests cover lost risk/session acknowledgements and original-ID
recovery, changed account/revision/holds/source/build, missing evidence, no enlarged spending
allowance, old-grant rejection, current-build restart evidence, nine-reference publication, no
historical proof overwrite, previously unowned eligible-stock selection and cutoff before/after
durable intent/journal saves. Desktop Debug and final normal Release builds pass. Full serial
regression passes **2,702 tests**, with nine native SQL tests skipped. After operator normal closure
and verified process exit, source c27a3cf8 is built directly into normal Release output, verified at
20:50 Toronto; no application launch or copied runtime. Initial implementation is 235e7abf.
This is offline validation/build installation, not actual October 21 account qualification.

Normal output Core SHA-256 is `5DD82AA86CBC398D83AD4EE5BC771B75D6FD13CBD55B3A35861B60D80867CD12`;
WPF SHA-256 is `EA10936380CC1DA4697877A88CB9BFEF1BD708B19C1DA0D3692AA2B3BB13DF02`.
The ignored receipt is `artifacts/continuation-release-receipt.json`; build/test logs use the
`artifacts/continuation-*` prefix. Existing October 20 proof remains historical; tomorrow's capture
binds whichever reviewed Release binary actually runs. Build before capture and use `--no-build`
only for the subsequent same-build recovery restart.

Existing dependency advisories for `System.Security.Cryptography.Pkcs` 9.0.4 (NU1902/NU1903) remain
separate from existing nullable/unreachable/unused-variable compiler warnings. No full solution/SSDT
build, SQL migration/rehearsal, market-data workflow, application launch or broker mutation ran.

## Implementation notes

The original qualification-packet/checkpoint implementation described below is historical under
ADR-0095. Current continuation instead reads current storage, source and account/risk state, prepares
an approval with no historical qualification references, and retains the same linked financial-save
workflow. A partial risk save recovers its original continuation request from risk storage before
review; no old test file is needed. First-session proof producers retain their original behavior.

The original managed approval remains immutable. An optional continuation contains its own explicit
approval and risk request; execution resolves the effective dated review without replacing the
original assignment/session identity, activation boundary, orders, protection or memory. The risk
association advances only the allowed trial date, preserving the original reference objects,
`Through`, `LastCompletedSession`, prior carry and all holds. The saved October 20 post-close
evaluation is linked by exact revision/content hash; it is expiry evidence, not a qualified financial
closing valuation. Only an unchanged, order-free cash book qualifies this shortcut.

The existing append-only JSON tables support these optional fields without a schema migration.
Risk association is appended/read back first while holding the managed-session lease; then the
linked permission revision is appended. A lost acknowledgement recovers the deterministic original
request. An incomplete pair cannot qualify the old scope or enable the new host; changed intervening
risk/session history fails closed. Preview uses a read-only prospective risk view and cannot append
financial events. Actual native synchronization may still append normal reconciled account imports.

The historical carry is stored once within the continuation, with the small prior risk anchor
reconstructing its exact original hash. The fixed read-only helper verifies latest revision 49 and
existing JSON fragment sizes. An offline serialized-shape check using those private saved fragments
is approximately 185 KB after compaction versus 270 KB with duplicated carry, under the unchanged
256 KiB risk-event limit. Hash-preserving JSON recovery and a synthetic larger historical book are
unit-tested; this is size/read evidence, not native SQL append qualification. Query receipts remain
private in ignored artifacts (hashes `FA3472A85537BA880A602D2A10D7DA9DA09D37AE9B2EF307A4BA5D6EFB4A78D6`
and `0A6B92020E65D8ABFCBF50D11DA1C670B67CF62C04B98FBA6FF8FB1120CD18CB`).

Each protected October 21 packet/checkpoint filename includes the current implementation identity;
the October 20 files are never overwritten. A new host grant pins the exact continuation ID as well
as strategy/build/session. Old grants cannot run it even when the strategy fingerprint is unchanged.
Expiry is checked before intent reservation, after its durable save and again beside the native
buy transmission, including a journal write that crosses the cutoff. No automatic renewal or
ordinary Resume buying can change this dated permission.

The complete production cash-event/opening-value producer, unrestricted recurring authority,
actual automated fills/populated protection, display refresh issue and remaining paper-history cost
are separate work. This contract does not assert that they are completed or waive them for production.

## Review questions

1. Why does retaining a strategy assignment not renew its dated buying permission?
2. Which original facts may be reused, and which current-date/build checks still need fresh evidence?
3. Why must saving tomorrow's permission not silently inherit today's enabled host grant?
