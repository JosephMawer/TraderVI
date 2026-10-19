# Delphi Live performance and TFSA readiness — October 18, 2026

**Verdict: NO-GO for automatic TFSA orders on October 19.** Normal is the best recorded
paper result among the three accounts, and the recommended integration candidate; it is not a
proven profitable strategy or an activated TFSA assignment. The operator requested the fastest
route toward using the most successful Delphi Live portfolio for Monday. The date does not
replace existing qualification, account review or explicit execution authority.

The immediate problem is choosing the exact Live variant and connecting its rules to real
account state. This belongs to portfolio-to-TFSA handover; the root goal is dependable account
management with preserved financial history, risk references, protection and order recovery.

## Saved performance comparison

A SELECT-only export at **22:22 Toronto, October 18** reads the three saved paper books through
the **October 16, 16:00 Toronto** checkpoint. Each began with CAD 1,000 virtual capital.
No real-account amount, identifier, holding or order is part of this report.

| Paper account | Value at Friday close | Return since inception | Completed trades | Winning trades | Open holdings |
|---|---:|---:|---:|---:|---:|
| Normal | CAD 992.90 | -0.71% | 2 | 1 | 0 |
| Original reference | CAD 987.18 | -1.28% | 3 | 1 | 1 |
| Portfolio rules only | CAD 964.33 | -3.57% | 26 | 8 | 4 |

For a shared observed period, use September 10 at 09:35 through October 16 at 16:00 Toronto,
after the pair's partial September 9 activation. Normal returns **-0.71%**, Original **-1.28%**
and Portfolio rules only **-2.46%**. The latter began this common period below its original
capital; its common-period return therefore differs from its inception return. This ranking
does not use Friday's daily return or compare unlike starting dollar balances.

Observed drawdowns across the same 850 shared checkpoints are respectively **-2.67%, -3.46%
and -6.65%**. Drawdown means decline from a preceding observed peak. These are descriptive
lower bounds in magnitude, not qualified maximum-drawdown scorecard values: only **850/2,028
(41.91%)** expected checkpoints across 26 regular sessions are complete in all three books.
Missing checkpoints can conceal deeper losses. The production readiness threshold is not met.
Individual checkpoint coverage is 59.17% Normal, 59.07% Original and 41.96% rules-only.

All **67/67 saved fills** use the paper engine's `EstimatedFill` fallback: four Normal,
seven Original and 56 rules-only. These are simulated executions, not broker fills or proof
of executable spread/slippage. Normal's two completed trades provide little evidence of its
ability to make money; remaining in cash has limited its losses in this sample. More activity
in rules-only has not produced a better result.

All three now use progressive-profit-protection successor policies under ADR-0090, enabled
October 18. This history contains **no regular session under those new versions**. Prior
returns cannot be attributed to their prospective protection change or establish its benefit.
The original account has earlier starting history and is not a fresh matched control.

The analyzer reconstructs cash from all fills, reconciles open quantities and closed round trips,
and verifies the latest NAV against current cash and exact holding marks. At duplicated endpoints,
the last saved mark wins, including an incomplete last mark; no earlier valid value is substituted.
This follows the production scorecard's grouping. Twelve independent PowerShell arithmetic checks
agree with the Python cash, realized P/L, trade counts and both return calculations.
No missing mark is filled forward or repaired.

## Current readiness and accepted first-day scope

SELECT-only metadata at **22:25 Toronto** finds one linked account, **zero saved initial risk
baselines, zero continuations and zero managed-session revisions**. No later write is claimed in
this implementation session. Sunday has no held/unexpired Live host lease or fresh Monday official
run; that alone does not prove an off-session fault. Monday's exact source and ownership still need
verification. The current desktop has not received these Live preparation changes.

The first broker read failed Windows-protected-session access in the sandbox. The separately
approved native read then reported NotAuthenticated. After the operator signed in through TraderVI,
the same read succeeds at **22:58 Toronto**: two CAD whole-share holdings and no pending orders.
Private snapshots stay CurrentUser-encrypted; no financial identifiers or amounts are printed here.
This verifies login/account shape through the existing reader, not the newly prepared Live adapter.
It sends no order, refreshes no token and changes no saved credentials.

The operator has now resolved the launch capital and opening-holding questions:

- Use actual available capital, not the operator's approximate account estimate.
- Sell both current holdings in full first thing, subject to fresh regular-session checks. Confirm
  each original order and inventory before proceeding, and reconcile usable cash before any new buy.
- No deposits, withdrawals or trades outside TraderVI for the first supervised session. Stop new
  automatic buys if cash or orders fail to reconcile.

[ADR-0092](../adr/0092-scoped-supervised-delphi-live-tfsa-session.md) records that accepted scope.
It adds constraints; it does not waive the existing six launch qualifications. In particular, an
operator's no-transfer agreement plus cash arithmetic is not a complete native cash-event feed.
No real opening plan, queued sale, baseline, assignment or host permission is created. A default-off
desktop host and existing-assignment enable/stop controls are now built/tested in an isolated copy;
first execution approval and native risk/cash/source/order evidence remain unfinished.
**Automatic TFSA management remains off/NO-GO.**

## Implemented and offline-tested

The earlier preview increment (`9bdacb8`) exposed the exact Normal definition/transition review,
pinning its account/profile, daily version and Live policy while excluding recommendation-only
comparison approval. It retained the old Live-family execution rejection. The following source
increment replaces that blanket rejection with Live-specific adaptation and keeps all existing
launch-evidence requirements. It is not an operational approval.

- Live handover/fills use actual costs and position identity, preserve independent carried floors
  and pending exits, and avoid fictional paper or cross-family price highs.
- The broker reader requests the exact Live policy and comparison admission. It clears paper
  lifecycle/ownership. A latest conflicted stock/XIU observation remains an invalid observation;
  it cannot disappear to expose an older confirmation.
- Completed bars replay real protection. Valid historical signal exits remain latched through
  later rebounds and next-session recovery. Live does not acquire Shadow's Session-2 time exit.
  A bar low cannot become an observed bid-based breach.
- The workflow saves raised protection and latched exits before requesting a later trigger bid.
  Actual funds, ordinary gates, loss holds, original-ID recovery and one-owner submission remain.
- Reviewed opening sales accept stable request IDs and recover those same IDs after restart.
  The pure opening-plan policy requires fresh matching inventory/no pending orders, waits before
  regular open, stops after close, and refuses changed holdings or partial/cancelled outcomes.
  The protected immutable file store is tested with synthetic data; no runtime plan is installed.
- The conditional scoped cash reader admits only exact anchor cash plus identified owned net
  trade costs/proceeds, including fees. Unknown/changed/duplicate orders, unexplained cash and
  stale or expired observations block new buys. It issues no daily continuation/reference reset.
  Additional scoped review evidence cannot substitute for any original qualification.

The [sequence concept](../concepts/supervised-tfsa-opening-sequence.md) explains the distinction
between a planned step, a durable order intent, a native fill and usable proceeds.

## Remaining route

1. **Qualify native risk/source and storage.** Complete cash-event and opening/closing boundary
   evidence and boundary/crash recovery. Baseline save/readback and hold-chain continuation now
   pass in isolated native SQL after correcting continuation record mapping; this supplies no
   native valuation/cash-flow evidence. Preserve known references and
   holds. The new conditional reader does not supply the missing broader production proof.
2. **Verify the exact Live adapter natively.** Check Normal policy/profile/daily identities,
   continuity and raw observations, actual held-cost mapping, current quotes and missed exits.
   Passing synthetic tests does not establish current data coverage or executable prices.
3. **Qualify the prepared shared host operationally.** Core host and desktop enable/stop/poll
   integration now reuse the existing client, encrypted journal, account gate and SQL lease. The
   built morning copy remains off. It starts an existing approved assignment, not a missing first
   review or opening liquidation. Finish that review/opening composition after native prerequisites.
   See the [morning record](delphi-live-morning-decisions-20261019.md) for current source/test evidence.
4. **Complete native lifecycle qualification and a fresh activation review.** Real orders and
   activation remain operator-controlled actions; this agent does not submit them or enable an
   automatically trading account. Do not manufacture native fill/cancel/restart evidence from tests.
   Two confirmed opening sales would qualify only their observed path. Use actual available cash
   and the exact installed build/source in the review, retaining all required launch checks.
5. **Observe real operation only after activation.** Verify confirmed fills, durable protection,
   stop-new-buys, uncertain-order recovery and next-session permissions separately. The scoped
   no-transfer permission expires at the reviewed close without releasing saved holds or authorizing
   next-day purchases. Ordinary entry gates can still produce no buy.

There is no verified automatic-order launch commitment for October 19. The accepted target and
liquidation instruction remain recorded; neither successful sign-in nor deadline supplies execution
qualification. A manual-order workflow is a separate, narrower capability.

## Validation and scope

**Risk-storage correction after desktop checkpoint `0563341a`:** 91 focused baseline/continuation
tests and a separate native SQL test pass. The actual baseline Save/Find/Read and continuation
Append/Find/Read run against canonical risk tables/triggers with synthetic parent facts. The test
reproduced the existing extra-column record-mapping failure, now fixed with explicit continuation
columns; it verifies retained holds/references, timestamps, request retries and a two-event chain.
No actual account baseline is saved, and native cash/boundary/order evidence remains unqualified.

**Subsequent host increment:** 127 focused checks pass; the full suite passes 2,518/eight native
skips with serial collections. The isolated native SQL session rehearsal also passes, now including
Normal Live protection/stopped-mode readback with synthetic transports. Five offline WPF renders
and command-target/status checks pass; Release builds succeed with existing compiler/cached Pkcs
warnings. No operational application startup or real host activation is performed. Baseline-save,
continuation, native cash/source and actual broker lifecycle qualification remain distinct. The
following earlier adapter validation retains its original scope.

.NET SDK **10.0.401**. The final **179 focused tests pass**. The full Core suite passes **2,501
tests/eight native skips**, using disabled collection parallelism in the isolated output runner
configuration. Tests cover real-cost transfer, save-before-bid, prior-session exit latching, scope
expiry/unknown cash, original-ID sale recovery, opening-plan inventory and protected immutable
recovery, in addition to the existing session/readiness tests. WPF Release builds with **zero
errors and 262 existing compiler warnings**. Build/test output is isolated from the running app.

Cached `System.Security.Cryptography.Pkcs` 9.0.4 moderate/high advisories were reported in the earlier
restore and remain separate from compiler warnings. There is no fresh dependency-security audit,
complete solution or SSDT claim for this change. Earlier ordinary parallel full-suite runs timed
out in AI host-loop stop/desktop cleanup; the isolated stop test and serial suites pass. No AI
logic or deadlines were changed, and a serial pass does not prove parallel timing stability.
The first follow-up build found an ambiguous ImmutableArray.AddRange overload; it was corrected
before the passing final tests/build. The independent 12-check paper arithmetic verification and
performance export from the preview increment retain their original scope.

Ignored evidence remains in `artifacts/delphi-live-tfsa-20261018/`: performance export/analyzers,
`native-account-read.log` and protected snapshot, `happy-path-focused.log`, `happy-path-full.log`,
`happy-path-wpf-build.log` and isolated output. Performance-export SHA-256 remains
`F032D839B1B94370A2B03A094C0B3A9507AFDDA5D6525B04093A959974801806`.

This preparation session performs no SQL mutation/migration, market collection, training, real order,
opening-plan installation, account assignment, protection handover, operational restart, trading
activation or push. The broker call described above is read-only. Concurrent broker-read-helper and
editor work is preserved and excluded from this increment's commit.
