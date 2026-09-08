# ADR-0061: Portfolio comparison workspace

**Status:** Accepted navigation; descriptive display implemented; cross-family promotion deferred
**Date:** 2026-09-07
**Domains:** user-interface, architecture

## Context

The immediate problem was an overcrowded Portfolios page combining account operations with comparison.
Its parent problem is evaluating independent strategies. The root goal is to choose an evidence-backed
strategy deliberately and manage its primary recommendation activity through Trading (ADR-0055).
The operator preferred the table concept, then approved three linked views and a review of one challenger
against the current primary. Existing daily/Live accounts do not yet share a complete promotion identity.

## Decision

- Portfolios opens to **Compare**, with **Performance** and **Strategy review** as secondary tabs.
- Tabs share account selection, period and scope. Compare has a table and selected-account details;
  Performance has saved closing-value charts and cards; Review places one challenger beside the saved
  daily recommendation assignment and tracked-position exit assignment.
- Review promotion is navigation, with no assignment side effect. Current-primary account performance
  is unavailable unless a complete matching account identity can be established. Do not relabel a daily
  lens, Real balance, Live operational champion or Settings policy name as that complete identity.
- Paper accounts are the default scope; Real/operator Ghost accounts remain reference-only. Existing
  paper controls and activity remain under a collapsed account-tools expander. The explicitly named
  Start daily Shadow tool operates on that family regardless of which challenger is being inspected;
  the old selection-scoped commands retain their family guard. Starting still requires confirmation.
- No common promotion writer is introduced. Settings remains the existing route for its supported
  assignments, retaining ADR-0060's paused-and-empty requirements. Full strategy adoption is separate.

## Initial display contract

The implemented measurement is descriptive account history, not an official cross-strategy scorecard.
Use saved completed daily Shadow session closes and complete Delphi Live closing marks. Neither collect
new prices nor reconstruct missing marks. Compare fixed-capital paper-account changes after whatever
costs each engine already recorded; do not claim identical execution assumptions across families.

All saved history, last 30 calendar days and last 90 calendar days are anchored to the latest saved close.
The shared dates are the union of saved dates across loaded paper accounts. The period begins at the first
observed date in that window. Real references do not alter it. No independent date shifting per account.
Return requires positive value at the exact shared start and a nonnegative value at the exact end, with
at least two dates. Zero ending value is a total loss. Invalid/incomplete/duplicate observations are null.
Interior gaps do not prevent a known endpoint return but break the chart and suppress maximum decline.

The chart rebases start value to 100. Maximum closing decline is the deepest observed closing-value
fall from a prior window peak; it is not intraday or lifetime drawdown. A union of observed dates cannot
prove complete market-session coverage, so labels explicitly avoid completeness or readiness claims.
Account value/cash/P&L are separately labelled snapshots or lifetime figures, outside the period return.
Current strategy names never retroactively attribute earlier account performance to that version.

## Consequences and limits

No schema, engine rule, assignment, account, trading record or market-service behavior changes during
view navigation. The history reader is SELECT-only. Refresh is serialized; failures are visible rather
than becoming numeric zero. The page still loads only the latest daily Shadow generation and the Live
accounts exposed by the existing reader. Earlier Shadow generations and arbitrary comparison dates
remain future refinements.

Common complete-strategy identity, prospective comparable evidence, primary-account mapping and the
durable human-approved recommendation route remain deferred under ADR-0055. Better-looking results
must not silently relax that boundary. See [measurement notes](../concepts/portfolio-comparison.md).

## Review questions

1. Why must all compared returns use the same start and end dates?
2. Why is a current strategy name insufficient to identify the strategy responsible for account history?
3. What changes when Review promotion opens, and what implementation is still required to apply it?
