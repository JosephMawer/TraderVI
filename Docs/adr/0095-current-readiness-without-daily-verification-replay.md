---
cssclasses: [executive-brief]
---
# ADR-0095: Current approval without replaying completed broker verification

**Date:** October 20, 2026. **Status:** Accepted by the operator; implementation/offline validation
tracked in project status. No financial approval or broker verification is performed by this change.
**Domains:** architecture, risk-management.
**Related:** ADR-0079–0081, ADR-0083, ADR-0086, ADR-0092, ADR-0094.

## Decision and practical consequence

Tomorrow's approval checks the current account and dated permission. Completed manual broker tests
remain development evidence, with their real dates and limits; they are not another daily trading
permission. The operator explicitly requests this change after questioning the continuing dependency
on the original verification trades.

The immediate problem is repeating first-day qualification during continuation. The parent problem
is understandable, recoverable real-account operation. The root goal is a reviewed Normal strategy
managing the permanent TFSA with continuous financial history and risk controls.

This supersedes ADR-0092 and the October 21 contract **only** where they require a new nine-reference
qualification packet, individual rereads of the four verification IDs, or a current-build native
verification checkpoint/restart before October 21 approval. It also updates ADR-0094's inherited
qualification wording. Original qualification evidence and first-session/production paths remain
historical and unchanged. No new qualification claims are issued or old proof dates/builds relabelled.

## Current approval and preserved controls

**October 21 timing amendment:** [ADR-0096](0096-pre-open-october21-approval-and-armed-host.md)
supersedes the regular-session-only review/enable wording below for this dated continuation only.
Pre-open explicit approval and separate host arming retain current financial checks and the cutoff;
actual execution still waits for the open and fresh account/Normal/risk readiness.

1. Recover the existing assignment, original financial baseline, risk history and saved October 20
   close revision. Preserve original sales/IDs, provenance, holds, protection, memory and prior approval.
2. During October 21 regular trading, confirm no outside account changes, stop the old host and
   review fresh account/history/cash/buying power, pending or unresolved orders, current risk state,
   exact Normal/source/running build and installed immutable storage. The existing cash-only,
   unchanged-book shortcut remains; changed financial facts require separate review.
3. Use ordinary account reconciliation to detect missing/changed historical financial orders. This
   is accounting integrity, not a rerun of a test. Do not require old protected test files, manually
   select old sales again, create new test proof, repeat trades or restart to establish test status.
4. Save explicit October 21 approval separately from host enable. Recheck current source/storage
   before approval and current account/risk under the existing managed-session lease. Preserve
   append-only linked risk/permission records, exact revision fences and original-request recovery
   after a partial/lost-acknowledgement save. No regenerated request may reset references or holds.
5. Retain the cumulative purchase allowance, strategy gates, full-fill-only outcome restrictions,
   uncertain-order review hold, single execution owner, durable intent-before-send and protection.
   Purchases stop at the reviewed October 21 regular close (4 PM Toronto). No October 22 renewal,
   recurring authority, automatic app startup or inherited old host grant follows.

## Development verification

**October 20 operator-path refinement:** the operator accepts skipping the desktop paper-cycle drain
when opening setup, refreshing saved readiness or enabling the already approved host. These actions
retain their saved-state reads/SQL fences and exact assignment/build/continuation matching; enable
does not invoke execution. Account/source review, approval and actual host execution retain their
paper-work admission. No freshness deadline, financial authority or session timing rule changes.
The [continuation contract's operator-path review](../reviews/delphi-live-continuation-contract-20261021.md#accepted-operator-path-simplification--october-20)
records the finding, purpose, existing protections and tradeoff. ADR-0094's presentation design stays separate.

Keep native manual full-fill/restart observations in their existing dated reports/protected files.
They establish only the paths actually observed. Automatic submission/fills, populated protection,
partial/cancel/uncertain/power-loss behavior remain unverified where previously unobserved.

Changes to submission, identity/duplicate prevention, order recovery, accounting, persistence or
protection require focused offline validation of the affected behavior. Assess separately whether a
particular change needs an explicitly authorized native check; neither every new day/build nor a
label/icon change automatically requires another broker trade. Missing runtime financial evidence
still blocks approval; absence of an old test certificate does not.

## Acceptance

- Current-build continuation can be reviewed/saved without a historical qualification packet or
  new verification restart; approval creates no order and leaves the host off.
- Stale/changed account or source, altered history/references/holds, missing immutable storage,
  insufficient allowance, expired permission, conflicting revisions and old host authority still block.
- Lost risk/session acknowledgements recover the original request without duplicate financial events.
- First-session and production qualification requirements retain their existing behavior.

## Review questions

1. Why is comparing financial history different from requalifying an old broker test?
2. What current facts can still stop approval after the original test is complete?
3. Which software changes warrant focused retesting, and what does a passing manual test not prove?
