# September 23 supervised TFSA activation review

**Verdict: NO-GO for real strategy-managed orders.** Target date: September 23, 2026 Toronto.
Review finalized September 22 evening Toronto. This is a concrete readiness review, not order authority.
Do not turn the date target into an exception to a failed check.

## Exact target and decision

The operator selected the ungated Breakout three-position portfolio:

- Paper portfolio: `77bcbaa9-43fe-4259-aff7-25f30554e450`.
- Strategy: `BreakoutTop3`, Daily Shadow, `PortfolioRulesOnlyV1` (Portfolio rules only).
- Pinned daily version: `48864449-92f4-4e33-99cf-40039210786e`.
- Shadow policy: preserved V1; no policy settings, thresholds or sizing rules changed.
- Read-only definition review at 22:55:29 Toronto: managed version
  `9dd7d37c-b5fc-10d5-cc9a-ab20010b5056`, fingerprint
  `69568623C5B1F0DD7DFAD64D9C610F4F38C880BDAE369F8045B2FB40960F50A1`, implementation
  `CoreSha256:52855CD1E0E62300B2F8C7BD05E554571C6B595AA8F43A68B03DDEF5320196D1`.
- The final build's exact implementation/strategy fingerprints must be regenerated with
  `BrokerReadiness strategy-review` before a later activation. Build identity is part of approval.

The pending cash-reference question was already accepted September 22 (ADR-0083). Keep measured loss
percentage and holds: reference 10,000, value 9,800, then deposit 1,000 gives value 10,800 and adjusted
reference 11,020.408163…; measured loss remains 2%. Matched before/after books and complete effective
cash-event coverage are mandatory. A net total or occurrence timestamp alone is insufficient.

## What passes, and its evidence limit

| Check | Evidence | Limit |
|---|---|---|
| Existing work preserved | Local checkpoint `761fb78`; isolated `codex/supervised-tfsa-20260923` worktree based on it | No push or operational deployment |
| Existing reliability changes integrated | Compact-storage/backup controls, UI worker/quote changes and broker history controls in the checkpoint | Earlier installation evidence remains September 17; not reinstalled here |
| Exact selected strategy resolved | Native read-only SQL review identifies the comparison portfolio, daily identity and pinned policy | Definition identity is not strategy performance qualification |
| Signed-in native broker reads | September 22 **22:25:27 Toronto**: history, holdings, pending orders and regular day graph succeeded; stable activity checkpoint and matching holdings | Local order journal not independently checked; no mutation/lifecycle test |
| Historical day points | 79 points, September 22 09:30–16:00 Toronto | Does not prove September 23 morning publication timing or the Shadow valuation method |
| Core regression suite | **1,640 tests passed**, including 45 new managed-session/transport cases | Synthetic qualification, account/order transport and store; no SQL crash rehearsal or native fill proof |
| Desktop and database projects | WPF builds (261 existing compiler warnings, zero errors); separate Visual Studio SSDT project builds | No full-solution health claim; no database installation or app restart |
| Migration 040 | Offline ScriptDom syntax and canonical table/trigger parity pass | Prepared only; no runtime installation or crash/lease rehearsal |
| Durable workflow | Tested approval, intent before send, uncertain/lost responses, concurrent ticks, partial terminal exit/residual, exit-to-buy sequence, restart protection, scope/stale/conflict blocks and reference continuation | No production host is enabled |

Private, ignored evidence: original checkout `artifacts/first-real-session-20260923/`; isolated
worktree `artifacts/launch-validation/` including `native-readonly.json`, `strategy-review.json`,
`all-tests.log`, `managed-tests.log`, `wpf.log` and `sql-build.log`. No account identifiers, balances,
credentials, raw broker responses or model artifacts are committed in this review.

## Shortest remaining dependency route

1. **Qualify the native risk source.** Produce complete external-cash event intervals, effective cash
   timing and matching books. Verify Shadow's existing saved-price boundary method and morning
   availability; recover missing closes in sequence. The supplied capture/current graph do not establish
   this. Implement the qualified native `IBrokerRiskEvidenceSource`; the shipped default blocks it.
2. **Install and verify storage separately.** Individually review migrations 035, 039, then 040 against
   the actual schema. Take/verify the required backup, apply only under explicit installation authority,
   test append-only permissions, high-precision readback, competing SQL leases, interrupted saves and
   recovery in an isolated database first. Native read-only inspection at 22:55 Toronto found all
   three tables absent. No migration ran during this work.
3. **Establish account setup.** In the built app's TFSA account panel, use **Account risk setup…** to
   refresh the linked account and review preserved references, holds and protection. Save explicitly
   only after storage is installed. Verify recovery returns the original saved request and confirm daily
   and cash-flow continuations use real source evidence. This source UI has not been installed/launched.
4. **Complete a current transition review.** Use the target session's compatible adopted publication
   and current broker facts to identify exact non-overlap exits and retained cycles/quantities/costs.
   Name the current protection owner for each retained position and verify its carried floor/state.
   Set the supervised cash ceiling and identify the exact build, baseline and evidence records.
   These financial details remain pending a fresh qualified account/session snapshot; yesterday's
   candidate list cannot authorize tomorrow's liquidation.
5. **Qualify evidence and native execution.** Review comparable strategy periods/capital/costs/coverage,
   downside and untouched evidence under the established automation contract. Obtain separate explicit
   authority for native small-order lifecycle verification, including partial/uncertain/restart recovery.
   Offline fake broker tests cannot replace that evidence. No requested operational approval is pending.
6. **Verify and roll out the supervised host.** The composition factory joins the existing connection,
   shared journal/semaphore, source, reviewed calendar, SQL store and Core workflow without starting
   any loop. Supply the qualified native source and install the reviewed supervision entry point. Verify old protection
   ownership and manual/advisory coordination, pause/recovery, current inputs and refresh cadence.
   Prepare the final exact activation packet and only then request explicit execution/activation
   approval. Do not schedule or start automation in this preparation step.

SQL-backed runtime rehearsal, native source implementation/qualification, supervised host rollout and
the fresh financial activation packet remain development/verification work. These are not completed
merely by introducing interfaces. Session renewal, strategy replacement and resolving externally
invalidated reviews are deliberately not automatic recovery paths in this first-session implementation.

## Demonstration without operational effects

Run the focused tests from the isolated checkout:

```powershell
dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --filter FullyQualifiedName~BrokerManaged --verbosity minimal
```

`ReconciledExitFundsFirstBuyAndRestartProtectsItsRealPosition` exercises actual Core workflow calls:
save approval → reserve/send synthetic exit → import confirmed exit → transfer/activate → wait for
prospective evidence → reserve/send synthetic buy → restart → recover the same ID → protect its real
synthetic fill. Other cases demonstrate blocking partial/unknown orders, duplicate prevention, stale
and conflicting data, external changes, session expiry and preserved loss references. No SQL/broker
service is called by these tests.

The read-only console in `Operations/BrokerReadiness` resolves the chosen strategy and verifies the
saved signed-in broker session. It has no order, refresh-token, import, migration or activation mode.
The running app remains the only owner of its saved login.

## Operational approval packet still required

The final packet must include exact session/build/strategy identities, current account fingerprint and
baseline revision, reviewed exits/retained holdings and protection owners, usable funds and estimated
purchase ceiling, scoped evidence references, migration/readback receipts, native lifecycle receipts,
one host owner, pause procedure and supervision window. Name the operator responsible for carried
holdings after that authority expires and review close-time exits against regular-session order
availability; the first-session adapter cannot silently renew authority overnight.
Market orders have no guaranteed price.
Explicitly approving that packet is separate from selecting this paper strategy or signing in.

No broker order, automation activation, SQL mutation, model training, market-data collection, app
restart, scheduler installation, push or PR accompanied this increment. Dependency-security advisories
were not freshly audited; existing compiler warnings are not test failures.
