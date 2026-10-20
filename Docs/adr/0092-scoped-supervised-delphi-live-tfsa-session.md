# ADR-0092 — First supervised Delphi Live TFSA session

**Date:** October 18, 2026; limited-trial amendment accepted October 19. **Status:** Preparation implemented; actual qualification/activation pending.
**Domains:** architecture, risk-management, market-microstructure.
**Related:** ADR-0053/0055/0067, ADR-0075/0076/0079–0086, ADR-0090.

The immediate problem is getting the Delphi Live engine connected to the real TFSA for October 19.
This belongs to portfolio-to-broker handover; the root goal is actual, recoverable account management.
The operator asks to follow the happy path after reviewing the three negative recorded paper returns
and incomplete launch evidence. Normal is the working target. This is a first supervised real-money
trial with disclosed evidence limits, not a declaration of profitable or fully qualified automation.

## Explicit operator instructions

- Use the broker-confirmed available capital; the operator's approximate amount is not a sizing input.
- Sell both currently held stocks in full first thing, then use confirmed available proceeds. This
  explicitly selects liquidation for these two holdings rather than overlap retention at this launch.
- For tomorrow's first supervised session, no deposits, withdrawals or trades outside TraderVI.
  Stop new automatic buys if cash or orders stop reconciling. The operator explicitly accepts this scope.
- Existing stops, account loss holds, source validity, normal daily gates, whole-share sizing,
  one-owner submission, intent-before-send and original-order recovery remain in force.

The connected-account read at 22:58 Toronto verifies two CAD whole-share holdings and no pending
orders. Actual identifiers, quantities and amounts stay private. No order was submitted by that read.
Every opening order still needs fresh regular-session account, quantity, pending-order and quote checks.

## Bounded first-day cash source

**October 19 amendment:** the awake operator explicitly approved using the two confirmed full sales,
saved initial risk references and exact cash/order reconciliation **in place of** full-production
`cash-events` and `opening-values` qualifications for a supervised first-session trial. The prior
October 18 rule requiring those two qualifications even for this trial is superseded only when the
new `LimitedFirstSessionTrial` flag is explicitly saved. All four other original checks remain:
`native-order-lifecycle`, `strategy-evidence`, `restart-recovery`, `installed-storage`, plus the five
scoped checks below. Ordinary production approvals and older no-transfer scopes retain all six.
No synthetic test, checkbox or scoped proof may issue any of the remaining native qualifications.

The scoped producer recovers the exact immutable import used by the saved cash-only baseline and
the two original full-sale records with verified inventories. It preserves the original references
and holds, never establishes a replacement baseline. Its protected scope evidence states the
conditional limitations. It can publish incomplete preparation, which leaves approval blocked;
independently reviewed native references are retained unchanged. Scope retries recover the original
anchor and proof. `PurchaseExpiresAt` must equal the reviewed session close; the account source and
execution workflow stop new purchases at expiry. Ongoing assignment/protection may remain, but no
next-day buying permission or general transfer support follows. This is not full-production readiness.

`SupervisedNoTransferCashReconciliationV1` is explicitly conditional on the operator's no-transfer
agreement. After both verified sales, capture a fresh reconciled cash-only book and save/preserve
initial risk references using ADR-0081's setup observation. Do not label that reference the market open
or reset any known reference or hold. The cumulative purchase ceiling is derived from real available
cash/buying power after confirmed sales, not anticipated proceeds or the approximate amount.

Each later observation must preserve the original orders and account identity. Only identified owned
strategy orders can explain cash changes; exact net proceeds/costs, including fees, must reconcile.
Unknown orders, corrections, missing records, stale snapshots or unexplained cash stop new buys.
Existing protective exits remain independently evaluated with fresh order preflight.

This source does **not** claim a complete native cash-event feed, effective transfer timing, or proof
that opposing outside movements cannot cancel. That limitation is why the explicit first-day scope
matters. It issues no daily opening/closing continuation or capital-flow adjustment. It expires at the
reviewed October 19 regular close; it cannot authorize next-day buys, reset references or clear holds.
General/native transfer qualification under ADR-0083 remains future work. This conditional source
does not satisfy production cash-event qualification; only the explicit October 19 trial amendment
permits its narrower alternative for first-session purchases.

## Trial approval and opening sequence

The launch is separately identified as a supervised trial. Its approval records exact account/build,
Normal source/profile/policy, no-transfer scope, actual initial references, available-capital ceiling,
durable storage/recovery checks and the operator's trial instruction. Two original-ID opening sales
must have complete native fills and reconciled inventories before approval. No partial, uncertain or
outside result may be treated as a successful liquidation or spendable proceeds.

The scope's additional qualification labels are `supervised-no-transfer-cash`, `scoped-initial-reference`,
`opening-native-sale-fills`, `operator-supervised-trial` and `scoped-durable-recovery`.
Do not forge ordinary full-production `cash-events`, `opening-values`, `native-order-lifecycle` or
`strategy-evidence` qualifications from these narrower facts. The October 18 no-transfer answer alone
waived nothing. The subsequent explicit October 19 amendment replaces only the first two for the
flagged limited trial; the other four launch checks and every scoped check remain required.
Native full-fill sales validate their observed path; they do not prove native buy execution, partial
fills, cancellation or power-loss behavior. Those limits remain visible and unobserved cases stop or
recover through existing tested controls. No actual engine buy or qualification order is placed by
this implementation session. Operator-controlled activation and real orders are separate steps.

Use the existing client, protected manual-order journal, shared account gate and SQL assignment
lease. A durable opening plan pins the two current holdings and deterministic request IDs. A restart
or lost acknowledgement recovers those IDs; it never generates replacement sales. Required sales
must reconcile, source/risk checks must pass and the actual cash must be available before any buy.
No new UI declaration, paper balance or simulated fill can supply those facts.

The same approved strategy remains selected; the first-day scope permits new purchases only on its
date. Expired/missing future risk permission stops buys while independently permitted protective exits
can continue. Ongoing assignment never replenishes the cumulative purchase ceiling. Normal may produce
no buy if its ordinary gates, confirmations or current data do not qualify; the deadline grants no entry
exception. The October 18 progressive-protection version has no regular-session performance record yet.

## Implementation and verification boundary

Core Live transfer/replay, exact-policy/profile observation binding, save-before-trigger-bid ordering,
owned-order cash checks and deterministic opening-sale recovery are implemented/offline-tested.
Historical valid Live signal exits latch through later rebounds and next-session recovery; a bar low
cannot stand in for an observed bid touch. A pure opening-plan policy and protected immutable file
store are tested, but no real opening plan has been saved and no order is queued. The default-off
desktop host and explicit existing-assignment enable/stop controls are now connected in source and
an isolated build. Operational installation, native first-review evidence, risk qualification
and actual fills remain unfinished. No SQL migration is needed for optional scope fields inside
existing immutable session JSON. See the [sequence concept](../concepts/supervised-tfsa-opening-sequence.md)
and [dated evidence](../reviews/delphi-live-tfsa-readiness-20261018.md).

The subsequent Core host adds an explicit protected operator switch for one already-approved SQL
assignment and build. Creating that switch cannot create an approval, baseline or opening order.
The regular-session poll coalesces overlap, preserves original orders and saved stopped/paused modes,
and uses bounded read-failure backoff. A changed build/assignment cannot inherit the old switch.
The host/permission store and desktop wiring are built/tested. One shared desktop broker timer polls
it; it reuses the journal/gate/clients and SQL assignment lease. The independent Stop execution host
command does not require broker sign-in. The initial setup screen enabled an existing approval only;
the subsequent first-review path below adds explicit cash-start approval without evidence overrides. Actual native operation remains
separate. The native SQL rehearsal includes exact Normal Live protection/stopped-mode recovery with
synthetic transports; it does not qualify real broker orders or baseline/continuation saves.
The subsequent canonical risk-storage rehearsal qualifies baseline Save/Find/Read and two hold
appends/recovery after correcting continuation column selection. It preserves references, both
holds and original request IDs; native boundary/cash-flow/broker evidence remains separate.

## First execution review path

### October 19 accepted native first-day contract

After reviewing the concrete native qualification plan, the operator explicitly approves a further
first-day contract. This is separate from the earlier cash/opening amendment. The new
`FullFillOnlyNativeTrial` flag requires `LimitedFirstSessionTrial`; ordinary production, research
promotion, older scopes and their evidence requirements do not change.

For this engineering trial, `strategy-evidence` requires the native exact Normal/profile/policy/
daily/build definition and fresh source review, with the previously reviewed comparable performance,
costs, coverage and downside limitations disclosed and accepted. It does not claim profitability or
production statistical readiness. ADR-0055/0062's recommendation-only exception is not reused as
broker authority; this is the separately accepted scoped alternative. ADR-0022 research thresholds
and provenance/leakage/invalid-report prohibitions remain unchanged.

Native order evidence requires both original full sales, one actual manual one-share buy and its
separate full sale, all acknowledged and matched to native history, costs/fees and empty inventory.
The cash-only baseline must follow every verification trade. Then the app captures a protected
checkpoint. After a normal restart of the **same executable**, it reads the same four original order
IDs, native history and the unchanged baseline through independent Read/Find calls. Current installed
immutable triggers/max JSON storage, continued references/holds and current source must still match.
One normal restart therefore checks orders and risk references without a second verification position.

Partial/cancel/uncertain-send/power-loss and populated engine recovery remain explicitly unobserved.
Any partial, cancelled, rejected/conflicting or uncertain automatic outcome latches
`TrialOrderReviewRequired`. The workflow may read original IDs, but reserves no additional order or
automatic residual. The hold survives restart and ordinary Resume buying; it does not change the
operator's saved buying mode or reset financial holds. Further execution needs explicit operator
recovery/review. Existing pending zero-fill acknowledged orders may be read until resolution.

The native producer publishes only after the actual before/after observations satisfy these checks.
It writes an encrypted, hash-linked review and all four native/five scoped references, pins the
original cash anchor/account/strategy/build/session and expires at the reviewed close. Every reference
is marked `FirstSessionTrialOnly`; it cannot qualify production/older scopes. It preserves existing
packet conclusions and never replaces a baseline, issues a financial approval or enables the host.
No synthetic test output or operator checkbox can create native observations. The general-production
cash/native qualification publisher remains outside this bounded implementation.

The [qualification review and operator steps](../reviews/delphi-live-native-qualification-20261019.md)
own current evidence and remaining manual observations.

The desktop now prepares the accepted cash-start Normal review through the existing shared account
synchronization source. Preparation may append the normal reconciled account import; it creates no
baseline, execution approval, host permission or order. Both holdings must already be fully sold,
actual inventories empty and reviewed initial references present. Use the existing explicit Sell all
controls for these first supervised sales; no new automatic opening-liquidation owner is added.
After authorization to prepare a manual buy/readback verification, the subsequent desktop review
allows explicit selection of the two original verified sale records. Any later test-position sale
stays separate. The original request IDs/native facts must still match the baseline import; there
is no latest-two or first-two heuristic and no change to the required financial/qualification checks.
The [manual sequence](../concepts/supervised-tfsa-opening-sequence.md#operator-review-bounded-native-order-verification-october-19)
is prepared for operator review, not submitted orders or native qualification.

The review derives its purchase ceiling from actual cash/buying power, retains all six original
checks for production/older scopes or four native plus five scoped checks for the explicitly flagged
October 19 trial, and lists missing/incompatible evidence. An independently
reviewed qualification packet pins account, exact strategy/build/session, expiry, reviewer and proof
references. Its read-only consumer uses Windows-protected bytes and verifies each referenced file's
SHA-256 before preparation and again before approval. These establish identity/integrity, **not** the
semantic truth of native qualification. The subsequent expressly approved native first-day producer
above adds bounded actual-observation qualification/publication; there is no checkbox override or
general-production issuer. Actual observations remain an operational dependency; synthetic reports
must never be published as that packet.

Explicit approval rechecks the current source plus actual account/risk through the existing durable
workflow and saves no order. The host remains off until separately enabled. Changed evidence,
cash, source/build, missing references, holdings or retained holds prevent approval; uncertain saves
recover through the existing SQL assignment before a new attempt. This needs no SQL migration.

## Accepted October 20 next-session amendment

The October 19 trial expired without activation. The operator requests preparation for October 20
while preserving completed trades and initial references. The concrete
[October 20 contract](../reviews/delphi-live-next-session-contract-20261020.md) defines an explicitly
reviewed, one-session cash-only carry that preserves original reference records/holds and original
verification order IDs. It claims no historical close/open and changes no production/older scope.
The operator explicitly accepts that complete dated contract on October 19 in the current chat.
Implementation and native observations remain required. A separately reviewed append-only trial
carry may preserve the original references/holds for October 20 only, without reconstructing a
historical close/open or changing production/older scopes. No financial save or activation is granted
by this ADR edit. All nine checks, current source, same-copy restart and separate operator carry save,
assignment approval/host enable remain required; buying expires at the reviewed October 20 close.

## Review questions

1. Why does a verified no-transfer agreement plus cash reconciliation differ from a complete cash feed?
2. Why must both opening sales reconcile before the capital ceiling and first strategy purchase exist?
3. What keeps working when the first-day cash permission expires, and what cannot reset on restart?
