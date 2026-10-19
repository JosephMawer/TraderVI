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
   the original six launch qualifications and require the additional scoped evidence. Complete the
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

## What the no-transfer agreement means

After a verified cash-only anchor, the conditional reader checks exact cash against that anchor plus
confirmed net costs/proceeds of identified owned orders. It rejects unexplained cash, changed old
orders, unknown new orders, conflicting identities and stale account observations. It cannot prove
that equal and opposite outside cash movements never happened. That is why operator attestation is
a scoped assumption, not a complete native cash-event feed or a substitute for production evidence.

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
