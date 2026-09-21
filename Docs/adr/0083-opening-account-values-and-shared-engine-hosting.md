# ADR-0083: Opening account values and shared engine hosting

**Date:** 2026-09-21

**Status:** Accepted direction; broker day-value reader implemented and tested offline. Native morning
availability, valuation alignment and durable daily-reference integration remain unverified.

**Domains:** architecture, data-sources

**Related:** ADR-0051, ADR-0053, ADR-0062, ADR-0072

## Decision and practical consequence

Acquire and retain the real TFSA's correctly dated opening values using shared Core code. Normal
operation should be a continuously running engine, eventually started before the market session by
Windows Task Scheduler independently of WPF. Keep the current increment small enough for that host
to reuse directly. A late startup alone is not a new reason to pause buys or reset loss measurements.

This supports carrying existing account risk checks into the exact promoted paper strategy, while
keeping real holdings, cash, financial history and protections separate from paper evidence. Existing
loss limits still apply; neither missing evidence nor restarting establishes a new account opening.

Prefer a previously saved, verified reference for the same account/session. Where it is missing,
investigate recovery from dated broker values and existing market/account evidence before proposing
new missing-data behavior. A new blanket morning pause was discussed but has not been accepted.
Host extraction, scheduler installation and automatic trading are separate unfinished work.

## Evidence found locally

The operator-supplied September 16 activity capture includes three successful
`FetchAccountGraphData` responses for `ONE_DAY` / `REGULAR`. Each contains 79 CAD account-value
points from 09:30 through 16:00 Toronto in five-minute steps, including 09:30 and 09:35, with explicit
timestamp offsets. Returned account identities match their requests. `previousClose` is a separate
point; it is not a substitute for the opening. Net-deposit totals are constant within those captured
days, which does not prove complete cash-event history or settlement timing.

The capture also includes a 24-hour series and sampled multi-day series. The new reader requests
only the regular-session daily series. Local inspection exposed a PowerShell date-conversion hazard:
automatic JSON DateTime conversion followed by reparsing can shift times twice. The evidence script
preserves raw timestamp strings, then converts their explicit offsets once. No account values,
identifiers, credentials or response bodies are copied to source or test fixtures.

This is historical response evidence. It does not establish when the opening point first becomes
available each morning, guarantee service availability, or prove that broker valuation matches each
strategy family's opening method.

## Existing engine behavior and remaining alignment

- Delphi Live values its saved opening cash/positions using the Open price of the first five-minute
  bar. The observation becomes available at that bar's end. Its collection, valuation, persistence
  and host-lease machinery already live in Core; WPF currently supplies the running host.
- Daily Shadow currently saves cash plus held shares times their stored LastPrice when creating the
  session. The activation timestamp does not prove those stored marks are a fresh market opening.
- The generic recommendation engine consumes reported opening/high references; it does not yet read
  reconciled broker references. Do not silently replace either family's definition during cutover.

Thus historical price bars alone are insufficient: real cash and quantities must also refer to the
same boundary, with intervening fills/cash movements accounted for. Native validation must compare
the broker's dated account point with the selected strategy's intended valuation. Resolving any
material difference is a focused behavior decision, not a reason to build a second recovery engine.

## Implemented increment

`WealthsimpleAuthClient.ReadTfsaDayValuesAsync` uses the existing authenticated, account-scoped read
path and one fixed query. It returns immutable timed CAD values, cumulative net deposits, receipt
time and a separate previous close. `FindExact` requires a full dated instant and performs no nearest-
point lookup, interpolation, previous-close fallback or restart-time substitution.

Wrong ownership/currency, incomplete monetary fields, missing timezone offsets, duplicate/reversed
timestamps, future points, and partial/error responses produce unavailable/unsupported evidence.
An empty series is distinguishable from a failed request. The result is evidence only, not a saved
baseline, a complete cash-flow ledger, a strategy-readiness result or an order instruction.

No WPF screen, second scheduler, background broker poller, multi-day reconstruction framework,
database schema or new trading pause is added. The new method has no automatic caller yet.

## Validation and next step

- 25 new synthetic cases pass; all 158 account-client tests and all 1,488 Core tests pass.
- Tests cover late reading, summer/winter offsets, exact date/time selection, missing points,
  malformed/conflicting evidence, account authority, cancellation, read failures and safe display.
- Tests/builds use isolated outputs; existing compiler warnings remain. No fresh dependency-security
  audit, WPF/full-solution build, database operation, broker request/order, task installation or host
  restart was run. Evidence: `artifacts/tfsa-opening-20260921/`.

Next verify native morning availability and strategy-compatible valuation, then wire the minimal
saved daily reference into ongoing cash-flow/risk tracking. Reuse that path when the standalone
engine is extracted. Existing baselines/highs/holds must survive strategy changes and restarts;
initial setup is not repeated merely because the host starts late.

## Review questions

1. Why does a dated account chart point help recovery without automatically proving strategy readiness?
2. Why must both the holdings/cash and their prices refer to the same measurement boundary?
3. Which parts should a scheduled standalone engine reuse from the current application?
