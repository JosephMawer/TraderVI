# ADR-0126 — Engine health and shared diagnostic snapshot

**Date:** November 3, 2026. **Status:** Accepted. **Domains:** user-interface, architecture.

## Problem and decision

The immediate problem is that “Running · needs attention” and opaque restriction codes do not
explain what happened or what remains to be done. The parent problem is scattered operational
evidence. The root goal is for the operator and an assisting AI to understand the same evidence
and navigate to the appropriate reviewed action.

Open Engine on **Health & issues**, with **Controls** and **History** beside it. Use flat,
scrolling sections for restrictions, warnings/verification gaps, and current status/waits/operator
settings. Display explanation, impact, next step, evidence and times without expanders.
The footer's Open Engine action selects Health & issues. Existing operational controls retain
their handlers and permission boundaries.

Project existing read-only engine status and integrity receipts plus local collector/nightly
receipts and the dashboard's shared account/execution display. Read integrity status every 30
seconds; a failed read preserves the previous findings as last known. Missing, future-dated or
older-than-two-minute engine/integrity observations are verification gaps, never all clear.
Source-specific holding, collector and nightly freshness rules remain unchanged.

## Explanation and evidence boundaries

- One item represents each original unresolved integrity incident. Stable incident IDs remain in
  the diagnostic snapshot. Do not merge distinct incidents by similar wording.
- Describe inherited Paper recovery using its verified original-incident link. Explain that
  Real-only recovery excluded Paper history and whether a later Paper/All recovery covers the
  original boundary. A completed pass is not clearance; current-head and eligibility checks remain.
- An engine-continuity code does not distinguish every interrupted-shutdown, missing-witness or
  mismatch cause and does not prove corruption. State that uncertainty explicitly.
- Blocked worker receipts provide exception types, times and counts but no causal incident IDs.
  Keep a grouped worker observation and do not assert that a simultaneous hold caused every failure.
- Retained older failed audits remain in History. A newer completed boundary for the same kind/scope
  removes the older failure from the current-audit warning list only; original receipts are unchanged.
  Running passes remain visible, but Running does not prove progress or current scheduling.
- Recent warning notices retain their observation dates and unknown current applicability. They
  are not automatically additional root causes. Expected market waits and intentional pauses are
  separate from failures.
- History is the engine's retained latest receipts per kind/status/scope plus retained notices,
  not a complete resolved-incident log. Execution supplies its latest outcome, not all latent
  order-level blockers. Display these coverage limits.

## Human and AI access

**Copy diagnostic snapshot** serializes the same presentation records as `TraderVI.EngineHealth.v1`:
stable IDs/codes, domain, category, explanation, impact, next step, source, evidence, timestamps,
certainty and an action/approval boundary. It omits raw account objects, balances, credentials,
database identities and raw exception payloads. Existing bounded status messages and evidence
references remain diagnostic content; copying is local and operator-triggered.

The export is an investigation aid, not an autonomous repair API or a guarantee of exhaustive
diagnosis. Page reads and copying cannot start verification, clear holds, enable execution or
repair records. Existing explicit recovery controls remain under History, governed by
[ADR-0104](0104-scoped-recovery-and-sequential-history-audits.md) and
[ADR-0116](0116-temporary-paper-continuation-during-reboot-recovery.md).

### November 3 — issue-linked recovery evidence

Each integrity issue now includes a flat recovery-progress and clearance-requirements section.
Link a verification-pending incident only to its exact `Recovery/<run ID>` receipt, even when its
creation time follows the plan boundary. Other incidents use a scope-compatible recovery covering
their original boundary. Missing or stale evidence never claims eligibility.

Show verified records, completed groups, current group family and last saved checkpoint. Measure
speed only between increasing checkpoints of the same run and fixed plan; do not infer movement
from Running, equate unequal groups with percentage, or invent an ETA. A checkpoint older than
15 minutes asks for investigation, not an automatic restart or a definitive stuck diagnosis.

The checklist distinguishes facts observed in the receipt from current history, continuity,
database identity, immutable guards and explicit operator clearance still requiring engine checks.
Real-only clearance also rechecks saved receipts and current broker/observation state. Neither
the projection nor a completed receipt grants clearance. These optional structured `Recovery`
fields extend the existing v1 diagnostic contract; original fields and stable identities remain.

**Review recovery** opens the existing dialog with the incident's scope locked and matching run
selected. Opening is read-only. Focused clearance selects that original incident and only its
matching verification-pending hold; unrelated findings are excluded. The ordinary History entry
retains its scope-wide workflow. A known running recovery disables a duplicate start for an
overlapping selected scope; engine authority and explicit confirmation remain unchanged. Live
worker cause links and underlying verification exception details are still absent from the engine
contract and are not fabricated by this dashboard update.

## Alternatives and remaining limits

A list/detail split hides impact behind selection. A system-area layout spreads a shared cause
across sections. The operator chose Issues first and visible explanations. Fully structured
worker cause links and complete incident-resolution history would improve diagnosis further;
neither is fabricated from current receipts or required to render this initial view.

## Review questions

1. Why does an inherited Paper hold not prove corrupt Paper data?
2. Why can an old failed audit remain in History after a later successful pass?
3. What can a copied snapshot establish, and which recovery actions still need explicit authority?
