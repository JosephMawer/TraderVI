# ADR-0108: Strategy workspace and independent account evidence

**Date:** 2026-10-23. **Status:** Accepted; WPF implementation and offline verification complete;
installation and running-desktop review pending. **Domains:** user-interface.

## Decision and purpose

Open Portfolios on **Strategy workspace**, with **Account directory** second. Keep the application's
left navigation. The top navigation in the concept was a mockup choice, not a necessary product change.
Performance, Strategy review and Account activity remain available and share the selected account.

The immediate problem is that account lists obscure the relationship between a strategy and the
accounts following it. The parent problem is interpreting Paper/Real differences without confusing
simulation, financial approval and broker execution. The root goal is to understand the evidence and
choose rules deliberately while each account retains its own state and authority.

This supersedes only the default Portfolios landing and view names in
[ADR-0061](0061-portfolio-comparison-workspace.md) and the initial all-account landing in
[ADR-0068](0068-main-account-trading-overview.md). Trading remains the main Real account workspace.
Shared comparison periods, reference-account scope, existing review eligibility and consequential
action contracts remain in force.

## Presentation contract

- Present the strategy as a rulebook, above separate Paper and Real account cards. Selecting a
  Paper source for review changes neither account nor their histories. Paper keeps running under
  its own controls after selection for Real; a paused account is not implicitly resumed.
- Explain that Delphi Live is the intraday strategy family. Paper can observe actual market data
  while its fills, cash and holdings are simulated. Use **Paper trade history** for those records.
- The Real card reuses Trading's linked broker snapshot and account-matched, verified management
  record. Keep operator-reported Real references explicitly labelled in the directory. Missing
  broker cash, holdings or management facts remain unavailable; no Paper or manual reference fallback.
- Show the relationship to the selected Paper source, including source/policy/profile differences.
  Source identity alone cannot prove complete current rule/input-version equality. Keep exact
  available identities in the expandable rules panel; never attribute an account's entire history
  to its current rules.
- Compare one stock using each account's own saved lifecycle, execution checks, recorded outcome,
  next-action context and observation/evaluation time. This displays existing evidence; it does not
  run either decision engine. Missing confirmation is not reconstructed from a fill, and an account
  update timestamp is not a candidate observation.
- A Paper fill is an assumed fill. A broker intent or acknowledgement is not a confirmed fill.
  Show broker-reported filled quantity, independently observed holdings and unresolved orders in
  their own roles. Attribute a saved price rejection only to its selected stock. Use retained prices
  when available; otherwise say the historical price pair is unavailable. The fictional ABC
  $10.00/$10.02 teaching example must never fill gaps in Friday's evidence.
- Financial approval and execution-host state remain separately labelled and timed. Host status
  alone does not establish account permission. Paper results do not establish profitability or
  qualify actual broker orders, fills and protection; approval is not statistical proof.

## Actions and implementation boundary

The workspace's **Review for Real** opens the existing exact-source TFSA transition review for
supported intraday sources. It neither broadens eligibility nor chooses a substitute if the source
is unavailable. The existing recommendation-review route remains separately labelled in Strategy
review. Approval and execution enable retain their existing operator steps.

Navigation and comparison do not add broker polling, evaluation, assignment or order calls. Saved
Paper data uses the existing display refresh; Real uses Trading's shared display context. Open Trading
is the route for loading broker facts and inspecting broker history. No strategy, gate, database schema
or financial permission changes are part of this UI increment.

## Alternatives and consequences

A new top-level destination would duplicate Portfolios and split account selection. Retaining the
directory as the default would leave the account/rulebook relationship secondary. Keep the approved
workspace inside Portfolios, preserve the directory for scanning and historical tools, and retain
the established left navigation.

The two columns may legitimately differ in rules, evidence time, lifecycle, holdings and action.
They are not automatically a matched experiment. Freshness and identity limits remain visible.
Implementation acceptance and remaining desktop review are tracked in the
[owning checklist](../strategy-alignment-implementation-checklist.md#portfolios-strategy-workspace--october-23).

## Review questions

1. What transfers when a Paper strategy is selected for Real, and what remains independent?
2. Why can Paper hold a position while Real remains in cash after the same apparent entry signal?
3. Why are an order acknowledgement, a broker fill and a current holding separate facts?
4. Why cannot a source match, approval or Paper return certify identical rules or profitable execution?
