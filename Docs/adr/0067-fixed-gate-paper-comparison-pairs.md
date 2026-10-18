---
cssclasses:
  - executive-brief
---
# ADR-0067 — Fixed-gate paper comparison pairs

**Date:** 2026-09-09 · **Status:** Accepted; implemented and activated September 9.

**Domains:** architecture, decision-engine, risk-management, user-interface

**Narrow prospective extension (October 17, 2026):** [ADR-0090](0090-progressive-individual-profit-trailing.md)
permits an explicit progressive-profit-protection successor assignment only. Original profiles, gates,
source snapshots and history stay immutable. Treat adoption as a dated risk-rule intervention, not
an uninterrupted original experiment. The narrow successors are installed/assigned October 18;
original profiles/history remain unchanged and off-session preservation checks pass.

> [!summary] Decision
> Create five fresh matched pairs of paper accounts, each account starting with CAD 1,000. One member
> applies the normal daily gates; the other uses portfolio rules only. Gate profiles remain fixed for
> each account's lifetime. Existing accounts continue separately. This enables a prospective comparison
> of the gates' effect while preserving portfolio entry, sizing, execution and protection rules.
>
> Gate bypass does not guarantee a purchase. Missing data, portfolio qualification, cash limits and
> protective holds can still prevent entry. These are paper comparisons, with no broker execution.

## Problem and goal

The immediate problem is that daily gate failures repeatedly prevent the portfolio rules from being
exercised. The parent problem is insufficient comparable operational evidence after late nightly
recovery. The root goal is to observe how portfolio entry, sizing, exit and risk rules perform over
time, and whether daily admission gates improve the result. Source adoption is already implemented
under [ADR-0066](0066-operational-paper-daily-source-adoption.md).

## Accepted account plan

| Strategy | Normal accounts | Portfolio rules only accounts | Starting virtual cash per account |
|---|---:|---:|---:|
| Continuation Top 3 | 1 | 1 | CAD 1,000 |
| Continuation Top 5 | 1 | 1 | CAD 1,000 |
| Breakout Top 3 | 1 | 1 | CAD 1,000 |
| Breakout Top 5 | 1 | 1 | CAD 1,000 |
| Delphi Live | 1 | 1 | CAD 1,000 |

Each pair starts together from the same Delphi publication and otherwise identical portfolio rules.
All ten accounts start cash-only, with no copied holdings, orders or fills. Their cash is independent;
it is not split from a common allocation or summed as shared wealth. Existing account histories remain
available as operational references, but their earlier holdings and interventions make them unsuitable
as fresh matched controls.

## Fixed profiles and selection scope

Normal applies the existing saved daily eligibility result. Portfolio rules only bypasses the daily
Regime, Breadth, Granville, DownProbability, lens setup/trend confirmation, Direction and Composite
admission gates. It preserves saved ranking scores and order, including model/Granville contributions.
The experiment changes admission, not scoring or model training.

Shadow Top 3 considers only its lens's published ranks 1..3; Top 5 considers only ranks 1..5. A blocked
or unqualified stock leaves a vacancy; neither account searches farther down the list. Delphi Live
uses the deduplicated union of the two published Top 25 lists and retains their lens attribution.
Normal still requires ordinary daily eligibility; the experimental member may admit the same ranked
stock despite its original gate failure. Unpublished evaluated stocks are outside this experiment.

Each account's profile identity and definition are immutable. WPF and repository writes cannot switch
an existing account to another gate profile. A different gate experiment receives a fresh matched
pair; it cannot relabel existing results. Shared Settings must not provide an indirect profile switch.
Other strategy changes remain subject to the existing configuration and evidence-identity contracts.

Original official Hold decisions, first-failure information, gate traces and Athena facts remain intact.
Record experimental admission separately as a bypass, never as a normal pass. The first-failure gate
pipeline does not establish that unevaluated gates passed.

## Invariants and operational behavior

Retain universe and required-input validation, canonical data freshness, conflict checks, ordinary
intraday entry/confirmation, cash and position limits, whole-share sizing, loss/capital-review holds,
protective exits, causal fills and the near-close buy cutoff. Daily source replacement continues under
ADR-0066: fresh evidence, no backdated entries, protected holdings and superseded pending-buy handling.
Both members use the same publication transition; any unaligned transition must be visible and cannot
silently become a gate-performance comparison.

Reuse the existing Shadow and Live engines. Shared market collection must not share account cash,
positions, pending actions, confirmation state or admission decisions. Live comparison accounts need
explicit account/profile identity and must not be disguised as research champion/challenger roles.
Their evidence is distinct from official Athena outcomes and existing Live promotion cohorts.

The provisional activation default is to prepare all ten accounts before enabling the common
prospective processing boundary. The requested operational target is the current run as soon as the
implementation is ready. A partial first session retains its real start time and coverage limitations;
there is no automatic reset of its history and no relaxation of entry conditions to obtain a fill.
An implementation completed after the ordinary buy cutoff cannot create a same-day buy exception.

WPF shows pair identity, the fixed profile, source publication/adoption, start and coverage. An
unfilled account explains whether a daily gate, remaining portfolio condition, data problem or capital
limit prevents entry. Pair comparisons use aligned observed periods and disclose unavailable values
or gaps; no return display by itself proves statistical superiority or grants recommendation authority.

## Alternatives and deferred work

- **Use existing accounts as controls:** rejected because their starting holdings and intervention
  histories differ. Fresh pairs make the comparison interpretable.
- **Breadth-only first experiment:** deferred. Replaying the saved September 9 gate contexts found
  zero eligible candidates in both lenses even after breadth bypass, because Granville blocked next.
- **Shadow-only first milestone:** superseded by the user's decision to include Delphi Live as well.
- **Editable profiles within an account:** rejected for this milestone. Fresh pairs preserve a clear
  rule identity without mixing different gate experiments into one account's lifetime results.
- **Arbitrary gate editors, broader universes, reranking, new capital flows, automatic promotion and
  broker execution:** outside the first milestone.

## Implementation and acceptance

Add immutable pair/profile/account metadata with individually reviewed additive SQL migration(s),
keeping canonical definitions synchronized. Activation must be idempotent and cannot duplicate or
recapitalize an existing account. Integrate admission in both engines and the WPF comparison display.
Preserve existing account state and original official/research evidence. Migration application requires
a fresh verified checksum backup and the project's manual migration process.

Acceptance requires ten correctly funded independent accounts, exactly one of each profile per pair,
fixed selection limits, rejected profile mutation, matched source/start identity, prospective source
updates, preserved protective behavior, restart idempotence and explanatory WPF status. Tests must show
that a bypass can admit a published Hold while ordinary portfolio/data/risk conditions still reject its
purchase, and that the Normal account retains the official gate outcome. Validate affected .NET
projects and the SSDT project; deployment and live collection checks are separate operational evidence.

The detailed acceptance checklist and integration constraints are in the
[design handoff](../concepts/paper-source-refresh-and-gate-profiles-draft.md).

## Implementation and activation evidence

The operator authorized implementation following the guided dialogue. Manual migration
`20260909_033_FixedGatePaperComparisons.sql` was applied after a fresh checksum backup, verified secondary
copy and successful rollback rehearsal. All 17 protected table hashes matched after rehearsal and
migration; original-account/source hashes also matched after activation and its idempotence retry.
Four transactional negative checks rejected batch/account rewriting, Live profile mutation and a
Shared Settings assignment. No test fixture was committed.

Batch `849334ad-1f09-4059-863e-159474d29d0f` contains exactly ten accounts in five complete pairs, each
funded independently with CAD 1,000 and no copied positions/orders. The common funded boundary is
13:15 Toronto; the resumed host adopted run `9ab04fbc-e71d-4021-ba1f-20bfd5f23984` at 13:21:33, with a
13:25 prospective source boundary across all ten accounts. These distinct timestamps remain visible.
Source revision `b6a36932-3d76-4d6a-b474-98adca79be14` has the 41-stock published lens union and 43
daily baselines, including retained observation symbols. The eight Shadow source/tracking transitions
commit atomically with that common revision; Live records it in each independent ledger.

Normal admission remains zero for this publication. Experimental Shadow accounts contain exactly their
three/five published ranks; experimental Live admits 41 candidates. At 13:32 the first full collection
cycle settled all 43 slots, with 39 usable bars and four missing observations. Both Live ledgers recorded
that same completed cycle, and Shadow candidate evaluation progressed. No comparison fill had occurred
at that checkpoint. Ordinary entry, missing-data and risk rules still apply.

The full Core suite passes 819 tests; WPF Release and SSDT builds pass. A read-only WPF view-model check
verified ten distinct displayed accounts, five pairs, source/profile explanations and disabled profile
and promotion actions. Existing compiler and cached dependency-security warnings remain. The native
screen-capture provider could not render this window, so this is not pixel-level visual verification.
Operational evidence and the explicit activation helper are under ignored
`artifacts/gate-comparisons-20260909/`. The helper requires an explicit activation argument; ordinary
WPF startup only reads existing accounts and runs their authorized paper monitoring.

The admission/profile and complete paired portfolio settings are pinned. Comparison accounts are
excluded from the ordinary Settings target list and recommendation review. New publications with the
same daily strategy are adopted prospectively; a changed strategy does not silently reconfigure a pair.
Source replacement retains held protection and cancels superseded pending buys. Comparison judgments
retain original failed lens facts plus separate account/source admission evidence, including carried
entry dossiers. Shared comparison collection permanently disqualifies that session from the original
research promotion cohorts; it does not overwrite official Athena outcomes.

A final operational restart exposed intermittent SQL error 51246: Dapper's default `datetime` rounding
could move a just-created evaluation timestamp into the future. `DelphiLiveEvaluationRepository` now
binds evaluation and checkpoint timestamps as `datetime2`, preserving precision without weakening the
causality guard. All 352 Delphi Live tests pass after this correction. Temporary SQL error capture was
removed after diagnosis. Admission auditing also verified all 50 original failed lens facts with zero
trace/rank/eligibility mismatches and identical paired portfolio settings for every strategy.
WPF PID 12424 resumed at 13:56. At 13:58 both comparison ledgers reached revision 12 and referenced
completed cycle `bad95173-13c3-4dd7-a6da-d868a90660a5` for the 13:55 checkpoint. All 43 slots settled;
35 bars were usable and eight were unavailable. The host retained one healthy lease, source revision
count remained one, no Live comparison fills had occurred, and checked Shadow fill-timing violations
remained zero.
The 13:58 read also verifies five open experimental Shadow positions across the four strategies, while
all four Normal controls remain cash-only. The bypass is exercising the ordinary portfolio entry engine.

## Review questions

1. Why does each strategy need two fresh accounts instead of one new account beside its existing one?
2. Which rules does Portfolio rules only bypass, and which conditions may still prevent a purchase?
3. Why does a changed gate experiment need a fresh pair rather than a profile toggle on an old account?
