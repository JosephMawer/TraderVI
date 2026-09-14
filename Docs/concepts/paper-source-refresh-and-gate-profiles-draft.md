---
cssclasses:
  - executive-brief
---
# Automatic daily-source updates and paper gate comparisons

**Date:** 2026-09-09 · **Status:** Guided design accepted in ADR-0067; five pairs implemented and activated. Source adoption is deployed under ADR-0066. The historical design discussion below is retained; ADR-0067 and project-status.md record verified deployment evidence.

> [!summary] Accepted behavior
> Operational paper portfolios should adopt a newer valid Delphi publication during the session instead
> of losing the entire day when the 09:30 publication cutoff is missed. Separate paper portfolio
> variants should make the daily entry gates explicit, allowing the operator to test portfolio rules
> while retaining the normal strategy for comparison.
>
> This document retains the guided decision record and acceptance contract. The operator subsequently
> authorized implementation; migration 033 and all ten accounts are active. Current state and test
> evidence are recorded in ADR-0067 and project-status.md.

## Problem, parent problem and goal

The original immediate issue was that a missed daily publication left both Shadow and Delphi Live
bound to `NoValidDelphiRun`. ADR-0066 resolved operational source adoption. The remaining issue is that
daily market gates can block every new candidate after recovery.
The parent problem is unreliable operational recovery and inability to observe the portfolio lifecycle.
The root goal is to measure how the portfolio's entry, sizing, exit and risk rules perform prospectively.

## Decisions

| State | Decision |
|---|---|
| Accepted user direction | Automatically use a newer valid Delphi run; 09:30 no longer permanently rejects an operational paper source. |
| Accepted user direction | Apply that behavior to the corrected September 9 run in the current task. |
| Accepted | Adopt on the next serialized processing cycle; preserve holdings, cash, exits and existing risk holds. |
| Accepted direction | Separate normal and experimental paper accounts with explicit, versioned gate settings; next milestone. |
| Accepted direction | Portfolio-rules-only comparisons bypass daily entry gates; account/capital activation remains the next milestone. |
| Accepted September 9 guided dialogue | For each selected strategy, create a fresh normal/portfolio-rules-only pair with identical starting virtual cash, activation time and Delphi source. Existing portfolios continue separately. |
| Accepted September 9 guided dialogue | Include Continuation Top 3, Continuation Top 5, Breakout Top 3, Breakout Top 5 and Delphi Live in the first comparison milestone: five matched pairs, ten new paper accounts. |
| Accepted September 9 guided dialogue | Each of the ten new accounts starts with CAD 1,000 virtual cash, no holdings and no pending orders. Each receives the full amount independently; capital is not divided among accounts. |
| Accepted September 9 guided dialogue | Keep published selection scope: each Shadow account uses only its lens's ranks 1..3 or 1..5; Delphi Live uses the deduplicated union of both published Top 25 lists. Do not reach below a Shadow rank limit to fill a vacancy. |
| Accepted September 9 guided dialogue | Gate profiles stay fixed for each account's lifetime. A different gate experiment requires a fresh matched pair; existing accounts cannot be switched between profiles. |
| Provisional implementation default | Prepare all ten accounts together and enable them at a common prospective processing boundary after readiness checks; preserve actual start time and partial-session coverage. |
| Deferred | Breadth-only experiments, per-gate toggles and changes of gate profile within an existing account. |
| Deferred | Any broker execution or change to real-account recommendation approval. |

The latest source currently requested is `9ab04fbc-e71d-4021-ba1f-20bfd5f23984`, published September 9
at 09:59 Toronto, with September 8 inputs. At activation, recheck the current valid source rather than
silently using a subsequently superseded run.

## What the saved-run check established

An offline helper reconstructed the actual gate contexts from all 207 saved candidates and the run's
saved configuration, market regime and Granville forecast. It called the existing Core lens pipelines.
Both reconstructed normal profiles exactly matched the saved official eligibility.

| Profile | Continuation eligible | Breakout eligible | First blocking gate |
|---|---:|---:|---|
| Normal | 0 / 207 | 0 / 207 | Breadth |
| Breadth bypassed | 0 / 207 | 0 / 207 | Granville |

Each published Top 25 also has zero eligible candidates under breadth-only bypass. This changes the
initial recommendation: breadth-only is a useful controlled experiment, but does not meet today's
request to exercise the portfolio rules. A portfolio-rules-only profile bypasses the daily gate layer;
it does not promise a buy because the portfolio still requires its own qualifying entry evidence.
Private inputs and aggregate helper results are in ignored `artifacts/source-adoption-design-20260909/`.

## Automatic source adoption contract

1. Poll for the newest completed, valid OfficialPaper run for today's recommendation date and the
   portfolio's assigned daily strategy. Require the immediately preceding completed canonical session
   and valid captured model/input identity. Invalid or incompatible runs never replace a valid source.
2. Record each adoption with previous/new run IDs, actual adoption time, affected portfolio, admission
   profile version and an effective processing boundary. Retain the original blocked/frozen state.
3. The initial late source and later same-day replacement use the same transition. Seeing the same run
   again is a no-op. A restart resumes the adopted revision and never applies it twice.
4. No action is backdated. Newly admitted candidates require observations after their effective boundary
   and the portfolio's ordinary confirmation. A late source cannot replay missed buys or invent a
   full-session VWAP. Where the existing entry rule lacks sufficient history, show the unmet condition.
5. Preserve cash, holdings, completed fills and pending protective sells. Source replacement is not a
   liquidation signal. Supersede unfilled buys tied to the prior source; requalify under the new source.
   A holding retains its original entry provenance while protection continues under its assigned policy.
6. Keep collecting held and pending-exit symbols even when they disappear from the new daily list.
   Add newly admitted symbols and their correctly dated daily baselines before evaluating them.
7. Retain regular market hours and the existing near-close buying cutoff; removing the 09:30 source
   deadline does not authorize after-hours purchases. Late coverage is visible and excluded from any
   existing evidence criterion that requires a complete session.
8. The UI shows active publication time, adoption time, previous source revisions and the reason for
   any rejected source. Shared Settings does not silently assign a different daily strategy.

## Gate profiles and comparison accounts

Selected profiles for the first comparison milestone:

- **Normal:** existing daily gate stack.
- **Portfolio rules only:** use saved Delphi rankings without enforcing Regime, Breadth, Granville,
  DownProbability, lens setup/trend confirmation, Direction or Composite admission gates.

Breadth-only bypass remains a possible later experiment. It is outside the accepted five-pair first
milestone; the saved-run check above explains why it would not exercise portfolio entries today.

Profiles do not remove stock-universe restrictions, required-input validation, market-data conflict
checks, causal prices/fills, portfolio momentum/confirmation, cash or position limits, loss holds,
stop-loss/trailing protection, or the close-of-session buying cutoff. They also do not remove the
model/Granville contributions already incorporated into the saved ranking scores. Disabling a gate
and changing the ranking calculation are different experiments.

Record normal gate results and the experiment's admission separately. A bypass is not a normal pass;
the original official Hold and Athena facts remain unchanged. Since the current pipeline stops at
the first failure, individual remaining gates must be evaluated before claiming a breadth-only pass.

Accepted source pool: Shadow Top 3 uses published ranks 1..3 of its assigned lens and Top 5 uses ranks
1..5. A failed gate or unmet portfolio entry condition leaves a vacancy; it does not pull in rank 4 or
rank 6, respectively. Delphi Live uses the deduplicated union of Continuation and Breakout's published
Top 25 lists, with saved lens attribution retained. Normal admission still requires the existing daily
eligibility result; portfolio-rules-only admission bypasses that daily layer. Neither profile reranks
the saved lists, expands to unpublished evaluated stocks or retrains models.

Accounts hold a versioned admission profile and keep independent cash, shares, orders, fills and marks.
The user accepted fixed profiles for each account's lifetime. The profile identity and definition are
immutable; a different gate experiment requires a fresh matched pair. The first milestone provides no
profile toggle or reassignment path for these accounts. Historical rules, original daily gate results
and entry provenance cannot be rewritten. Existing scoped configuration controls remain applicable to
other strategy changes; a gate-profile lock must not be defeated through Shared Settings.

The user accepted fresh matched pairs in the September 9 guided dialogue. For each selected strategy,
start new normal/portfolio-rules-only accounts at the same time, with identical virtual cash and Delphi
source. Existing portfolios continue separately; their different holdings and intervention history make
them operational references rather than matched controls. The first comparison milestone includes the
four Shadow strategies (Continuation Top 3/Top 5 and Breakout Top 3/Top 5) and Delphi Live: five pairs,
ten new paper accounts. The user specified CAD 1,000 starting virtual cash for each account. All start
cash-only with no holdings or pending orders; none inherits trades or capital from an existing account.
The ten balances are independent alternatives, not one shared CAD 10,000 investment portfolio.
The earlier request is to begin as soon as the implementation is ready for the current run. The
implementation default is to prepare all ten accounts together, then enable them at one common
prospective processing boundary after both engines are ready. A partial first session retains its actual
start time and coverage limits, with no backdated trades. If readiness is reached after the normal buy
cutoff, the cutoff still applies. No account creation has occurred in this design pass.

## Existing implementation constraints

Before ADR-0066, existing Shadow sessions retained their initial source and Delphi Live froze source
identity through repositories, SQL foreign keys, baselines, dossiers and in-memory session state.
The September 9 source-adoption milestone is now deployed; it supplies audited operational source
revisions while preserving original evidence. The comparison work must extend that contract to each
account's admission profile.

Delphi Live's existing ledger and monitor already isolate account cash, positions and action state and
iterate multiple accounts. Including its pair is a manageable extension, but requires more than adding
two rows: activation is currently coupled to research policy roles, and daily candidate admission is
shared through the operational session context. Provide explicit comparison-account identity and
account-specific admission without reusing research promotion roles or mixing their evidence. Reuse
the existing entry/exit engines and shared collection wherever possible. This assessment supports the
user's conditional request to include Delphi Live in the first milestone; it is not a delivery-time
estimate or completed implementation.

The tracked holding's `CompletedBarConflict` is a separate data-reconciliation issue. A gate bypass
cannot make conflicting bars valid. Preserve first-received records and audit any accepted correction;
do not clear the conflict by overwriting history or treating it as a passed admission gate.

## Acceptance and implementation sequence

The behavior is coherent enough for implementation; no product-choice question remains for this first
milestone. [ADR-0067](../adr/0067-fixed-gate-paper-comparison-pairs.md) records the accepted contract.
The SQL schema, activation transaction, engine admission and WPF integration are implemented. Migration
033 and the initial ten accounts are active; the verification addendum in ADR-0067 records the exact
source/start identities, protected-state checks, builds, tests and operational coverage.

Acceptance criteria:

1. Exactly five fresh pairs exist, with two independent accounts per pair. Every account starts with
   CAD 1,000, zero holdings and zero pending orders; existing accounts retain their state and history.
2. Each pair has the same starting source, start boundary and portfolio rules. One fixed profile is
   Normal; the other is Portfolio rules only. Profile mutation is rejected in code and storage, not
   merely hidden in WPF. Repeated activation cannot duplicate or recapitalize accounts.
3. Normal admission matches official saved daily eligibility. Experimental admission includes the
   accepted published ranks despite failed daily gates, records that bypass explicitly, and preserves
   the original Hold/gate trace. A bypass never creates a purchase without portfolio qualification.
4. Top 3/Top 5 never expand past their rank limits. Live deduplicates its published Top 25 lens union.
   Invalid or incompatible sources, bad data, cash/position limits and portfolio risk controls still
   block new risk. Existing sizing, execution assumptions and exits are identical within each pair.
5. New valid publications are adopted prospectively for both members of a pair. An incomplete source
   transition cannot let one member trade from a different publication unnoticed. Holdings and
   protective sells survive source changes; unfilled superseded buys requalify on fresh evidence.
6. Shared collection does not share cash, candidate confirmation, orders, holdings or gate admission.
   Live comparison accounts use explicit comparison identity without taking a research promotion role.
7. WPF identifies each pair and fixed profile, shows source/start/coverage information, and distinguishes
   a daily gate block, a bypass and an unmet portfolio entry condition. Existing comparison metrics use
   aligned observed periods; missing valuations remain unavailable and late coverage remains visible.
   Independent account balances are never presented as one combined portfolio.

Implementation order:

1. Completed: guided design and ADR-0067; source adoption remains the already-deployed ADR-0066 base.
2. Add immutable profile/pair/account identity and additive storage; synchronize canonical SQL and the
   reviewed manual migration. Extend activation to prepare the five pairs with common provenance.
3. Integrate profile-specific candidate admission and existing execution engines for Shadow and Live,
   including repeated source updates, restart recovery, account isolation and explanatory WPF display.
4. Run focused behavior tests, affected .NET builds and the SSDT build; review the exact migration and
   activation records. Before applying an authorized migration, verify a fresh checksum backup.
5. Apply the authorized operational change, update WPF with an orderly host handover, and verify exact
   account counts/capital/profile identity, common source/start, usable collection and preserved existing
   history. Report partial coverage and any remaining entry conditions without forcing a trade.

Regression cases include before/after-09:30 publication, repeated and replaced runs, incompatible
strategy/as-of dates, invalid model/input identity, missing opening data, removed held symbols,
pending buys/sells, source changes during collection, close cutoff, restart and account isolation.

Related: [ADR-0051](../adr/0051-system-shadow-portfolios.md),
[settings contract](global-settings-design-draft.md),
[September 8 Shadow exception](../adr/0064-one-session-shadow-engineering-override.md),
[September 8 Live exception](../adr/0065-one-session-live-engineering-activation.md).
