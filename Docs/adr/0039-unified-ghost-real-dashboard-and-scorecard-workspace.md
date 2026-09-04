# ADR-0039: Unified Ghost/Real dashboard and scorecard workspace

- **Status:** Accepted
- **Date:** 2026-08-28
- **Domains:** architecture, data-pipeline, market-microstructure, risk-management
- **Related:** ADR-0015, ADR-0021, ADR-0031, ADR-0032, ADR-0037, ADR-0038
- **Extended by:** ADR-0044, which includes unlinked operator-reported Real holdings in the dashboard and monitor without granting Delphi provenance; ADR-0047, which records confirmed non-Buy Real overrides as that same unlinked class; ADR-0048, which adds atomic opening and retry-safe full-exit receipts without deciding partial fills or commissions

## Context

The immediate problem is that advanced official-prediction scorecards exist in
Athena and optional CSV artifacts but are not visible in the desktop application,
while the Trading dashboard describes every tracked position as paper-only. The
user's five-share EDR holding is an actual TFSA purchase, even though its current
TraderVI row was created in the ghost ledger.

The parent problem is making Delphi, calibration, simulated execution, and
operator-reported real holdings useful together without letting one ledger
impersonate another. The root goal is a trustworthy decision-support system that
can improve Delphi from controlled evidence and help supervise actual capital
without silently gaining broker authority.

## Decision

### Display the official scorecards in WPF

Add a read-only `Scorecards` workspace to TraderVI.WPF. It invokes the same
`OfficialPredictionScorecardCalculator` as Athena over the same immutable
official evidence query. It shows coverage/readiness, model probability metrics,
reliability buckets, probability deciles, Continuation/Breakout rank results,
and diagnostic slices.

The workspace does not require CSV export, run Athena, mature outcomes, change a
model, or write SQL. CSV remains an optional machine-readable artifact rather
than a normal operator workflow. Primary metrics remain hidden below the
accepted 95% usable-outcome floor.

### Give every operational position and trade an explicit mode

Add durable `ExecutionMode = Ghost | Real` and optional `AccountLabel` fields to
`ActivePosition` and `TradeLog`.

- `Ghost` represents a simulated fill and must not carry a brokerage account.
- `Real` represents a fill the operator says already occurred at a broker and
  requires an account label. It does not mean TraderVI submitted or verified the
  order.
- Migration 013 classifies every historical row as Ghost. It never infers that a
  historical record was real from its symbol, shares, notes, or similarity to a
  brokerage holding.
- A Ghost-to-Real correction is an explicit confirmed operation and appends an
  immutable `PositionExecutionAudit` row. The current EDR row may be reconciled
  only through this path after the migration is applied and its shares/fill are
  checked by the operator.

Show both an icon and the words `GHOST` or `REAL`, plus the account label, on
position and trade rows. Report Ghost and Real open counts and P/L separately;
do not combine them into a performance score that could be mistaken for model
calibration.

### Keep real execution manual and outside the broker

The Delphi picker can create either a Ghost position or an operator-reported
Real position from a saved Buy recommendation and supplied fill. A Real entry
requires confirmation that the fill already occurred and an account label.

The monitor evaluates durable market evidence and the accepted exit policy for
both modes. Automatic exit execution is permitted only when the position mode
is Ghost. A Real exit alert is displayed as a manual-action signal; it cannot
close the position or place an order. The operator may later record the actual
all-shares broker sell fill in WPF, after a separate confirmation, and that
manual reconciliation closes only TraderVI's tracked Real row.

No broker adapter, credential, order ID, order submission, order cancellation,
partial-fill handling, or broker-state claim is introduced. Real trade records
remain outside ADR-0038 official prediction calibration.

### Preserve safe transition behavior

Migration 013 is a reviewed manual migration and is not applied by an application
build. Before it is applied, existing repositories project legacy positions and
trades as Ghost so the monitor and history remain usable. Any attempted Real
entry or reconciliation fails with a migration-required explanation before a
Real row is written.

## Alternatives considered

- **Use only a ghost icon inferred from notes.** Rejected because presentation
  cannot enforce exit authority, account identity, historical reporting, or
  database integrity.
- **Import or query the broker immediately.** Rejected because credentials,
  order identity, reconciliation, partial fills, and independent safety controls
  have not been designed or authorized.
- **Let an exit signal close a Real row automatically without sending an order.**
  Rejected because the dashboard would then claim a sale that may not have
  happened at the broker.
- **Remove CSV output now that WPF can display scorecards.** Rejected because the
  existing export is inert, versioned, useful for audits, and does not burden the
  normal WPF workflow.
- **Rewrite the existing EDR row as Real during migration.** Rejected because a
  schema migration cannot safely infer a user's broker truth.

## Consequences

- Scorecards become visible in the application without creating a second metric
  implementation.
- Ghost and Real operational performance are clearly separated from each other
  and from official model calibration.
- Real holdings can receive the same delayed monitoring evidence and policy
  signals, but the operator remains the only bridge to a broker.
- Applying migration 013 requires the normal reviewed-backup and explicit manual
  authorization workflow. Until then the application remains Ghost-only.
- Version 1 records an all-shares real exit. Partial fills, cash balances,
  commissions, broker identifiers, imports, and order routing remain deferred.

## Review questions

1. What does `Real` prove, and what does it explicitly not prove?
2. Why must a Real exit alert remain open until an actual broker fill is entered?
3. Why is the existing EDR row not automatically converted by migration 013?
4. Which data may enter the official Delphi prediction scorecards?
