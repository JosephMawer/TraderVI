# ADR-0083: Opening account values and shared engine hosting

**Date:** 2026-09-21

**Status:** Accepted direction; broker day-value reader, strategy-defined boundary valuation and
durable continuation for verified intervals without capital flows are implemented/tested offline.
Cash-flow adjustment, native source qualification and operational integration remain unfinished.

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

## Initial day-reader increment

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

## Initial day-reader validation

- 25 new synthetic cases pass; all 158 account-client tests and all 1,488 Core tests pass.
- Tests cover late reading, summer/winter offsets, exact date/time selection, missing points,
  malformed/conflicting evidence, account authority, cancellation, read failures and safe display.
- Tests/builds use isolated outputs; existing compiler warnings remain. No fresh dependency-security
  audit, WPF/full-solution build, database operation, broker request/order, task installation or host
  restart was run. Evidence: `artifacts/tfsa-opening-20260921/`.

## September 21 continuation increment

`BrokerAccountSessionValuation` values the actual cash and holdings reconciled at the measurement
boundary. Reading today's changed book at noon does not establish what was held at 09:30. Recovery
requires the opening book as well as its identified price evidence. Delphi Live uses the first
five-minute bar's Open for opening and the exact final bar's Close for closing. Daily Shadow preserves
its saved-price book definition for both boundaries. These definitions are explicit source contracts;
the broker graph is a dated cross-check, not an automatically interchangeable source. Missing or
ambiguous evidence produces no substitute value. Recovered evidence keeps its later observation time
and does not become on-time paper-performance evidence. Cash-only books need no fictitious price bars.

`BrokerAccountRiskContinuation` and its shared workflow/repository preserve the first saved session
reference, advance the high only on a completed closing valuation, and carry the capital hold across
sessions. The next reviewed calendar session clears only the prior daily hold, following existing
policy. Ordinary hold observations can add holds but cannot release them. Missing session closes must
be recovered in order; a restart cannot invent closing history. Initial setup after the close or on a
non-session date does not require an invented earlier close. Initial tracking starts at the broker
observation's actual timestamp, now retained separately from the baseline save time.

Each append pins its preceding revision. SQL account fences and unique predecessor constraints prevent
competing continuations. Request recovery returns the same saved event after a lost response, even
when a new read fails. Altered evidence cannot reuse a request. Exact timestamp/content readback checks
accompany the immutable event history. Initial baselines, imported history, paper accounts, legacy
account reports and symbol protection are not rewritten. Migration 039 is additive and prepared only;
both 035 and 039 require separate installation authority and verified backups.

**Limited source contract:** continuation currently accepts only intervals with positively reconciled
evidence of no external deposits or withdrawals. Equal cumulative net-deposit totals are insufficient:
an intervening deposit and withdrawal can cancel. The current order importer and day graph do not
produce this complete cash-event proof. There is no production caller, no engine-input cutover and no
new morning-pause rule. This is a tested development increment, not completed operational item 1.

**Open cash-flow decision:** recommend preserving the measured loss percentage across a confirmed
deposit/withdrawal (an account down 2% remains down 2%). The alternative preserves the dollar loss but
changes its percentage and therefore the remaining distance to the existing guard. The user has been
asked this single question; neither formula has been accepted or implemented in this increment. After
the answer, define the necessary event/value evidence and implement confirmed-flow continuation,
including unexplained movements and existing holds. Do not mistake cumulative totals for timed events.

Validation: 64 new valuation/continuation cases and all **1,552 Core tests** pass with isolated outputs.
The isolated SSDT build, SQL parsing and canonical/migration parity checks pass. A separate source copy
of the reviewed broker/baseline prerequisite checkpoint passes all **1,465 tests**, establishing that
it does not depend on the remaining uncommitted work; that copy passes **1,529 tests** after adding
this continuation increment. Existing compiler warnings remain; no fresh
dependency-security audit or complete solution build ran. Evidence is under
`artifacts/tfsa-risk-continuation-20260921/`. No database mutation, native broker call, order, scheduled
task, operational host launch or app restart was performed.

Next finish the cash-flow behavior and its evidence source, qualify native morning publication and
valuation compatibility, and integrate/verify setup and continuation under their separate operational
authorizations. Keep source/projection work in Core for the eventual standalone host. Item 1 remains
open; pure calculations and prepared storage do not establish an operational account reference.

## Review questions

1. Why does a dated account chart point help recovery without automatically proving strategy readiness?
2. Why must both the holdings/cash and their prices refer to the same measurement boundary?
3. Which parts should a scheduled standalone engine reuse from the current application?
