---
cssclasses: [executive-brief]
---
# Real TFSA execution and buying permission UI design draft

**October 20, 2026. Scope accepted; layout and wording are draft for operator review before code.**

> [!summary] Proposed design
> Put observed execution activity beside dated buying permission, with visible reasons and real
> observation times. Group setup into review, dated approval and separate enable; this changes
> presentation only, with automatic tomorrow start deferred.

**Next decision:** review the two screen wireframes and example wording below before implementation.

The immediate problem is understanding whether automatic execution is doing work and whether new
purchases are permitted. This belongs to safe account operation. The root goal is deliberate,
recoverable Normal management of the same TFSA with its history and controls intact.

The screen answers two separate questions: **What is automatic execution doing?** and **What is
the current buying permission?** A saved enabled grant is permission to run the reviewed host,
not proof that it is running. A completed cycle is not proof that a purchase is allowed.

## Account screen

Keep performance and the existing holdings layout. Place a full-width status strip immediately below
the strategy selector and above holdings. Use two equal sections at ordinary desktop widths; stack
execution above buying permission when narrow. This avoids squeezing reasons into the current
240-pixel action column. All times are explicitly Toronto time and include the date.

```text
Strategy  Normal                         [Review switch…]
Ongoing strategy selection

AUTOMATIC EXECUTION                      NEW PURCHASES
[clock] Enabled — waiting                 [lock] Permission expired
Waiting for desktop paper work            Expired Oct 20, 2026 · 4:00 PM Toronto
to finish.                               October 21 review and dated approval required.
Last check: Oct 20 · 4:02 PM Toronto       Saved mode: new buys not stopped by operator
Last completed cycle: Oct 20 · 3:58 PM     Permission/account check: Oct 20 · 4:02 PM

[Buying permission & execution…]          [Stop automatic execution]
```

The dates/times above are **illustrative**, not a new account observation. Show saved mode underneath
expiry/blockers so the weaker saved preference cannot obscure an expired permission. Avoid a global
green “Ready” badge. Unexpired permission says **Subject to fresh checks**. Existing Stop new buys /
Resume buying controls retain their current behavior and remain distinct from stopping the host.

The visible reason must not require a tooltip. Show the primary blocker and expose all remaining
known reasons under **Details**. Label saved facts with their observation time; do not refresh their
timestamps when repainting the UI. Missing observations read **Not checked in this app session**.

## Buying permission and execution screen

Rename the presentation title and entry button to **Buying permission & execution**. Keep the
existing commands and handlers. At the top, repeat the same two status summaries as the account
screen; they remain visible while action progress and scrollable evidence change below them.

```text
Buying permission & execution                    Normal · TFSA
[execution status + reason]       [buying permission + expiry]

1  Review account and evidence
   October 21 only. Reuse the original trades and references.
   [ ] I confirm no outside account changes … [existing full wording]
   [Review next session] [Refresh readiness]
   Progress / precise blocker
   [Readiness details: current account/orders/risk/storage, exact strategy, source,
    build, cash ceiling, original references, provenance and holds]

2  Approve dated buying permission
   Review valid until: [actual review deadline]
   Purchases stop: Oct 21, 2026 · 4:00 PM Toronto
   [Approve October 21 continuation]  (existing eligibility)
   Approval sends no order. Automatic execution remains separate.

3  Enable automatic execution
   Use the explicitly reviewed, saved assignment and permission.
   [Enable reviewed host]  (existing eligibility)

[Stop automatic execution]   [Earlier first-session setup ▸]
```

The three groups organize existing actions; they are not a new wizard or gate. Preserve current
enable/disable predicates. Explain unavailable actions using actual prerequisites/results; never
invent evidence or imply a review passed merely because a previous step was clicked. Retain all
current readiness checks and exact review content in details. ADR-0095 removes the repeated
test qualification packet from October 21 approval; earlier first-session qualification details
retain their historical meaning. Primary
reasons and the expiry remain visible when details are collapsed.

For this existing assignment, October 20 carry, initial checkpoint and first-approval actions belong
in the secondary section with a clear **Earlier setup — not the October 21 continuation path**
description. Preserve existing visibility rules and handlers; do not make currently hidden actions
available. For a genuinely unassigned account, expose its existing first-session route normally.
The original orders and two-sale selection are recovered automatically on October 21.

While waiting for paper work, show elapsed time and **Close this window to cancel waiting**.
After work begins, retain the existing busy-close guard. October 21 continuation requires no
new verification checkpoint/restart under ADR-0095. Earlier first-session recovery instructions
retain their existing meaning. No replacement launcher or runtime copy is introduced.

## Execution states and freshness

Icons are simple shapes paired with text; color is supplementary. These are presentation states,
not replacements for the saved managed-session phase or new execution controls.

| Icon | Label | Evidence needed / visible explanation |
|---|---|---|
| Square | Stopped | A confirmed disabled/absent host grant after any running cycle finishes; no new cycles. Original orders still need recovery. |
| Clock | Enabled — waiting | Enabled grant observed; no cycle currently observed in progress. Show the actual account, paper-work, hours or next-check wait. |
| Activity | Running a cycle | An observed start of the current cycle with no observed completion. Show start time and elapsed duration; never infer an order or fill. |
| Pause bars | Paused | Observed saved assignment pause. This pauses managed submissions, including exits; it is distinct from Stop new buys. |
| Warning triangle | Blocked | Host recovery/check failure or incompatible assignment/build. Show the safe reason and any retry-backoff time. |

Before the first permission read, use **Checking execution status**. Do not display a default Off
value as a confirmed stop. A cycle finishing returns the activity display to waiting, paused or
blocked as supported by its result, rather than leaving “Running” on screen indefinitely.

Five minutes without a new status observation adds **Status may be stale — current activity is
unconfirmed**. Show the last known state as historical, including an old Stopped or Running state.
The five-minute rule is a display warning only; it changes no trading freshness, retry or risk rule.
Long work may be legitimate: show **Started at …; completion not observed** rather than claiming a
hang or success. A lightweight display timer ages text; it performs no SQL/broker read or poll.

**Last completed cycle** means a host cycle returned normally; it can still return purchase blockers.
It does not mean an order succeeded or every source qualified. Failed/cancelled cycles cannot advance
it. Track separately from last host check, last cycle start and the saved assignment's revision time.
At restart, in-process activity is unknown; do not relabel an old saved evaluation as this process's
successful cycle. Show saved record times separately if useful.

## Buying permission examples

| Display | Meaning and next action |
|---|---|
| Not verified | No fresh same-account permission observation; refresh/review. Do not assert missing approval from a failed read. |
| Permission expired | Show exact saved deadline. October 21 needs native review, explicit dated approval and separate enable. Resume buying cannot extend it. |
| Stopped by operator | Saved purchase-only stop; expiry/other holds remain visible. Existing exit rules are retained, but actual execution depends on host activity. |
| Blocked | Show known account/risk/order/source/build holds from the observed record, with its check time. |
| Subject to fresh checks | Unexpired saved scope with no known blocking saved facts. Every action still needs existing fresh account, order, source, sizing and risk checks. |

Multiple restrictions can coexist. Present expired/missing permission first, then operator stop and
other known blocks; never hide a latched hold behind a preferred headline. A stale or wrong-account
observation cannot produce a positive current permission statement. At the exact saved expiry,
the display changes immediately even if the host is busy. This does not replace either buy cutoff guard.

## Delivery and review

The accepted scope is recorded in [ADR-0094](../adr/0094-real-tfsa-execution-status-presentation.md).
Detailed layout/wording await operator feedback. **Starts automatically tomorrow** is deferred;
no scheduling, startup or new trading authority is included. The
[October 21 contract](../reviews/delphi-live-continuation-contract-20261021.md) remains authoritative:
current account/risk/source/storage review under [ADR-0095](../adr/0095-current-readiness-without-daily-verification-replay.md), explicit approval, separate host enable and
no purchases after 4 PM Toronto. No October 22 renewal follows.

After design review, implement passive observation/presentation and reuse existing setup actions.
Test offline: initial unknown and absent grant; saved enabled versus actual cycle; success with buy
blocks; failure/cancellation; paper/account waits; pause/stop; stale and future observations; logout/
account switch; expiry exactly at 4 PM; operator stop plus risk/order holds; and identical summaries
in both views. Check normal/narrow layout, keyboard order, wrapped reasons and accessible labels.
Coordinate normal app closure before a standard-output build. Do not launch operational workflows
to verify the design or save permissions, submit orders, run native SQL tests, or enable execution.

Preserve assignment/approval, original trade/order IDs and sale selection, references/provenance,
all holds, memory, qualifications and protected proof. No migration or financial record rewrite.

The affected presentation paths are `BrokerExecutionHost`, `ManualBuyCoordinator.ExecutionHost`,
`BrokerExecutionSetupPanel`, `WealthsimpleAccountPanel` and the saved buying-mode view model.
Observe activity at existing boundaries; do not create another polling owner or change order work.

**Design-only validation, October 20:** the companion in-conversation mockup passes 60 offline
screen/example/width checks (two screens, ten illustrative states, widths 320/620/940), with local
preview actions, bundled icons and no page-script errors. Normal and narrow captures were visually
reviewed; external browser requests were blocked. This validates the design preview, not WPF runtime
behavior or current trading readiness. Ignored checks/captures: `artifacts/tfsa-ui-design-20261020/`.

## Review questions

1. Why can an enabled host and an unexpired permission still produce no purchase?
2. What does a last completed cycle establish, and what does it leave unconfirmed?
3. Why must an old status become visibly uncertain even when its last label was Stopped?
