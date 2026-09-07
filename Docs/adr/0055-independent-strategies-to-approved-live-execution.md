# ADR-0055: Independent strategies to approved live execution

- **Status:** Accepted direction; common strategy promotion and broker execution remain to be implemented
- **Date:** 2026-09-06
- **Domains:** architecture, decision-engine, risk-management, market-microstructure
- **Related:** ADR-0022, ADR-0039, ADR-0051, ADR-0053, ADR-0054

## Context

The immediate problem is making the relationship between Delphi, Delphi Live, paper portfolios and the
future live trader explicit. The parent problem is establishing which complete strategy performs well
enough to adopt without mixing its evidence with another strategy's trades. The root goal is dependable
TSX momentum advice, followed by explicitly authorized real execution of a proven strategy.

The operator confirmed that independent paper portfolios are intentional: compare them, choose a
successful strategy, promote it to the live trader in recommendation mode first, and later execute its
decisions through a Wealthsimple client. This is the accepted product direction, not an authorization to
activate trading or connect a broker.

## Decision

1. **A portfolio is an independent account following an identified strategy.** Each paper account owns
   its cash, positions, pending actions, fills and performance history. Strategies may share immutable
   market facts and Delphi's daily candidates. They must not share a mutable balance, take over another
   account's positions, or combine results into a fictitious account. Starting a new comparison run
   preserves the previous run's separate history.
2. **Promote the complete strategy.** Selection, entry, exit, sizing, risk rules, data requirements and
   execution assumptions together define the behavior being evaluated. A trained model is one possible
   input to that behavior. Training success, a model metric, a highest current account balance, or a
   portfolio's display name is not strategy-promotion authority. Meaningful changes need traceable
   strategy/model/policy versions and the applicable evidence and review boundary.
3. **Compare fairly before choosing a winner.** Review returns alongside drawdown, downside outcomes,
   coverage, consistency across comparable sessions, capital and cash-flow differences, and fill/cost
   assumptions. Preserve excluded, losing and abandoned experiments. Historical replays remain labelled
   estimates and cannot become live receipt evidence or clean forward cohorts. The common comparison
   contract across strategy families is deferred; this decision does not invent a new numerical score,
   shorten an evidence window, or make unlike account returns directly comparable.
4. **Promotion first changes the approved recommendation source.** A human-reviewed, durable record must
   identify the winning complete strategy, supporting evidence, reviewer, effective boundary and prior
   selection. The selected strategy supplies the live trader's recommendations using its evaluated rules
   and the target account's actual state. Its paper account and history continue to be independent. A
   paper position or simulated fill is never copied into a real account as an actual holding or fill.
5. **Broker execution is a later, separate authority.** Wealthsimple is the intended first broker target,
   subject to a reviewed, supported access mechanism. TraderVI owns strategy selection, decision and risk
   rules. The adapter translates approved account-specific orders and reports broker acknowledgements,
   rejections and actual fills. It does not select or retune the strategy. Broker-reported cash, positions,
   fees and partial fills must be reconciled; paper fill assumptions cannot stand in for broker facts.
   Recommendations do not themselves grant permission to send orders.
6. **Preserve the accepted V1 behaviors.** Daily System Shadow (ADR-0051), Delphi Live's frozen V1 source
   (ADR-0053), manual Real tracking (ADR-0039), and exploratory replay (ADR-0054) remain separate contracts.
   Delphi Live's Operational Champion is a role inside that paper-policy family, not proof that it has
   won a comparison against every strategy or been approved for real execution. Its existing discovery,
   untouched confirmation, promotion and carried-position rules remain unchanged. ADR-0022's broader
   evidence requirements remain in force; a later design must reconcile their application to comparisons
   across families explicitly.

## Alternatives considered

- Pool all strategies into one paper balance: loses attribution and creates competition for cash that
  was not part of each independent experiment.
- Route whichever portfolio is currently most profitable directly to a broker: ignores unequal periods,
  risk, costs, data quality and repeated selection bias.
- Let the broker client decide what to trade: separates real behavior from the strategy whose evidence
  justified adoption.
- Treat Delphi Live's existing policy-promotion button as a universal live-trading switch: exceeds its
  implemented scope and confuses paper-policy approval with real execution permission.

## Consequences

The present separation of accounts is the right foundation. The next architecture work must bind complete
strategy identity to its actual model/policy inputs, define comparable account evidence, and implement
explicit recommendation routing before broker execution. Existing model activation, legacy entry paths
and host dependencies require the [alignment review](../reviews/strategy-direction-alignment-20260906.md)
to guide that work. This ADR documents direction only; it changes no engine, threshold, database state,
model artifact or execution permission.

The future execution design must cover authentication, approved accounts, limits, order preview/approval
mode, idempotency, partial fills, reconciliation, cancellation, stale evidence, operational recovery and a
kill switch. These are deferred design requirements, not claims that the current client supports them.

## Review questions

1. Why is the object of promotion a complete strategy rather than its current paper balance or ML model?
2. What changes when a strategy becomes the approved recommendation source, and what still requires
   separate authorization?
3. Why must an actual broker account retain its own state when following a successful paper strategy?
