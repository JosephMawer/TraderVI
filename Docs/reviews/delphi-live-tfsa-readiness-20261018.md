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

## Current readiness

SELECT-only metadata at **22:25 Toronto** finds one linked account, **zero saved initial risk
baselines, zero continuations and zero managed-session revisions**. This is a fresh storage
observation, not verification of current broker balances, login, orders or positions.
PID 20036 is responsive. There is no held/unexpired Live host lease and no fresh Monday official
run in this Sunday-night sample. Those observations do not prove a fault outside market hours;
Monday's source, host ownership and collection still need pre-open/in-session verification.

Source inspection confirms distinct unfinished boundaries:

- `BrokerManagedSessionWorkflow.ApproveAsync` rejects Delphi Live before saving authority or
  sending transition exits. `BrokerProtectionHandover` transfers/replays Daily Shadow only.
- `UnqualifiedBrokerRiskEvidenceSource` cannot qualify effective cash-event timing, complete
  event coverage and matching opening/closing books. A current broker display is not this source.
- The broker strategy-input reader needs exact Live policy/profile/source and continuity mapping;
  it currently chooses saved evaluation input without a complete account/profile adapter. The
  managed quote path also assumes a Shadow policy. These must be fixed before enabling Live.
- Initial/continued risk storage needs runtime recovery qualification; broker order lifecycle,
  populated assignment restart and one-owner execution-host rollout remain unqualified.
- Existing holdings need explicit ownership and carried protection. Normal's empty paper book
  is not permission to liquidate the TFSA. Manual/default ownership and ADR-0079/0081/0085 apply.

## Completed first increment and shortest remaining route

**Implemented/offline-tested:** the Trading dropdown and transition dialog can inspect the exact
Normal Live comparison through `ReviewBrokerAsync`. The review pins account, normal profile,
daily version and live policy; mismatched identities and bypass profiles remain rejected. The
dialog rechecks the ledger and comparison identity. Recommendation-only comparison approval and
the Delphi Live execution-family guard remain unchanged. A reviewed source never supplies paper
cash/holdings as real financial facts. The displayed return ordering remains explicitly daily;
this report, not that ordering, supports the integration-candidate recommendation.

This increment is built in an isolated output directory, **not installed into/restarted in the
running desktop**. Native transition-preview acceptance remains pending. Selecting Normal for
preview is not selecting a running TFSA strategy.

Proceed in these dependency-ordered increments:

1. **Qualify the actual account/risk source and storage.** Establish complete effective cash-flow
   coverage and exact family opening/closing valuation, qualify baseline/continuation save and
   recovery, then review/save actual initial references without clearing existing holds.
2. **Complete the Live adapter.** Preserve Normal's exact source/policy/profile, independent
   real-account confirmation/funds, real fills/position ages and ongoing risk state. Implement
   Live protection transfer/replay and family-aware quote collection. Test restart, missed
   protective triggers, pending exits and manual-order coordination before relaxing the guard.
3. **Qualify one execution host and native order lifecycle.** Reuse the shared journal, account
   lease, existing broker clients and original-ID recovery. Real-order tests require a separately
   reviewed concrete instrument/quantity and explicit authority; no test order is assumed here.
4. **Prepare the fresh activation packet.** Identify exact strategy/build, actual account and
   risk records, protection owners, retained/manual holdings, required exits, evidence limits
   and a reviewed cumulative purchase ceiling. Required exits reconcile before incoming buys.
   Review the evidence requirements in ADR-0055/0053/0067; no automatic permission follows from
   choosing the least-negative result or from an approaching deadline.
5. **Only after those checks, authorize and observe a supervised run.** Verify confirmed fills,
   active protection, stop-new-buys, uncertain-order recovery and subsequent-day continuation.

Monday can remain a paper observation day while this work proceeds. A recommendation/manual-order
workflow is a separate, narrower option; it must not be presented as automatic TFSA management.
There is no credible verified automatic-order launch commitment for Monday in the current state.

One account-specific consideration also remains: CRA says income from a business carried on in
a TFSA is taxable. Whether a particular short-term trading operation is a business is fact-specific;
automating it does not establish tax exemption. Review the intended activity with a Canadian tax
professional before real short-term automation. This is not a newly invented engine gate.
[CRA guidance](https://www.canada.ca/en/revenue-agency/services/tax/businesses/topics/tax-free-savings-account-tfsa-issuers/taxes.html).

## Validation and scope

.NET SDK 10.0.401. **102 focused tests pass**, including exact Normal/mismatched-profile review
and rejection of both original/Normal Live execution before any order or session save. WPF builds
with zero errors; cached dependency-security warnings identify moderate/high advisories on
`System.Security.Cryptography.Pkcs` 9.0.4, separately from existing compiler warnings. No fresh
dependency audit, full solution or SSDT qualification is claimed. The full suite passes **2,471
tests/eight native skips with collection parallelism disabled in the isolated output's runner
configuration**. Two ordinary parallel runs time out in the AI host-loop stop test; the second
also times out in desktop deadline cleanup. The stop test passes alone. Those timing failures
are preserved in the logs; a serial pass does not establish parallel timing stability. No AI host
logic/test deadlines were changed. The compiled suite also contains unrelated concurrent daily-
return edits; those remain outside this increment's commit.

Ignored evidence: `artifacts/delphi-live-tfsa-20261018/` contains SELECT scripts, export,
analyzer/independent verifier, summaries and build/test logs. Export SHA-256:
`F032D839B1B94370A2B03A094C0B3A9507AFDDA5D6525B04093A959974801806`.
The first readiness query failed inside the sandbox's SQL security context; the same SELECT-only
query succeeded with approved local access. Initial analysis helper assumptions about duplicated
endpoints and variable timestamp precision were corrected before the final verified results.

No database mutation, market/broker diagnostic, model training, live order, account assignment,
protection transfer, migration, operational restart, automation activation or push ran here.
Unrelated concurrent daily-return UI/source changes are preserved and excluded from this increment.
