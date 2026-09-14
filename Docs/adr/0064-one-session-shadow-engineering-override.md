---
cssclasses:
  - executive-brief
---
# ADR-0064: One-session Shadow candidate override for engineering observation

- **Status:** Accepted for the operator-requested September 8, 2026 trial
- **Date:** 2026-09-08
- **Domains:** decision-engine, risk-management
- **Related:** ADR-0051, ADR-0055, ADR-0060, ADR-0062

> [!summary] Decision in brief
> Admit September 8's ranked candidates into the four existing Shadow portfolios to exercise paper trading after the missed overnight run. The exception changes candidate admission for one date; ordinary entry checks, risk controls and later market fills still determine what happens.
>
> **Interpretation:** this is an engineering trial. Its returns cannot validate the normal strategy because the daily report recommended Hold.

| Scope | Practical effect |
|---|---|
| Four portfolios; Top 3 and Top 5 per lens | Sixteen candidate assignments; admission does not guarantee a purchase. |
| One dated exception | Normal candidate selection resumes next session; test history and carried effects remain visible. |
| Preserved financial state | Existing cash, holdings and protective exits continue under the ordinary controls. |

**Implementation status:** applied at 11:02:32 Toronto. The [execution record](#execution-record) below describes verification at adoption; it is not a closing performance report.

## Context

The immediate problem is that the four Shadow portfolios cannot exercise their entry workflow today.
The overnight build guard stopped execution during concurrent development; the recovered daily report
was saved at 09:52, after Shadow's 09:30 deadline. Its published candidates are all Hold and ineligible,
with Breadth as their first failed gate. The parent problem is incomplete observation of the weekend's
portfolio changes. The root goal is to verify the paper execution lifecycle before relying on it.

The operator explicitly requested forcing the Top 3/Top 5 candidates for today's portfolio test.
This authorizes a bounded paper candidate-selection exception; it does not establish strategy
performance, authorize a real trade, or require synthetic/immediate fills.

## Decision

Use the existing four Shadow portfolios, preserving their holdings, cash, risk state and history.
Manual migration 030 adopts the exact recovered run `9491e421-ef3e-48b4-bcb7-389c42ec25e9` into the
four currently empty `NoValidDelphiRun` sessions for September 8 only. Each Continuation/Breakout
portfolio receives its published ranks 1 through its own limit of 3 or 5: sixteen account-candidate
assignments in total. The official run, ranks, Hold directions and failed gates remain intact.

An exclusive application lock fences the existing WPF monitor while all four session changes,
candidate inserts, visible name labels and append-only override events are applied in one transaction.
Every event records the prior session, previous display name, source run, exact admitted candidate IDs
and ranks, original gate result, actual adoption time and engineering-only evidence scope.
The original `NoValidDelphiRun` lifecycle events remain. No model, threshold, strategy assignment,
daily recommendation, position, order or capital row is rewritten to create the test.

The activation baseline is the next five-minute boundary after adoption. The controller requires a
bar end strictly after that baseline, so the first eligible five-minute bar begins after adoption.
Ordinary momentum qualification, missing/late/conflicting-data checks, slot and cash limits,
daily-loss/drawdown holds, stops, trailing protection and causal pending-fill behavior remain.
Existing holdings continue occupying slots. Admitting sixteen candidates does not promise sixteen fills.

Append `[TEST 2026-09-08]` to the four current display names. The dated label remains until deliberately
renamed because the history contains this intervention and resulting positions may carry forward.
Tomorrow's new session resumes ordinary candidate selection automatically; this exception cannot
admit candidates to another date. Do not force liquidation at today's close.

## Evidence and limitations

Treat this intervention and its carried effects as engineering evidence. Existing raw portfolio charts
still include its financial results; this operation adds no automatic comparison filter. Do not use
these returns as an unmodified-strategy comparison or independent validation of the failed daily gates.
Official Delphi/Athena evidence remains unchanged. Delphi Live remains separately unactivated.

The script is single-use, rejects pauses/risk holds, existing candidates/orders or a changed source,
checks full hashes of fourteen protected financial/control/evidence tables, verifies portfolio financial
state and schema preservation, and supports a full rollback rehearsal before application. A fresh
checksum-verified backup and hash-matched secondary copy precede application. SQL schema definitions
are unchanged; the dated script is a reviewed operational data transition.

After adoption, observe candidate evaluation, honest skips, later pending orders/fills, holding updates
and protective exits through the existing WPF collector. Read-only monitoring does not authorize more
forced fills, repeated overrides, cleared risk holds, app restarts or Delphi Live activation.

## Recovery

Any precondition/postcondition failure rolls back the whole transaction. After commit, the ordinary
Shadow pause control can block new risk while protective exits continue. Preserve all evidence and
financial history; do not delete sessions, backdate records or restore a whole database over subsequent
trades. A broader reusable late-start facility remains a separate design task.

## Review questions

1. Which two admission restrictions are bypassed, and which execution controls still apply?
2. Why can a forced candidate still receive no fill?
3. Why do engineering labels remain relevant after the session closes?

## Execution record

Applied at 11:02:32 Toronto on September 8. The four sessions are Active with ranks 1..3 / 1..5
for each lens, sixteen Pending candidate assignments, four override events and all four dated name
labels. The first permitted five-minute bar starts at 11:05; a first ordinary qualifying decision can
therefore occur after that bar completes and is collected. No order or fill was created by the script.

The final rollback rehearsal passed; application again matched all fourteen protected-table hashes,
portfolio financial state and schema identity. An initial rehearsal rolled back because empty-table
JSON produced a null hash; the final script normalizes that case to an empty array. No persistent
candidate/session change occurred in either rehearsal. The SQL/SSDT Release build passed without
warnings or errors. No Core behavior source changed, and no full solution or Core test run was needed.

Before application, a fresh 45,101,568-byte full backup passed checksum verification and its OneDrive
folder copy had the same SHA-256. Cloud synchronization remains independently unverified.
The applied script SHA-256 is `F64354DDC7103EA3CC35F703F55518D30483C906AF28B3DB92A670529013084C`.
Private backup, rehearsal, application and monitoring evidence is preserved under ignored
`artifacts/shadow-engineering-20260908/`. The operator's existing WPF process remains running;
no second host, model training, broker action or Delphi Live activation was started.

At 11:12 the existing WPF host evaluated all sixteen candidate assignments: seven Qualified,
two BelowPreviousSessionClose, six FallingFromPreviousFiveMinuteClose and one MissingEvidence.
It created seven ordinary pending Initial buys: Continuation Top-3/Top-5 have two/three;
Breakout Top-3/Top-5 have one/one. Every signal was received at approximately 11:12 and requires
the 11:15 five-minute fill bar; no early fill or order-time violation was found. All post-adoption
receipt groups inspected were Valid. The official published rows remain fifty ineligible Hold entries.
Later fill/exit observation continues under the read-only watch; candidate admission is verified,
and pending signals are not completed purchases.
