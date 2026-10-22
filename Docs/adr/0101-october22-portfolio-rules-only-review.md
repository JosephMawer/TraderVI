# ADR-0101: October 22 Portfolio rules only review

**Date:** October 21, 2026. **Status:** Accepted for preparation by the operator: “Prepare tomorrow’s review only.”
**Domains:** architecture, risk-management, user-interface.
**Related:** ADR-0081, ADR-0086, ADR-0092, ADR-0095–0100.

The immediate problem is tomorrow's eligibility after today's dated permission expires. The parent
problem is deliberate approval of the exact source and installed implementation while preserving
financial history. The root goal is reliable real-account execution with unchanged risk controls.

Prepare one October 22-only review for the existing, already source-switched Portfolio rules only
assignment. This narrowly supersedes the no-October-22 restriction of ADR-0097 and the October-21-only
review route of ADR-0098 for this follow-on path. It is neither an executed approval nor recurring
renewal. October 21 permission and all prior records retain their original content and expiry.

The operator reviews on October 22 before 16:00 Toronto, including pre-open, with the host stopped.
Require the exact prior saved cash-only evaluation, zero automatic orders/positions/protection,
no invalidation, financial or order-review holds, fresh matching account and unchanged book,
installed immutable storage and the exact same complete source/policy/daily/model definition.
The running implementation may change and must be displayed and approved explicitly. Changed
strategy definitions, unexplained cash, outside trades or other account changes require separate
recovery; this path does not choose another portfolio or change policy settings.

Append a new linked risk event and October22ContinuationApproved session revision. Keep the original
approval, October 21 continuation, source switch and build-review records, financial memory,
reference values/provenance, cash anchor, opening sales, buying/paused modes and cumulative unused
purchase allowance. An expiry observation is not a qualified closing valuation: do not advance
Through or LastCompletedSession, invent an opening balance, reset a hold or refill the allowance.
Historical coverage gaps remain. Absent new fields are omitted so historical serialized hashes stay
unchanged. No schema migration or protected grant/certificate rewriting is required.

Review lasts two minutes. Approval repeats current source/storage/account/build checks and leaves
the host off. Risk is appended first, then read back before the session append. An interrupted pair
must recover the identical request; it cannot create execution authority while incomplete. Repository
validation fences the prior revision/hash and rejects changes to old authority or financial memory.
The operator separately enables a grant bound to the new dated approval and exact implementation.
Old grants cannot transfer, including a grant for the same source/build with the old permission ID.

Pre-open review requires October 22's latest Valid OfficialPaper input with matching config/models
and immediately preceding-session market date. Stored source definition evidence is not a current
intraday signal. Arming performs no pre-open execution; at/after 09:30 current source/storage,
account/order/risk facts and fresh post-approval market inputs must pass ordinary checks. Preserve
the seven-minute data allowance, quote windows, confirmation/maturity, whole-share sizing, cash/
account/stock limits, protective exits and full-fill-only/uncertain-order recovery restrictions.
The existing 15:45 entry cutoff remains distinct from the exact October 22 16:00 outer buy expiry;
both reservation and broker transmission checks retain their boundaries. No October 23 permission.

Preparation/builds/tests are offline. Normal operator stop/closure and verified zero owners precede
standard Release replacement. Installation does not approve, enable, launch the app, collect data or
prove tomorrow's latency/orders. Native timing and decision-trace publication require subsequent
operator-approved operation. No order or trade count is promised.

## Review questions

1. Why does carrying original references preserve risk history without proving a new opening valuation?
2. Why can tomorrow's approval neither inherit yesterday's grant nor authorize October 23?
3. How do the 15:45 strategy entry cutoff and 16:00 permission expiry differ?
