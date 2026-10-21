---
cssclasses: [executive-brief]
---
# ADR-0097: Reviewed October 21 Portfolio rules only source switch

**Date:** October 21, 2026. **Status:** Accepted by the operator's explicit implementation request.
**Domains:** architecture, risk-management, decision-engine, user-interface.
**Related:** ADR-0062, ADR-0067, ADR-0079–0083, ADR-0085–0086, ADR-0092, ADR-0095–0096.

The immediate problem is the Normal-only real-account setup after the operator requested the
existing Delphi Live Portfolio rules only source. The parent problem is deliberate selection of
an exact complete strategy for the permanent TFSA. The root goal is independent strategy-driven
trading with preserved financial history and risk controls. Normal was an earlier preparation
recommendation, not an autonomous runtime winner or proof of an informed preference for gates.

## Decision

Add a narrowly dated, explicit source-switch review to the existing October 21 continuation.
Definition review can describe either identified Delphi Live comparison profile; it does not
qualify general ungated execution. Only the existing matched pair/batch's Portfolio rules only
source may replace Normal through this workflow, with identical daily configuration/models,
intraday policy, sizing and risk settings. Policy identity/name and running implementation identity
may differ. No paper portfolio, policy settings, stored gate facts or model is rewritten.

This supersedes only the Normal-source restriction for this explicitly approved October 21 switch
in ADR-0092/0095/0096 and the dated continuation contract. It grants no generic portfolio activation,
repeat switch, evening or next-session permission. The original scope expires at **16:00 Toronto
October 21**, with the existing reservation/transmission cutoff checks unchanged.

## Review and durable boundary

Require the host stopped, current original cash-only/no-outside-change scope, zero owned automatic
orders/positions/protection, no invalidation or financial/order holds, immutable storage, fresh
exact source/build and fresh reconciled account. Preserve original approval/continuation, account
identity, baseline/references/provenance, financial memory, spending ceiling, holds and operator
paused/new-buy-stop modes. No new baseline, funding, sale, qualification replay or risk write is added.

Display the exact source/profile/policy/daily/build, current cash/remaining ceiling, original risk
references, retained controls and exact cutoff. State explicitly that daily Regime, Breadth,
Granville, DownProbability, setup/trend, Direction and Composite admission gates are bypassed by
this existing profile. Published-universe/rank rules, intraday confirmation, fresh valid data,
whole-share sizing, cash/account/stock limits, stops, order reconciliation and full-fill-only
native restrictions remain. Paper results retain their disclosed limitations.

The operator approves within the original two-minute review window. Approval repeats current
source/storage/account/build/time checks and saves an append-only `PortfolioRulesOnlyApproved`
revision, with the prior revision/hash and full exact source review. The repository validates
preservation of original authority/history. Lost acknowledgement recovers the identical request,
never a second approval or order. Approval leaves execution off; **Enable reviewed host** remains
a separate operator action bound to the new source and current implementation. Old grants fail.

Clear only derived intraday confirmation state and set the actual observation boundary to source
approval time. The real engine reads/re-evaluates durable market inputs after that boundary;
earlier Normal or paper confirmations, positions, funds and results cannot transfer. Financial
memory remains intact and earlier signal snapshots remain in immutable prior revisions. No
immediate order is promised: valid post-approval confirmation and all retained controls must pass.

## Installation and verification

Validate offline preservation/rejection/recovery/host/cutoff and engine confirmation/data behavior.
Normal app closure and independent zero-owner inspection precede replacing standard Release output.
Operator normal startup/sign-in, exact review/approval and separate enable follow. Source/build
tests establish no native activation, order, fill or populated protection. No SQL schema migration,
native write rehearsal or external-service probe is required. The independently deferred SQL
reliability draft stays out of this build and remains an unresolved operational limitation.

## Review questions

1. What changes between Normal and Portfolio rules only, and which financial/data controls remain?
2. Why must actual confirmations start after source approval even when the paper source already bought?
3. Why does source approval neither enable the host nor extend today's buying cutoff?
