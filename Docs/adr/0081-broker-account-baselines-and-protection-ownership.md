# ADR-0081: Broker account baselines and protection ownership

**Date:** 2026-09-16 Toronto (September 17 UTC)  
**Status:** Accepted behavior; initial risk-reference preparation, reviewed save/recovery workflow and
SQL repository implemented in source. Migration 035 is prepared, not installed. Operator UI,
continuation orchestration, projection and protection handover are now source/tested under
[ADR-0084](0084-supervised-broker-strategy-session.md); native qualification and rollout remain unfinished.
**Domains:** architecture, risk-management  
**Related:** ADR-0062, ADR-0073, ADR-0079, ADR-0080

## Problem

The immediate problem is establishing the real account's loss references and protection ownership.
This belongs to connecting reconciled broker facts to strategy evaluation. The root goal is an exact,
reviewed paper strategy managing the permanent TFSA, with continuous real history and independent
paper evidence. The account is already linked and its 32-order native history refresh was confirmed
September 16 at 22:06 Toronto. Repeating import setup is not this milestone.

## Accepted decisions

1. **Initial risk references need no arbitrary waiting period.** If no earlier applicable reference
   exists, setup may establish the first one from the verified current TFSA value. Record its source
   observation and actual starting boundary. A midday setup value is not the market-opening value;
   a starting drawdown reference is not evidence of the account's lifetime highest closing value.
   Measure from the recorded boundary and disclose the incomplete earlier period. Preserve known
   applicable references and existing holds. Missing records after a failed read are not proof that
   no prior baseline exists. Promotion, import refresh and repeated setup cannot reset a baseline.
2. **Use confirmed broker purchase availability.** Start with the lower of fresh CAD cash and buying
   power, then apply strategy sizing and order reservations. Do not count anticipated sale proceeds,
   pending deposits or an order-submission acknowledgement as available funds. Required non-overlap
   exits must be confirmed and reconciled before any new buy. This does not impose a separate wait
   for a reconstructed settlement schedule when the broker already reports usable funds. Cash,
   buying power and settlement provenance remain distinct facts; reservations must not be deducted
   twice if already reflected by the broker.
3. **Transfer protection at activation.** During required exits and reconciliation, retained positions
   remain with their current protection owner. Transfer responsibility once to the incoming strategy
   at the recorded activation boundary, carrying forward established protective exit levels. Preserve
   actual quantities, costs, position history and account holds. Outstanding or uncertain sells must
   be resolved before assuming retained shares are available. The current owner can be an existing
   advisory/operator workflow; this decision does not give that workflow automatic order authority.
4. **Initialize missing position protection from real evidence.** Where no saved protection exists,
   initialize the incoming rules using actual cost and fresh price evidence at activation. Do not copy
   a paper position's highs, cost, age or protection state, and do not invent unseen historical highs.
   Existing verified protection still carries forward. Missing required cost/price evidence remains
   a real blocker, rather than a reason to fabricate a default.
5. **Adjust loss references for confirmed deposits and withdrawals.** A withdrawal is not a trading
   loss, and a deposit must not conceal one. Confirmed external cash movements adjust the applicable
   reference values while preserving the measured loss percentage (explicitly accepted September 22);
   unexplained movements pause new buys until reconciled. Existing holds do not clear merely because
   cash moves. The append-only adjustment is tested under ADR-0083; complete broker cash evidence and
   effective timing remain to be qualified. The initial-baseline table does not store later events.

The operator's first question about why buys would wait clarified decision 1: waiting for an undefined
historical reference is not mandatory. Establish a documented first reference during setup where none
exists. This does not authorize deleting/relinking history, clearing holds or resetting protections.
There is no change to the promoted strategy's loss thresholds or the independent paper policies.

## First source increment

`Core/Trader/Promotion/BrokerAccountRiskBaseline.cs` prepares immutable initial risk references from
an already saved linked broker observation and an explicitly complete read of existing risk history.
It reuses the import's reconstruction, identity, conflict and two-minute freshness checks. The broker
observation must belong to the current Toronto date and have a positive account value. A saved import
by itself remains insufficient to activate or trade.

Each reference records a value, source category, evidence ID and evidence time. The prepared baseline
pins the broker binding/import, setup time, Toronto date and source report/recommendation identities.
A valid same-date reported session opening is retained. Otherwise first setup uses the current
observation with the distinct `SetupObservation` label. A previous session's opening is not relabelled
as today's opening. Known account highs remain available even when a newer record omits a field.

Existing recommendation memory supplies the carried risk holds and symbol protection. Ordinary
records cannot lower known highs or remove same-session holds. A later, separately recorded explicit
capital review remains authoritative: setup must not resurrect a legitimately reviewed old high or
released hold. Setup itself does not perform daily rollover, release a hold, initialize position
protection, transfer its owner or change a holding. An already established baseline blocks this
initialization function; subsequent sessions need the future continuation workflow.

`CanEstablish` describes preparation only and must never be treated as `CanTrade`.

## Reviewed storage increment

`BrokerAccountRiskBaselineWorkflow` permits preview without saving. Explicit apply requires the exact
reviewed source and risk-state fingerprint. The SQL repository obtains import, report, recommendation
memory and existing-baseline presence under the existing account/strategy/control fences in one
serializable transaction, then rebuilds the preparation before inserting. Changed broker observations,
references, holds or baseline presence require another review. An exception propagates; a failed query
never becomes an empty account history.

Migration `20260917_035_AddBrokerAccountRiskBaseline.sql` creates one immutable table and its update/
delete rejection trigger. Unique account/binding constraints permit only one initial baseline. Foreign
keys bind the exact account import and any existing account report/recommendation batch. Setup leaves
all original history, financial records, protection memory and assignments intact. Subsequent risk
events require a separate continuation design; this initial record is never an update target.

The request ID recovers a committed save after a lost response without needing a fresh broker read or
repeating initialization. A changed reviewer or reviewed content cannot reuse that request. Review
fingerprints sort symbol-state keys so dictionary order does not change request identity after restart.
Stored JSON has its own full-content hash; readback also checks relational identities and exact
`datetime2(7)` timestamps. Preparation time may advance at save, but source evidence times remain fixed.

Migration 035 is source only and requires separate installation authority and a verified backup.
There is no WPF/CLI caller or saved operational baseline in this increment. No account-input cutover,
broker operation, SQL transaction rehearsal or app restart has run. All 1,441 Core tests pass, including
58 baseline policy/workflow/readback cases. SQL parsing/canonical parity and an isolated SSDT build
also pass. Existing compiler warnings remain; no fresh dependency-security audit or full solution
build ran. Evidence is under `artifacts/tfsa-baseline-20260917/`.

## Remaining design and implementation boundaries

- **September 22 source checkpoint:** shared boundary valuation, daily references/holds and
  percentage-preserving cash-flow adjustments are tested offline. Initial baseline evidence retains
  actual broker observation time separately from save time. Migrations 035/039 remain uninstalled.
  Complete broker event/book evidence, native qualification and operational wiring remain open;
  see [ADR-0083](0083-opening-account-values-and-shared-engine-hosting.md).

- **September 20 sequencing clarification:** a dedicated baseline review screen is not required.
  Develop and test continuation against the existing baseline contracts first; a concise summary in
  account setup can support explicit review/apply later. Separately installed/verified storage and an
  established baseline remain prerequisites for operational continuation, not for its development.
- Qualify session opening/closing and cash-flow evidence, integrate the tested continuation, and
  finish gap recovery and explicit capital-review transitions. Keep the selected strategy family's
  valuation/timing rules rather than substituting an arbitrary late-day snapshot.
- Define stable real position identities across partial exits, manual additions and full close/reopen
  cycles. Existing reviewed legacy references must not transfer protection onto a different cycle.
- Preserve inherited protection levels with their causal timestamps. A floor established at activation
  cannot trigger a sale on an earlier bar. Different strategy families' internal state is not freely
  interchangeable; the handover must retain the protective obligation without pretending their five-
  and fifteen-minute histories are identical.
- Add ongoing reconciliation, source freshness/conflict handling and reservation accounting before
  switching recommendation inputs. Uncertain orders cannot become zero pending orders.
- Implement durable transition ownership and order recovery, then automation qualification and the
  separately authorized native order/lifecycle checks. The existing manual buy/sell functions remain
  the intended transport; none is invoked by the baseline policy.

The continuing [integration draft](../concepts/strategy-promotion-and-execution.md) records the source
map and implementation order. This ADR accepts the above behavior, not database installation, live
orders, a history correction, strategy activation or an operational application restart.

## Review questions

1. How can setup begin immediately without claiming to know the account's historical high?
2. Who owns retained-position protection while required exits are still reconciling?
3. Why must a failed history read differ from a successful read with no previous baseline?
