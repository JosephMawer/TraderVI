# ADR-0044: Unlinked Real position monitoring

- **Status:** Accepted
- **Date:** 2026-09-02
- **Domains:** architecture, data-pipeline, market-microstructure, risk-management
- **Related:** ADR-0015, ADR-0028, ADR-0031, ADR-0032, ADR-0037, ADR-0039

## Context

The immediate problem is that an operator-confirmed historical Real holding can
exist correctly in `ActivePosition` and `TradeLog` while remaining invisible to
the Trading tab and excluded from the shared monitor because it has no
`OriginalPickId`. The parent problem is supervising actual holdings without
inventing Delphi provenance or weakening the rules for experimental Ghost
positions. The root goal is a trustworthy advisory system that can help monitor
real capital while keeping broker facts, simulated execution, and official
calibration distinct.

ADR-0037 deliberately requires a saved Buy pick for its WPF entry path. That is
the right provenance rule for Ghost experiments and for Real fills opened from a
recommendation, but it is not a truthful representation of a broker holding
that predates TraderVI enrollment or was acquired independently of Delphi.

## Decision

Include an active position in the Trading dashboard and shared monitor when it
either has an `OriginalPickId` or has `ExecutionMode = Real`.

1. Continue excluding unlinked Ghost rows. A simulated position without a
   Delphi pick remains outside the accepted dashboard/monitor workflow.
2. Display an unlinked Real row as an `unlinked historical holding` and include
   its attached BUY and later SELL rows in Trade history.
3. Begin durable monitoring from the import/enrollment record's `CreatedUtc`.
   Do not backdate intraday receipt availability to the broker purchase date.
4. Apply the ordinary delayed swing-management limits to an unlinked Real
   holding, but never grant ADR-0028's conditional loss exception without the
   position's original Delphi-pick provenance.
5. Preserve ADR-0039's hard boundary: every Real exit remains an operator-
   reported all-shares broker fill. No policy result may close it automatically
   or submit an order.
6. Keep unlinked operational holdings outside the immutable official Delphi
   calibration population.

## Alternatives considered

- **Attach an unrelated or later Delphi pick.** Rejected because it would
  manufacture provenance and contaminate attribution.
- **Display the holding but leave it unmonitored.** Rejected because the
  dashboard would show stale P/L and fail ADR-0039's supervision purpose.
- **Allow all unlinked Ghost and Real rows.** Rejected because it would silently
  broaden the accepted experimental Ghost-entry population.
- **Backdate monitor availability to the broker purchase.** Rejected because
  TraderVI did not collect durable intraday receipts before enrollment.

## Consequences

- Historical or discretionary Real holdings can be represented honestly and
  supervised in the same Trading workspace.
- The monitor's enrollment horizon may be shorter than the broker holding
  period; the UI and durable notes must retain that distinction.
- Unlinked Real holdings use conservative ordinary loss handling because they
  cannot establish the fresh Delphi provenance required for the exception.
- No database migration, broker integration, automatic Real exit, or official
  calibration change is introduced.

## Review questions

1. Why may an unlinked Real holding be monitored while an unlinked Ghost row is excluded?
2. Why does monitoring start at durable enrollment rather than the broker purchase date?
3. Why can an unlinked Real holding never receive the fresh-Delphi loss exception?
