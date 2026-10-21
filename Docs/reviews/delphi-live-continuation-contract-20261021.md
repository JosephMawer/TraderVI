# Delphi Live Normal — accepted October 21 continuation

**Status: explicitly accepted October 20; source implemented and offline validated.
No October 21 financial permission or host enable is saved by this development work.**

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
   buy/sale records and their original IDs. Re-read those orders; never repeat verification trades.
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
5. Retain all four native and five scoped checks, the full-fill-only trial limits, ordinary Normal
   gates, real capital ceiling, whole-share sizing, one execution owner, durable intent-before-send,
   original-ID recovery and independent protection. Unobserved partial/cancel/uncertain/power-loss
   paths remain unqualified and stop automatic progression for operator review.
6. Review the exact current Normal profile, policy/daily/model definition, prospective source and
   running build for October 21. Existing evidence is historical input, not automatically fresh
   qualification for another date/build. Incompatible/changed strategy definitions require explicit
   review rather than silent substitution. Missing source/storage/account evidence blocks approval.
7. Use append-only/versioned continuation proof. Reuse historical native facts only with their true
   dates and qualification limits; re-read required records and publish the October 21 packet from
   actual matching observations. If Core changes, current-build native capture/normal-restart
   recovery remains required. Preserve earlier proof; never relabel its date/hash or replay a trade.
8. New permission ends at the calendar's reviewed October 21 regular close. Recheck expiry before
   reserving a buy and immediately before transmission. No replacement/residual order, automatic
   scope renewal or ordinary Resume buying action may bypass that deadline or an order-review hold.

## Intended operator flow

These controls are implemented in source; installation and actual October 21 native checks remain separate.

1. Keep the existing saved records. No Sell all, test buy/sale or Account risk setup save is needed.
   Before changing the installed build or saving a continuation, stop the old execution host normally
   and confirm it is stopped; original orders and independent holds remain authoritative.
2. Run the reviewed build from the normal checkout/Release output. No separate launcher or full
   copied runtime is needed. Use the build command appropriate to its reviewed native qualification;
   do not claim the current October 20 Core hash qualifies changed code.
3. During October 21 regular trading, confirm the no-outside-change condition and choose a new
   **Review next session** action. It recovers the existing assignment and original references/
   orders automatically, show all nine checks, cash ceiling, preserved holds, exact Normal/build/
   current source and October 21 expiry. Show any changed fact or blocker before requesting approval.
4. This implementation changes Core. The first **Review next session** saves and reads back a new
   October 21/current-build checkpoint from the original four order IDs and asks for one normal
   restart. Close normally, reopen with `dotnet run --project TraderVI.WPF -c Release --no-build`,
   confirm no outside changes again and choose **Review next session** again. It automatically
   recovers the original two-sale selection and publishes all nine references only after actual
   same-build restart recovery. No trades or baseline setup are repeated. Subsequent retries of
   this review reuse that current-build packet, not another restart.
5. Explicitly **Approve October 21 continuation** after review. This appends the dated permission
   and linked risk association, preserving the existing assignment/history. Save/read back under
   the same account/revision fences; recover an uncertain/partial save by its original request ID.
   Fail closed until both records agree. Approval submits no order and must not inherit an enabled
   old host grant, even if strategy identity is unchanged.
6. Separately **Enable reviewed host** for this exact saved October 21 permission/build/assignment.
   This remains the operator's activation action. Stay available for the supervised session.

## Implementation and readiness blockers

- [x] Operator explicitly accepts this complete dated continuation contract after the order-ID clarification.
- [x] Add append-only risk/session continuation with previous-revision fencing, original-reference/
  memory preservation, idempotent recovery and no authority inherited from old host grants.
- [x] Integrate current-source/all-nine-check review and versioned original-ID/native build evidence;
  preserve expired October 20 packets/checkpoints/approval. No qualification waiver.
- [x] Expose continuation review/explicit approval instead of routing an existing assignment through
  first-execution setup. Keep host enable separate and display the exact permission expiry.
- [x] Validate changed financial facts, holds, stale source/build, existing orders, conflicting prior
  revisions, uncertain save/restart recovery and both cutoff guards with meaningful offline tests.
- [ ] Build the desktop project and coordinate normal-output installation without full runtime copies;
  no SQL migration/native rehearsal or external workflow is implied by preparation.
- [ ] Operator performs actual October 21 native review/approval/enable; observe activation and cutoff.

### Development validation — October 20

The focused managed-session/transport/carry set passes **252 tests**, including 26 new continuation/
native-transmission tests. Synthetic tests cover lost risk/session acknowledgements and original-ID
recovery, changed account/revision/holds/source/build, missing evidence, no enlarged spending
allowance, old-grant rejection, current-build restart evidence, nine-reference publication, no
historical proof overwrite, previously unowned eligible-stock selection and cutoff before/after
durable intent/journal saves. Desktop Debug and initial normal Release builds pass. Full serial
regression passes **2,702 tests**, with nine native SQL tests skipped. The compact-record follow-up
needs its final normal Release rebuild after commit. This is offline validation, not actual October
21 account qualification.

Existing dependency advisories for `System.Security.Cryptography.Pkcs` 9.0.4 (NU1902/NU1903) remain
separate from existing nullable/unreachable/unused-variable compiler warnings. No full solution/SSDT
build, SQL migration/rehearsal, market-data workflow, application launch or broker mutation ran.

## Implementation notes

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
