# Preparing the first supervised TFSA session

The immediate problem is turning the operator's two-holding opening instruction into a recoverable
sequence. Its parent is the Delphi Live handover; the root goal is preserving real financial facts
and protection while one approved strategy manages the account. [ADR-0092](../adr/0092-scoped-supervised-delphi-live-tfsa-session.md)
records the accepted first-session scope. This note describes prepared software, not enabled trading.

## Sequence and evidence

1. Review a fresh broker snapshot and pin the exact account, two instrument identities and whole-share
   quantities, session date, Normal portfolio/policy/daily version and running implementation.
2. Before regular opening, wait. At opening, refresh actual holdings and pending orders. A changed
   holding, quantity, instrument or pending order prevents expanding the reviewed liquidation.
3. An explicitly submitted opening sale retains its original request ID. After uncertainty or restart,
   read that order; never submit it again under a replacement ID. Require its complete native fill and
   remaining-inventory verification before considering the second sale. Partial/cancelled outcomes stop
   this simple path for review rather than silently authorizing a residual sale.
4. After both complete sales and empty verified inventory, reconcile cash. A planning decision called
   `PrepareAccount` is only permission to prepare the next review. It creates no baseline or assignment.
   Usable capital is the lower of reported CAD cash and buying power, not expected proceeds.
5. Establish/read back actual initial risk references without resetting known history or holds. Keep
   the four native/five scoped launch checks for the explicitly approved limited trial (all six original
   checks for older scopes/production). Complete the
   shared host, native lifecycle and storage checks before any operator-controlled activation.
6. In Execution setup, prepare the first execution review. It shows the exact current Normal source,
   actual available purchase ceiling and all native qualification references. Missing evidence means
   NO-GO. Approve the completed review explicitly, then enable the reviewed host separately.

The encrypted opening-plan store is immutable. Its stable IDs survive restart; progress belongs to
the existing original-order journal and SQL receipts. Neither a saved plan nor a successful login
means a sale was submitted or a strategy was activated. No real opening plan is installed in this
increment, and no desktop execution loop consumes it.
The simplest prepared first-day route therefore uses the existing two explicit Sell all actions,
followed by account risk setup and the new first execution review. The first review is implemented;
independently qualified native evidence and its installed packet are still required. A successful
protected-file read or matching hash cannot supply missing native cash, boundary or order proof.

## Operator review: bounded native order verification, October 19

**Prepared for manual review; no orders have been submitted and this is not a GO for automation.**
The operator explicitly authorized preparing the two requested full sales followed by one minimum-size
buy/readback. A verification purchase has real price risk and must be sold again to restore the
cash-only start. Review that additional sale and actual costs before choosing to execute this sequence.
No security is selected or recommended by this procedure. The manual whole-share minimum is one share;
the current ticket must independently accept its instrument, quote and available cash.

**Before any sale:** keep the execution host off. The operator now explicitly approves the
[first-day native acceptance contract](../reviews/delphi-live-native-qualification-20261019.md).
Its checkpoint/review/packet producer is implemented/tested; actual manual observations still remain.
Use the validated **native-review working copy** after its supervised switch. Older copies lack this
checkpoint/publication route. Do not overwrite a running copy or carry approvals across builds.
Record account/instrument/order details only in private local evidence, never committed documentation.

| Step | Operator action in TraderVI | Required observation before proceeding |
|---|---|---|
| 1. Review the two opening holdings | In Trading, confirm the owned TFSA, both current CAD whole-share holdings and exact displayed quantities. Refresh holdings/pending orders. | No pending broker or unresolved local order; account and quantities match the intended two holdings. No baseline or host approval is saved yet. |
| 2. First full sale | Select the first original holding and use its existing **Sell all** action. Personally review/submit the displayed full quantity. Open **Manual sells… / Check sell status**. | Complete native fill, actual net proceeds/fees and verified empty remaining inventory for this holding. Retain its original request ID. |
| 3. Second full sale | Repeat only for the second original holding, using its freshly displayed full quantity and separate original ID. | Both original sales fully filled/resolved, no remaining holdings or pending orders; cash/import/ledger reconcile. Anticipated proceeds are not buying power. |
| 4. One-share verification buy | Choose the instrument yourself from the existing supported manual-buy selection. Open **Manual buy…**, enter **1**, then **Refresh checks**. Personally review cash, quote, estimate and actual market-order submission. | Existing safety checks pass; the original buy ID receives a complete native fill/readback with actual cost/fees and the one-share holding is visible. Acknowledgement alone is not a fill. Do not enable the engine or hand this position to strategy management. |
| 5. Return the verification purchase to cash | Select the actual test holding and personally review/submit **Sell all**, using fresh ordinary checks. Read back that sale and reconcile account history. | Test position completely sold, no holdings, pending or unresolved orders; actual available cash/buying power and all trade costs reconcile. Keep this third sale separate from the original two. |
| 6. Save initial references | Use **Account risk setup…** to review/save/reopen the actual cash-only setup after all four trades. Preserve existing references/holds. | Original baseline/import recovered. No test trade may follow this anchor; a new snapshot or net-zero round trip cannot replace it. |
| 7. Capture and restart | In **Execution setup…**, click **Capture native recovery checkpoint**. After success, close normally and reopen the **same executable**, with host off. | Protected native before-state contains the four completed original IDs, saved baseline/Read/Find, references, source and installed-storage checks. It creates no approval/order. |
| 8. Complete native review/publication | Click **Prepare first execution review**. Select exactly the two original opening sales by symbol/quantity/time/ID, excluding the test sale. | App reads back the four original orders and unchanged baseline, verifies fresh source/storage/risk and publishes the first-day packet only if every check passes. Missing/contradictory evidence blocks; no synthetic report or checkbox qualifies it. |
| 9. Activation remains separate | Review the fresh exact Normal source, actual cumulative ceiling, close deadline and every evidence item. | Only a complete compatible review permits the operator to approve, then separately enable the host. No grant is created by this runbook or sale selection. |

On any partial, pending, uncertain or contradictory result, stop this sequence. Use the status control
for the original ID; never delete a journal/attempt marker or send a replacement under a new ID.
Do not induce a disconnect, partial fill or power failure with real money to fill a qualification box.
If the session closes, the October 19 trial does not roll forward or gain next-day buying permission.

| Native check still required | Evidence already observed | What this manual sequence can and cannot add |
|---|---|---|
| `native-order-lifecycle` | Native historical/account reads reconcile; submission remains unverified. | Actual full sell/buy/readback paths qualify only the expressly approved first-day scope if completed. Partial/cancel/uncertain outcomes remain unobserved; the durable hold prevents automatic progression on them. |
| `strategy-evidence` | Monday Normal publication is present; paper limitations/negative inception returns are disclosed. | No strategy-performance qualification follows from a manual buy. Exact current policy/profile/daily/build/source and the independent evidence review remain required. |
| `restart-recovery` | Earlier isolated native SQL rehearsals and synthetic transport recovery pass. | Same-ID manual fill and unchanged saved-reference recovery if steps 7–8 succeed. It does not prove populated actual managed-session/protection or uncertain-send/power-loss recovery. |
| `installed-storage` | Installed immutable tables/triggers, earlier isolated SQL save/reopen and a clean actual import/trade-sync receipt. | Actual reviewed baseline readback can add account-specific evidence; installed counts or a blank setup screen alone cannot qualify it. |

The bounded native producer/reader now exist; the actual packet is still absent until the native
observations above complete. Each reference is first-session-only, expires at the reviewed close and
cannot qualify production/older scopes. Checking off this table creates no evidence or authority.

## What the no-transfer agreement means

After a verified cash-only anchor, the conditional reader checks exact cash against that anchor plus
confirmed net costs/proceeds of identified owned orders. It rejects unexplained cash, changed old
orders, unknown new orders, conflicting identities and stale account observations. It cannot prove
that equal and opposite outside cash movements never happened. That is why operator attestation is
a scoped assumption, not a complete native cash-event feed or a substitute for production evidence.

The explicit October 19 amendment permits this bounded evidence in place of production cash-event/
opening-value checks only for a saved `LimitedFirstSessionTrial`. Four other native checks and all
five scoped checks remain. Preparation uses the baseline's original immutable import and actual
full-sale journal records; a newer snapshot cannot repaint the original reference. It publishes
protected scoped proof but cannot create the remaining native qualifications. Older scopes retain
all six production checks. Purchase permission ends at the exact reviewed close.

The permission expires at the reviewed regular close. It produces no later-day continuation and
cannot invent an opening value, replenish the cumulative purchase ceiling or clear a saved hold.
Existing protective exits remain independently eligible through the managed-session contract, subject
to current account/order preflight; this does not establish that an installed host is performing them.

## What Live adopts

The strategy adopts real costs, fills and position identities. It does not adopt paper holdings,
cash, previous confirmations or paper protection. Exact policy, Normal profile, daily source and
continuity remain required. Completed closes may raise protection; a later fresh observed bid can
trigger it only after the raised state is durable. Valid historical signal exits remain latched
through rebounds and restart. Missing prices or a bar low cannot manufacture an actual bid touch.
