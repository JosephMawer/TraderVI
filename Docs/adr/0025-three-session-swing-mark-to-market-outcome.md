# ADR-0025: Three-session swing mark-to-market outcome

- **Status:** Accepted
- **Date:** 2026-08-23
- **Domains:** architecture, math-statistics, market-microstructure, risk-management
- **Related:** ADR-0002, ADR-0020, ADR-0021, ADR-0023, ADR-0024
- **Refined by:** ADR-0026 for separately versioned MFE and MAE measures, and ADR-0028 for a separate delayed intraday paper-exit challenger; this definition remains unchanged

## Context

The immediate problem is to measure whether Delphi's published recommendations have short-horizon economic value before the final swing exit policy has been selected. The parent problem is tuning Delphi for multi-day trades without confusing selector quality with an unproven stop, trailing, or profit-taking rule. The root goal is an honest, versioned feedback loop that improves Delphi under human strategic control.

ADR-0023 sets a roughly three-to-five-session swing as the primary direction but deliberately leaves the exact exit policy open. We can still measure what happened after a tradeable entry at the first three session closes. Those marks are useful evidence, but they are not realized strategy profit and must not be presented as though a complete trading policy already exists.

## Decision

Add the immutable tradeable definition `SwingMarkToMarket3` version 1. Apply it only to candidates published by at least one Delphi lens. Persist one candidate outcome per definition; join that outcome back to the published Continuation and Breakout lens rows when producing lens-specific reports. A symbol published by both lenses therefore has one price path but contributes to each lens's separately identified selection evidence.

Use ADR-0021's next-eligible-open rule. The first eligible session is the first XIU session after the observation session whose 9:30 a.m. `America/Toronto` open is later than the recorded run start. A pre-open run can enter that day's open; a run at or after the open waits for the next XIU session. Treat database `StartedUtc` values as UTC even when the SQL client returns an unspecified `DateTime` kind.

Search the first three eligible XIU sessions, including the initial eligible session, for the symbol's bar. Enter at that bar's actual open and persist the number of skipped eligible sessions. If fewer than three eligible sessions are currently available and no bar has appeared, remain pending. If none of the first three contains a usable symbol bar, persist terminal `NoEntry`; this is a valid observed execution result, not a zero return and not a data-quality failure.

Once an entry exists, require symbol bars aligned to the entry session and the next two XIU sessions. Remain pending until XIU establishes the full three-session path. After that point, a missing or duplicate symbol session or a non-positive required price is terminal `Invalid`, never silently replaced by another date.

Measure mark-to-market outcomes at these closes:

1. session 1: the entry session close;
2. session 2: the next aligned XIU session close;
3. session 3: the second aligned XIU session after entry.

For each horizon, persist:

- raw entry open and raw exit close;
- adjusted long entry `raw entry × (1 + 0.0025)`;
- adjusted long exit `raw exit × (1 - 0.0025)`;
- separately identified 10-basis-point slippage and 15-basis-point half-spread components per side;
- XIU's raw entry open and raw horizon closes;
- gross symbol return, net symbol return, XIU gross return from XIU's same entry-session open, and net symbol return minus XIU gross return.

Do not apply modeled costs to XIU: it is a market benchmark, not a second executed trade. Keep gross and net values explicit so this asymmetric comparison remains visible.

Call these results **mark-to-market measures**, not an exit policy or realized portfolio return. Version 1 applies no hard stop, warning action, profit target, trailing stop, or trend extension. Maximum favourable excursion, maximum adverse excursion, time-to-excursion, stop behavior, and the accepted three-to-five-session trade-management rule remain separate work. Adding any of those behaviors requires a new immutable definition or a separately identified metric whose contract is recorded before use.

Extend ADR-0024's coverage scorecard to active prediction and tradeable definitions. Prediction definitions expect every official candidate. Tradeable definitions expect only candidates with at least one published lens row. `NoEntry` is terminal and usable for coverage, but return scorecards exclude it from return averages and report its rate separately.

## Alternatives considered

- **Wait for the complete swing exit rule.** Rejected because entry feasibility and one-to-three-session economic paths can already test selector quality, provided they are not mislabeled as strategy returns.
- **Treat the third close as an automatic sale.** Rejected because the user wants healthy trends to be able to continue and has not accepted a three-session forced exit.
- **Measure every evaluated candidate as tradeable.** Rejected because an unpublished candidate was not a recommendation the user could act on.
- **Count missing entries as zero return.** Rejected because that hides execution availability and biases both return and coverage statistics.
- **Use the symbol's next dated bar without XIU alignment.** Rejected because halts and missing sessions would change the economic horizon and create an invalid comparison.

## Consequences

- Athena can collect short-horizon, cost-aware economic evidence before the final exit policy is resolved.
- The same price path can support separate Continuation and Breakout reports without duplicate outcome rows.
- Delayed and unavailable entries become measurable rather than silently excluded.
- No new SQL object is required; the existing versioned definition and candidate-outcome tables hold the new contract and result.
- The mark-to-market results cannot answer whether a stop, trailing rule, profit floor, or longer hold would improve realized performance.

## Review questions

1. Why is a three-session mark-to-market result not the same as a three-session forced exit?
2. Which candidates belong in the tradeable outcome population?
3. When does a missing entry remain pending, and when does it become `NoEntry`?
4. Why is XIU session alignment required after a delayed entry?
