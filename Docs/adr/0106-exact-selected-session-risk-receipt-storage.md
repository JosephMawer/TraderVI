# ADR-0106: Exact selected-session risk receipt storage

**Date:** October 23, 2026. **Status:** Accepted implementation correction under the request to repair
the failed October 23 continuation approval; installation and operator approval remain separate.
**Domains:** architecture, risk-management. **Related:** ADRs 0081, 0083, 0092, 0103, 0105.

The immediate problem is that the selected-session risk receipt repeats its historical evidence in
both the request and resulting state, exceeding the existing 256 KiB write limit. The parent problem is
recoverable, explicitly approved session continuation. The root goal is real execution with preserved
financial history, exact risk references and deliberate dated permission.

Store new `ReviewedSessionContinuation` receipts in a versioned `SelectedSessionRiskReceiptV1` shape.
Keep the complete original request once. Store the state's scalar/reference fields, save timestamp and
full reconstructed-state hash; recover its carry, legacy continuations and selected-session evidence
from that same request. Before writing, require reconstruction of the exact full receipt. On reading,
verify the storage hash, representation, reconstructed state hash and existing row/request identities.
Keep the 256 KiB stored-event limit and all previous financial validation/approval rules unchanged.

Old inline receipts remain readable and old event kinds retain their original serialized bytes. No
historical row, domain object fingerprint, request identity or financial reference is rewritten. The
existing immutable table and transaction suffice; no migration, backfill or automatic retry is needed.
Unknown formats, duplicated state evidence, changed state hashes and oversized new receipts fail closed.
Raw SQL consumers of selected-session `State` evidence must use the repository reconstruction; existing
cash-flow event queries retain their `Request.CashFlows` path. Older binaries cannot read the new format,
so a rollback after an operator saves one needs a compatible reader and separate review.

Raising the limit or dropping old orders would conceal duplication or discard evidence. Changing the
domain serialization would alter financial fingerprints outside this storage defect. A generic archive
or compression scheme would add unnecessary schema or expansion behavior to this bounded correction.

An offline reconstruction using the saved original risk history measures 389,240 bytes inline versus
139,668 stored, with identical request/state hashes. Synthetic tests cover larger histories, legacy
reads, corruption and the unchanged ceiling. These checks save no financial event and do not establish
installed approval, host enable, live latency or order/protection qualification.

## Review questions

1. Why can the stored receipt become smaller while the reconstructed financial hash stays identical?
2. Why must an unknown storage format fail rather than fall back to a partially decoded risk state?
3. Why does a successful offline size check leave dated approval and host enable as separate actions?
