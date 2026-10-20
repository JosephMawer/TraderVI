# Delphi Live Normal — proposed October 21 continuation

**Status: proposed October 20; operator decision pending. No October 21 financial authority or renewal is saved.**

The immediate problem is tomorrow's buying permission: today's managed Normal assignment is durable,
but its deliberately limited buying scope expired at October 20 close. The parent problem is safe
next-session continuation without repeating initial account setup. The root goal is recoverable
management of the same real account while preserving financial history and risk controls.

## Decision requested

Approve implementation of **one supervised October 21 continuation** on the existing managed
assignment, with a fresh dated permission that expires at the reviewed October 21 regular close.
This is an explicit amendment to ADR-0092's October 20-only/no-further-carry restriction, not an
interpretation of the expired permission or a conversion to ordinary production qualification.
No October 22 or recurring buying permission follows.

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

These are proposed controls, not buttons already implemented in the current app.

1. Keep the existing saved records. No Sell all, test buy/sale or Account risk setup save is needed.
   Before changing the installed build or saving a continuation, stop the old execution host normally
   and confirm it is stopped; original orders and independent holds remain authoritative.
2. Run the reviewed build from the normal checkout/Release output. No separate launcher or full
   copied runtime is needed. Use the build command appropriate to its reviewed native qualification;
   do not claim the current October 20 Core hash qualifies changed code.
3. During October 21 regular trading, confirm the no-outside-change condition and choose a new
   **Review next session** action. It must recover the existing assignment and original references/
   orders automatically, show all nine checks, cash ceiling, preserved holds, exact Normal/build/
   current source and October 21 expiry. Show any changed fact or blocker before requesting approval.
4. If the installed Core changed, perform one new-build capture/normal-restart recovery using the
   original order IDs. This can require a normal app restart; it does not repeat the trades, risk
   setup or original sale selection. If the build is unchanged, do not demand a redundant restart
   solely because the trading date changed; the retained evidence must actually support reuse.
5. Explicitly **Approve October 21 continuation** after review. This appends the dated permission
   and linked risk association, preserving the existing assignment/history. Save/read back under
   the same account/revision fences; recover an uncertain/partial save by its original request ID.
   Fail closed until both records agree. Approval submits no order and must not inherit an enabled
   old host grant, even if strategy identity is unchanged.
6. Separately **Enable reviewed host** for this exact saved October 21 permission/build/assignment.
   This remains the operator's activation action. Stay available for the supervised session.

## Implementation and readiness blockers

- [ ] Operator explicitly accepts or revises this complete dated continuation contract.
- [ ] Add append-only risk/session continuation with previous-revision fencing, original-reference/
  memory preservation, idempotent recovery and no authority inherited from old host grants.
- [ ] Integrate current-source/all-nine-check review and versioned original-ID/native build evidence;
  preserve expired October 20 packets/checkpoints/approval. No qualification waiver.
- [ ] Expose continuation review/explicit approval instead of routing an existing assignment through
  first-execution setup. Keep host enable separate and display the exact permission expiry.
- [ ] Validate changed financial facts, holds, stale source/build, existing orders, conflicting prior
  revisions, uncertain save/restart recovery and both cutoff guards with meaningful offline tests.
- [ ] Build the desktop project and coordinate normal-output installation without full runtime copies;
  no SQL migration/native rehearsal or external workflow is implied by preparation.
- [ ] Operator performs actual October 21 native review/approval/enable; observe activation and cutoff.

The complete production cash-event/opening-value producer, unrestricted recurring authority,
actual automated fills/populated protection, display refresh issue and remaining paper-history cost
are separate work. This proposal does not assert that they are completed or waive them for production.

## Review questions

1. Why does retaining a strategy assignment not renew its dated buying permission?
2. Which original facts may be reused, and which current-date/build checks still need fresh evidence?
3. Why must saving tomorrow's permission not silently inherit today's enabled host grant?
