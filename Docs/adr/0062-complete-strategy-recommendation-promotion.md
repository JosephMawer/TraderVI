# ADR-0062: Complete-strategy recommendation promotion

- **Status:** Accepted; implemented in source, not operationally activated.
- **Date:** 2026-09-07
- **Related:** ADR-0022, ADR-0051, ADR-0053, ADR-0055, ADR-0060, ADR-0061

## Problem and decision

The immediate problem was the unavailable promotion action. The parent problem is routing independent
strategy families to Trading without mixing accounts or evidence. The root goal is human-approved
recommendations based on actual reported financial facts, before separately authorized broker execution.

The operator explicitly rejected making evidence thresholds an approval gate for this manual mode:
"if I want to promote a portfolio I will." **Manual approval may accept incomplete or non-comparable
performance evidence.** The review discloses that evidence; it does not certify a winner. There is no new
score, statistical threshold, automatic promotion, or requirement to satisfy ADR-0022's tiers before this
manual recommendation assignment. This is a scoped amendment to ADR-0022/0055, not an assertion that
the strategy has met their statistical standards. Enforcement belongs in the future Wealthsimple live
trading/automation design. Delphi Live's internal research promotion, untouched comparison and
intervention rules remain unchanged and separate. Athena's official evidence contracts remain unchanged.

The existing ADR-0060 pause-and-empty rule still applies because this changes Trading rules. The tracked
family must be paused and empty; the target Real account must have a reported CAD state with no holdings
or outstanding orders. Approval cannot clear orders, close holdings, resume entries, or release risk holds.
These are operational consistency checks, not tests of investment performance.

## Domain boundaries and programming style

The repository combines object-oriented services/repositories, procedural orchestration and pure policy
calculations. Promotion extracts useful boundaries without introducing a generic trading framework.

| Concept | Responsibility |
|---|---|
| Complete strategy/version | Immutable snapshot of selection, daily inputs, model IDs and artifact checksums, typed portfolio policy, implementation checksum and recommendation contract. |
| Shared daily inputs | Frozen candidate run, models and gates; a daily model assignment alone is not approval of a complete portfolio strategy. |
| Family policy | Shadow keeps its lens and Top 3/5 selection, staged whole-share sizing, rotation and five/fifteen-minute protection. Live keeps confirmation, ranking, bid protection and its own sizing. |
| Account and report | Real holdings/fills remain in the existing tracked ledger; immutable reports add actual cash, NAV, session opening/high values and outstanding-order state. |
| Paper experiment/evidence | Independent account and research history; immutable review captures supporting facts and explicitly describes attribution/comparison gaps. |
| Promotion and assignment | Human-reviewed, append-only assignment for one Real account, with prior approval and effective UTC boundary. A Live champion role has no authority here. |
| Recommendation | Advice, with evidence and account provenance. It does not reserve broker cash, submit an order, or create a fill. |
| Reported fill | Explicit human report of a completed actual purchase/sale. It alone changes Real holdings; simulated fills are never a source. |

`Core/Trader/Promotion` contains cohesive immutable domain records, validation and deterministic family
evaluation. `TradingRecommendationWorkflow` coordinates the store and market-observation interfaces,
which are consequential boundaries. SQL lives in four focused `Core/Db/Trading*` files; WPF provides the
review, state entry and result presentation. There are no interfaces for every domain type.
`SystemShadowProtection` extracts the prior controller's exact protection composition and is shared by
paper and Real recommendation evaluation. Existing paper selection, fills and financial state are untouched.

## Operator workflow

1. In Trading, load the Real account, reconcile tracked holdings and report current CAD cash, account
   value, session opening value, historical closing high and outstanding orders. Reports are immutable.
2. Pause tracked entries and satisfy the existing empty-family requirement. In Portfolios, select a paper
   strategy, specify the Real account and prepare a complete review. Inspect exact daily/model/policy/code
   identities, supporting paper facts, limitations and any operational blocks.
3. Enter a reason and explicitly approve. The approval becomes effective when the serializable transaction
   commits, at its recorded UTC boundary. The dialog identifies the saved approval. Resume is separate.
4. Trading shows the source and its latest recommendation batch. Evaluate explicitly or keep the dashboard
   running for scheduled evaluations. Missing or incompatible inputs explain why new risk waits.
5. After trading manually, separately report the actual fill, then reconcile cash/state again. Purchase
   quantity and price are never prefilled from the recommendation. Initial/reentry/add-on attribution keeps
   the original Real position target. Existing all-shares, zero-commission exit restrictions remain.
6. To switch back, select an earlier approval in Trading history and review the exact saved version again.
   This appends a new decision; it never rewinds financial facts or paper state.

The Portfolios header identifies TFSA's approved Trading source explicitly; the Trading account selector
supports other named Real accounts. Comparison returns remain descriptive and may span earlier policies.
Review does not infer clean coverage, an untouched result, or version-specific superiority from those
returns. Raw supporting facts are available in the review, with readable limitations above them.

## Boundaries, failure and recovery

- Promotion uses the daily-selection lock, engine-settings fence and per-account recommendation lock in
  that order. It rechecks source identity, account report, paused/empty state and prior approval inside the
  transaction. Failed activation rolls back and retains the previous assignment.
- The same request ID and content returns the same saved approval. Reusing that ID with changed content
  fails. A concurrent/stale review needs a new review. A strategy already assigned is explained as such.
- Evaluation holds its account/assignment fence through publication. A switch cannot interleave midway;
  batches identify their approval, account report, prior batch and immutable input checksum. A failed quote
  workflow or publication leaves the prior batch intact. Retry evaluation without repeating promotion.
- Reports must match the current session and Real ledger checksum before new risk can be considered.
  Unknown cash, risk baselines, outstanding orders or mismatched daily evidence blocks new risk. Stale cash
  cannot invent an account loss after a fill. Protection can still use the actual holding and available
  valid evidence. Repeated advice does not spend cash or create a position.
- New entries require a valid pre-open official run matching the approved daily configuration and exact
  model artifacts. The reviewed TSX calendar supplies sessions; missing coverage does not become a weekday
  guess. Live re-evaluates durable stock/XIU inputs with its own Real-account state and fresh observed
  quotes; it never copies a paper lifecycle, position or fill. Quote receipts retain their own causal times.
  Only durable Live input context enters the route; paper evaluation results are not loaded. Holdings are
  quoted first, then eligible candidates, within the saved policy's quote-attempt window. The recommendation
  loop does not emulate the paper execution retry/fill lifecycle; a later evaluation can retry observations.
- Approval pins the Core assembly checksum. A changed implementation blocks new entries until reviewed;
  existing holdings retain protective checks under the current implementation with a visible warning.
  An old binary is not dynamically loaded. Reapproval of an unavailable old implementation is blocked;
  prepare a current strategy review instead. A changed daily assignment similarly requires compatible
  daily inputs; promotion does not silently change the shared daily assignment.
- Loss/capital holds survive a switch and ordinary resume. A separate explicit capital review appends a
  reported-state record and rebases its closing high, retaining the daily loss hold and Trading pause.
- Actual purchases are transactional and idempotent by report request. Actual sales reuse the existing
  idempotent Real-exit workflow. The existing one-active-tracked-position-per-symbol behavior and unsupported
  partial exits/fee-bearing fills remain explicit. No broker balance is inferred from these reports.

## Persistence and separate activation

Migration `TraderDB/Migrations/20260907_029_AddTradingPromotion.sql` creates five additive tables:
`TradingStrategyVersion`, `TradingAccountReport`, `TradingPromotion`, `TradingRecommendationBatch` and
`TradingReportedFill`. Canonical SQL definitions and immutable update/delete triggers are included in
the SQL project. Composite foreign keys retain account ownership and prior history; the promotion chain
cannot fork. The migration contains no seed, approval, account assignment or financial data copy.

The migration was **not applied**. Before a separately authorized activation: stop older hosts, take and
verify the required backup, review/apply 029 with SQLCMD error-stop behavior, use the reviewed build and
calendar, then report/reconcile the account and review an actual promotion. Keep the existing collectors
available: this feature consumes their durable evidence and does not start them. Live quote retrieval is
market observation only, never broker execution.

## Validation and limits

All 774 Core tests pass, including 54 added tests across identity, reporting, eligibility, routing, isolation,
serialization, effective cutover, concurrent workflow fencing and failure/retry behavior. The affected WPF
build and SQL project/SSDT build pass. The standalone markup preview uses synthetic values at 1280×900
and 980×680 with zero binding errors; no operational code or event handlers are loaded. Existing compiler
warnings concern nullable annotations, unused variables and unreachable code. Separate existing package
advisories: System.Data.SqlClient 4.8.1 (moderate/high), System.DirectoryServices.Protocols 5.0.0 (moderate),
System.Drawing.Common 5.0.0 (critical), System.Security.Cryptography.Xml 5.0.0 (moderate).
The SQL transaction contract is statically reviewed and built; no database integration test or migration
rehearsal was executed. No operational application, market request, model training, actual approval,
assignment, migration, broker connection, commit or push was run. `Docs/.obsidian/workspace.json` was
modified externally during the task and preserved.

Primary changed files are the six domain/workflow files under `Core/Trader/Promotion`, the four
`Core/Db/Trading*` repositories/storage helper, `SystemShadowProtection` and its controller call site;
five Core test files; the Trading recommendations view and two reporting/review dialogs; Portfolios
review/header integration and dashboard scheduling; five SQL tables, five immutable triggers, migration
029 and the SQL project; and this ADR plus linked status/architecture/design/roadmap/concept/review records.
Validation logs and isolated preview output are in ignored `artifacts/promotion-*` paths. The complete
solution was not built; the affected WPF dependency graph and SQL project were built separately.

Before automated Wealthsimple trading, decide enforceable evidence eligibility, cross-family comparability,
account/broker reconciliation, credentials/access, order authorization, stale data behavior, partial fills,
fees, cancellation, idempotency, stopping and recovery. The manual recommendation exception does not
authorize automated trading or predetermine those decisions.
