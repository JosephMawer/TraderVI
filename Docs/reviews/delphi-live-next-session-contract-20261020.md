# Delphi Live Normal — accepted October 20 first execution session

**Status: explicitly accepted October 19; implemented, offline tested and installed with execution off. Actual October 20 qualification remains required. No trading authority is saved.**
Prepared October 19 Toronto. October 19 buying permission expired without a native checkpoint,
qualification packet, strategy assignment or host enable. Acceptance saves none of those actions.
The operator requested preparation for October 20 while preserving completed trades and saved risk
references. [ADR-0092](../adr/0092-scoped-supervised-delphi-live-tfsa-session.md) remains authoritative
with the October 20 amendment below explicitly accepted; actual native checks remain required.

## Accepted contract

1. **One launch session:** October 20, 2026, only if it is the next regular session after the original
   baseline date in the reviewed TSX calendar. New purchases start only after qualification, explicit
   assignment approval and separate host enable during that session. They expire at its reviewed
   regular close (16:00 Toronto for the reviewed ordinary session). No October 21 buying renewal,
   recurring permission or general production qualification follows.
2. **Original evidence stays original:** retain the October 19 baseline request/import, both initial
   reference records and their values, evidence IDs/timestamps and source labels. Retain the two
   original liquidation sales, the one-share verification buy and its separate sale. Do not repeat
   trades, save another initial baseline, repaint an opening/high or rewrite historical dossiers.
3. **Cash-only bridge:** at the October 20 review, read the same original broker order IDs and
   reconcile a fresh native account snapshot against the original immutable cash-only baseline.
   Require no holdings, no pending orders, unchanged exact cash/account value, unchanged original
   cumulative order facts (only unchanged filled-to-posted status advancement allowed), no added or
   missing order, no conflict and no existing managed assignment. Require explicit operator
   confirmation of no deposits, withdrawals, outside trades or other account changes since the
   original baseline, continuing through the reviewed trial. Equal totals alone do not prove this.
4. **Risk history is carried, not reconstructed:** an explicitly reviewed, append-only, trial-specific
   carry event may associate the unchanged original reference records with October 20 risk evaluation.
   It must retain both values, sources, original evidence IDs/times, all existing holds, financial
   anchors and any stronger position protections. It must not claim an observed October 19 closing
   book or an October 20 market opening, advance the closing high, mark a missing close complete,
   clear a daily/capital hold or rewrite the initial baseline. A held/conflicting/advanced risk state
   blocks this happy path; there is no reset/replacement control in this workflow.
5. **Narrow authority:** the carry event is usable only by the separately identified October 20
   supervised first-session scope, binding the original baseline, exact next date and close-time
   expiry. General production/older scopes cannot use it as opening/cash-event qualification. It
   cannot roll again to a later day. Saving it requires a separate explicit native review/save by
   the operator; the agent will not save financial authority.
6. **Fresh strategy and same-copy recovery:** require the exact current Normal profile/policy/daily/
   build and fresh October 20 source. Read the original verification order IDs on both sides of a
   normal restart of that same executable. Before/after observations bind unchanged original
   baseline, carried risk revision, account facts and strategy. Today's historical host gap stays
   disclosed; it is not converted into clean research evidence. No pre-open/off-session snapshot
   substitutes for the required fresh in-session source.
7. **Qualification and execution remain separate:** retain all four native and five scoped checks,
   protected hash-linked proof, full-fill-only limits and explicit performance-evidence limitations.
   All proof is trial-only, dated October 20, and expires at that session close. Current ordinary
   gates, whole-share sizing, actual available-capital ceiling, source/price checks, durable order
   intent, one owner, original-ID recovery and independent protection remain required. Approval
   sends no order; enabling the reviewed host permits actual automatic trading.
8. **Stop on mismatch:** missing/changed evidence, unexplained cash, extra/missing order, loss hold,
   unsupported source or partial/cancelled/uncertain outcome stops new automatic order progression.
   Existing protective obligations remain governed by their existing independent controls. No
   replacement/residual order, financial-reference reset or automatic scope renewal is allowed.

This is a disclosed engineering trial, not proof of profitability, complete external cash-event
coverage, exact historical opening/closing values, native partial/uncertain/power-loss recovery or
general production readiness. A conditional no-transfer assertion cannot qualify those claims.

## Explicit approval and its boundary

ADR-0092 says the current source "expires at the reviewed October 19 regular close" and "cannot
authorize next-day buys, reset references or clear holds." ADR-0083 requires missing closing history
to be recovered in order for ordinary risk continuation. This proposal introduces a separate,
explicitly labelled, single-session carry exception while leaving ordinary continuation unchanged.
The operator subsequently explicitly accepts this complete dated contract in the current chat.
That accepts this risk-history treatment and authorizes implementation; it saves no financial
carry, assignment, host permission or real order. Each native financial/activation action remains
the separate operator step below.

## Implementation dependencies before calling this ready

- [x] Operator explicitly accepts the complete October 20 contract in this chat on October 19.
- [x] Implement the trial-only append request/readback and native repository recheck, with exact unchanged-anchor validation,
  original reference preservation, no clearing of holds and production-projection exclusion. Native save remains unverified.
- [x] Extend Core trial scope/native producer to distinguish original verification date from the execution
  date; require original-ID history and exact next-session/carry linkage. Older scopes stay unchanged. Unchanged overnight posting and successful original-ID read times are covered without duplicate fill observations.
- [x] Expose preview and explicit carry save through the native execution setup screen; no baseline
  resave, trade replay, packet checkbox or implicit financial save during capture/preparation.
- [x] Validate focused/full offline tests and WPF build: 2,655 full passes, nine native skips; six
  synthetic panel renders pass at minimum/default sizes with no binding errors. The Release WPF
  build succeeds with 262 existing compiler warnings and cached Pkcs security advisories; no fresh
  audit, native SQL rehearsal or full solution/SSDT validation is claimed.
- [x] Install the exact reviewed copy with execution off: b1095ae9 started October 19 **23:08:41
  Toronto**, PID **83608**; manifest/Core hashes match, one visible responding owner. Host permission,
  packet and both October 19/20 checkpoints are absent. Receipt: `artifacts/delphi-live-tfsa-20261018/next-session-switch-receipt.json`.
- [ ] Perform actual October 20 account/source/carry/checkpoint/restart/nine-reference observations.
- [ ] Operator reviews and approves the assignment, then separately enables the reviewed host.

## Operator sequence

**Tonight:** contract accepted; keep execution off. No additional buy/sale, new initial
baseline or manual risk-reference reset is required. Installed executable:
`C:\src\TraderVI\artifacts\delphi-live-tfsa-20261018\next-session-working-copy\TraderVI.WPF.exe`.
Installed Core SHA-256: `4BBBD6F141CD50F1D634BEAA4D0533ECFF307B4C65642B84C6C25346214DC07F`.
The local launch helper, `C:\src\TraderVI\artifacts\delphi-live-tfsa-20261018\Run-DelphiLiveNormal.cmd`,
will open only that hash-checked copy with the repository working directory and reviewed calendar.
It will reuse an existing exact owner, refuse another copy, and retain all financial evidence on a
normal restart. The helper grants no financial authority.

**October 20, one step at a time:**

1. Use the launch helper above to open the exact reviewed working copy, sign in normally if needed,
   and confirm the intended TFSA.
   Leave host enable untouched. Keep the app running so ordinary paper publication can resume.
2. After the regular session starts, wait for a fresh Normal source. Refresh the account and verify
   cash-only holdings, no pending orders, and no intervening account changes. There is no requirement
   for Normal to generate a buy; its ordinary confirmation/data/entry gates still decide.
3. Open Trading → Execution setup. Check the no-outside-change confirmation, then click
   **Preview October 20 carry**. Review original
   references, verification date/order identities, next execution date, unchanged cash/holds and
   close-time expiry. Click **Save reviewed October 20 carry** only after reviewing those facts;
   wait for saved/readback confirmation. Do not use Account risk setup to save another baseline.
4. Capture native recovery checkpoint. Continue only after an explicit successful-save message.
5. Close TraderVI normally and reopen that same exact executable. Do not change builds between the
   successful capture and recovery review.
6. Choose Prepare first execution review. Select the two original liquidation sales, excluding the
   later test-position sale. The producer reads the original buy/all three sale IDs, saved baseline
   and carry revision again. It must publish all nine checks from actual matching observations.
7. Review the exact account/Normal strategy/build, disclosed evidence limits, capital ceiling,
   carried original references, all nine checks and October 20 expiry. If complete, personally click
   Approve reviewed assignment. Verify its saved confirmation. This does not send an order.
8. Separately click Enable reviewed host only when ready for real automatic orders. Stay available
   for the supervised trial. Use Stop execution host to stop new cycles if needed; existing orders
   still require original-ID recovery and closing the window does not cancel them.

If any step blocks, preserve the displayed evidence/diagnostic and stop that progression. The agent
will diagnose the cause; no new trade, baseline replacement, date edit or qualification waiver is a
recovery step. The next actual market-close behavior of the installed closing-lease fix also remains
an operational observation to record.
