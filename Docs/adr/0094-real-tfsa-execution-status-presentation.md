---
cssclasses: [executive-brief]
---
# ADR-0094: Real TFSA execution status and buying permission presentation

**Date:** October 20, 2026. **Status:** Presentation scope accepted; detailed design draft requested
before implementation. No source implementation or runtime qualification in this decision.
**Domains:** architecture, user-interface.
**Related:** ADR-0085, ADR-0086, ADR-0092 and the accepted October 21 continuation contract.

## Decision and practical consequence

Show observed host activity separately from dated buying permission. The operator accepts a small
display/layout scope with an icon and plain-language states, visible reasons, last check and last
completed cycle, a five-minute stale-status warning, and setup grouped into review, dated approval
and separate host enable. The operator then requests a design draft to inspect before code.

The immediate problem is misleading execution status: the existing Running label follows an Active
cycle result, even when new purchases are blocked. The parent problem is understandable real-account
controls; the root goal is recoverable Normal management without accidental financial authority.

## Boundaries

1. An enabled saved grant does not establish current process activity. Running a cycle requires
   an observed in-process start. Completion and check times retain their actual meaning and age.
2. Buying mode, dated permission/expiry and known blockers are separate from host activity.
   Unexpired permission remains subject to fresh checks. Do not infer success, order submission or
   a fill from a host status.
3. The five-minute stale warning is presentation only. It cannot pause/resume execution, change
   financial freshness thresholds, create polling work or clear an independent hold.
4. Reorganize existing setup actions without changing their eligibility or side effects. Preserve
   review deadlines, original-ID recovery, account fences, busy-close behavior and separate enable.
5. Scheduling and “starts automatically tomorrow” remain deferred. Retaining a strategy/grant cannot
   renew the October 20 permission. The subsequent
   [ADR-0095](0095-current-readiness-without-daily-verification-replay.md) replaces October 21's
   repeated native qualification/restart with current readiness checks; explicit dated approval,
   separate host enable and the 4 PM Toronto cutoff remain. No October 22 authority.
6. Preserve all assignment/history, original trades/orders/sale selection, references/provenance,
   holds, memory and evidence. Validate offline and coordinate normal closure before replacing loaded
   normal output. This design authorizes no financial save, order, host enable or operational test.

## Alternatives and consequences

A single green execution/permission badge would conflate activity and authority. Reusing Running
as the last successful Active result would preserve the ambiguity. An unconditional “starts tomorrow”
message would promise behavior that neither starts a closed app nor qualifies a new dated scope.
Separate summaries require more space and passive observations, but make uncertainty and the next
operator action visible without changing execution policy.

The [design draft](../concepts/real-tfsa-execution-ui-design-draft.md) contains wireframes, state
meanings, wording and offline acceptance cases. Layout/wording remain reviewable; implementation
must not treat this accepted presentation scope as permission to change scheduling or authority.

## October 30 — accepted dashboard placement

Joseph selects the **Status overview** concept and directs that controls live on an **Engine**
page. This supersedes the account-screen placement in the October 20 draft, not its separation
of observed activity, permission and consequential actions. The immediate problem is duplicated
status and controls consuming Trading space; the parent problem is understandable engine operation;
the goal is a clear dashboard with deliberate access to existing controls.

Keep Engine, Real execution and History checks in the global header, with an Open Engine button
and an Engine navigation entry. Move engine start/stop, execution setup/host stop, strategy-switch
review and history checks there. Remove the Trading execution side panel; keep its strategy picker
and account-specific buying controls. Reuse current account selection, command eligibility and
review workflows. Show blocker/stale explanations, actual check times and detailed history notices;
unknown or incomplete history must not become a healthy status merely because notice severity is
Information. The existing October 29 readiness projection remains unchanged, including its two-minute
receipt-age check; the earlier five-minute draft is not a new requirement in this layout change.

The compact history summary interprets only the existing known notice format, preserving the full
message and observation time on Engine. Unrecognized notices remain explicit review items. No new
polling, engine ownership, financial authority or recovery action follows from navigation. Compact
status with controls retained in the header was considered; the selected overview moves those
actions to the dedicated page. Installation and runtime verification remain separate from source
implementation and offline rendering; see [current status](../project-status.md).

## Review questions

1. Why is a saved host grant insufficient evidence for a Running status?
2. Can the five-minute stale display warning change trading permission?
3. Which October 21 actions remain separate after improving the setup layout?
