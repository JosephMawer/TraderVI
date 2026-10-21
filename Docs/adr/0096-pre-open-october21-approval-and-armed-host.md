---
cssclasses: [executive-brief]
---
# ADR-0096: Pre-open October 21 approval and armed execution host

**Date:** October 21, 2026. **Status:** Accepted by the operator's implementation request.
**Domains:** architecture, risk-management.
**Related:** ADR-0079–0083, ADR-0086, ADR-0092, ADR-0095.

The immediate problem is requiring approval clicks after the market opens. The parent problem is
predictable daily operation with deliberate financial permission. The root goal is the reviewed
Delphi Live Normal strategy managing the same TFSA while preserving financial history and risk controls.

## Decision

For the existing October 21-only continuation, permit explicit review/approval on October 21 before
09:30 Toronto, followed by separate manual host enable. This supersedes only the regular-session-only
approval/enable timing in ADR-0095 and the October 21 contract. The user still performs both financial
authorization actions. Saving approval creates no host grant; enable arms the running desktop.
It does not start an app, sign in automatically, bypass data gates or authorize October 22.

Persist an explicit `PreOpenApproved` flag in the linked continuation. Its default is omitted so
historical continuation hashes remain stable. A flagged confirmation must belong to October 21
before 09:30; ordinary confirmations retain their regular-session rule. Original assignment,
baseline/references, holds, memory, trades, cumulative ceiling and original-request recovery remain.
Fresh cash/history/order/risk and immutable-storage checks still precede approval; the two-minute
review and thirty-second account limits remain unchanged. Stop the old host before review/save.

Pre-open source review verifies the exact stored Normal definition/current running build and latest
October 21 OfficialPaper run: Valid, October 20 market-data date, matching daily version/configuration/
models, created before review/open. Stored Normal evidence must be from at least the preceding regular
session and cannot be future dated. This is definition readiness, not current-session signal freshness.
No timestamp is relabelled and no Live evaluation/session is fabricated. Approval repeats these checks.
An invalid latest daily run cannot fall back to an older valid run.

## Execution and cutoff

The enabled host waits until the calendar's 09:30 open; its status reports armed/waiting. Core also
refuses pre-open ticks for this scope before account reads or any execution save/action. The separate
review-only risk-evidence option permits pre-open financial review but is never supplied to execution.

At and after the open, until the first owned order, the desktop requires enabled immutable storage
and a fresh (two-minute), exact matching Normal source/build. The existing execution workflow obtains
new broker facts and checks the unchanged cash-only book, orders and risk holds before first action.
Existing strategy input, source continuity/adoption, entry/confirmation, reservation, native preflight,
duplicate prevention, full-fill-only outcome restrictions and protection controls remain authoritative.
Data unavailability blocks/retries through the existing host; it does not require another approval
unless authority/definition/build or saved financial facts require recovery.

Automatic checking begins at the first eligible host poll at/after 09:30, subject to existing paper-work
coordination. There is no guarantee of a purchase at exactly 09:30; fresh source collection and normal
strategy gates may delay or reject it. Keep the desktop running and signed in; this remains a supervised
session. New buys stop at **16:00 Toronto October 21**, including reservation/transmission rechecks.
Restart recovers only the same enabled exact-build grant; a changed build, stopped/paused mode or
missing/uncertain state cannot silently inherit permission. No recurring renewal follows.

## Verification

Use offline tests for pre-open save/recovery, separate enable, no pre-open tick/order, open/close
boundaries, changed account/holds/source/storage, stale observations, wrong dates and original history.
Build standard Release output only after normal closure and independent zero-owner inspection.
Current status owns test/build/install evidence. Source validation does not prove native activation,
automatic fills or populated protection. No SQL schema change or operational rehearsal is required
by this source change; actual financial approval/enable remain operator actions.

## Review questions

1. Why does pre-open approval not make yesterday's market observations usable at 09:30?
2. What can delay or block an armed host after the open, and when does buying permission end?
