# Documentation consolidation and learning — October 23, 2026

**In progress.** This bounded review records the documentation inventory, preservation work and
acceptance checks for the operator's requested learning/consolidation session. It does not replace
the project backlog. Immediate problem: the system and its documentation exceed comfortable working
memory. Parent problem: reliable understanding and session recovery. Root goal: dependable TSX
strategy research and deliberately approved Real management, with evidence and safeguards intact.

## First increment and reading path

Start at [README](../../README.md), then [Paper and Real](../system-design.md#paper-and-real-share-rules-not-account-state).
Read only task-relevant current status, roadmap and acceptance sections. Decisions and procedures
remain task-specific. Historical reports and the cross-chat register are retrieval destinations.

The first increment corrects orientation, explains Paper/Real ownership, and changes the documentation
workflow to update only affected authoritative owners. It does not compact every tracking file before
the operator has explored the system. No application, database, broker, collector or training ran.

Validation: exact staged diff and whitespace checks pass; 1,395 scanned local document links resolve,
including the repaired October 19 anchor. New Markdown/code targets and the solution reference exist.
Safety/build/change/commit/handoff sections match their prior text exactly. S53/S54 rows, the readiness
review, strategy checklist and design rules are unchanged. Application tests/builds were not rerun
for documentation-only changes. The unrelated Obsidian workspace edit remains outside this increment.

## Inventory and measured burden

Baseline: tracked Markdown at `7267acb`, measured October 23. Counts are physical lines, not reading
time or semantic complexity. Families classify each tracked Markdown file once.

| Family | Files / lines | Audience, owner and maintenance judgment |
|---|---:|---|
| Entry points and component guidance | 9 / 808 | New sessions and contributors. Navigation and working rules; root README was stale about execution and trained patterns. Keep entry concise; retain narrow component contracts. |
| Current tracking | 3 / 3,726 | Returning operator/agent. Status owns facts, roadmap priorities, register source recovery. Repeated Friday narratives and old current-tense states are unjustified; retain source IDs but remove parallel reporting. |
| Acceptance checklists | 3 / 1,716 | Implementers and reviewers. Own dependencies and completion criteria. Long installation/watch narratives duplicate evidence reports; preserve unchecked acceptance and distinguish source/test/install/native states. |
| Explanations, rules and procedures | 15 / 4,258 | Learners, implementers and operators. Architecture/system-design/models overlap; older hosting and training descriptions conflict with later changes. Detailed thresholds, causal timing and side effects must remain; volatile state belongs elsewhere. |
| Decisions and indexes | 109 / 12,800 | Decision reviewers. Own accepted choices, rationale, scope and supersession. Detail is valuable; dated implementation labels in indexes can become competing status. Historical ADRs must not be rewritten as if later rules always applied. |
| Concepts and drafts | 23 / 5,240 | Design/learning discussions. Explain relationships and alternatives. Some contain accepted amendments plus old plans; audit links and status before merging. Keep unique rationale and unresolved choices. |
| Reviews, questions and learning | 25 / 10,180 | Investigators and learners. Own detailed dated evidence, consequential questions and optional study aids. Evidence detail is justified; repeated current summaries and compulsory learning outputs are not. |
| Tracking archives | 3 / 2,167 | Historical retrieval. Preserve earlier decisions/evidence and anchors; no routine startup or ongoing milestone updates. |
| Operations and database guidance | 8 / 471 | Operators. Exact safe commands, scope, migration and authorization contracts justify detail. No simplification of substantive safeguards is proposed. |

The five previously named startup/reference files total **5,005 lines / 64,418 whitespace-delimited
words**: AGENTS 84, status 2,460, roadmap 676, strategy checklist 1,195, register 590 lines.
This is a full-file footprint, not a claim that every task had to read every line. Relevant-section
reading was already partly allowed; new rules make that explicit and make the register conditional.

In the last 30 commits ending at `7267acb`, **29 touched all four tracking files**, for 117 tracking
file touches; 24 commits changed Markdown only. This demonstrates repeated maintenance, not time
spent or wasted. An exact normalized 20-word-window comparison finds only 20 shared sequences:
the substantial duplication is mostly differently worded retellings, not identical copied paragraphs.
For example, the Friday close appears in status, roadmap, checklist, register and the owning review.

The initial static scan checked **1,361 local Markdown-document links** and found one unresolved
anchor, manually confirmed in status's October 19 switch link. The scan excludes external links,
code-fenced examples, bare/backticked paths, private artifacts and generated content; it is not a
claim that every reference or historical statement is correct. `Delphi/Notes.md` uses legacy encoding;
its content was read without rewriting it. Local audit outputs stay ignored under
`artifacts/documentation-consolidation-20261023/`.

## Workflow examples

These counts concern substantive documentation owners, not source/tests or optional evidence files.
The old milestone rule required four tracking updates; the old wrap-up prompt also defaulted to an
ADR, index updates, cards and a build. The new process has no fixed per-milestone file count.

| Example | Documents actually changed | Why |
|---|---|---|
| Question, no new finding | None (0) | No state or decision changed. |
| Small bug fix implementing an accepted rule, no tracked milestone | Status (1), if the fix changes a recorded current limitation | Record the changed implementation/validation state; commit/tests supply routine detail. No new rule, priority or dependency means no other tracking update. An entirely internal fix may need none. |
| Newly discovered operational blocker | Status + owning checklist (2) | Current readiness and an acceptance dependency changed. Link substantial diagnostic detail once. Roadmap changes only if priority order changes. |
| Meaningful design decision, priority unchanged | ADR + its two indexes; design rules if affected; checklist if new dependencies | Each file serves a distinct purpose. No automatic report, concept, cards, register entry or implementation claim. Record a pending consequential choice in open questions until decided. |
| Completed implementation milestone | Status + owning checklist (2) | Implementation/testing state and acceptance changed. Add roadmap only when completion changes priority order; installation/native acceptance stay open until evidenced. |

These are worked examples, not replacement quotas. Fix a contradictory claim wherever it still
exists. The one-time consolidation legitimately touches more files to remove those contradictions.

## Preservation and corrections

- Root README's outdated advisory-only/future-broker and trained-pattern summary is replaced by
  current orientation. Legacy model taxonomy remains in `models.md` pending its own reconciliation;
  current deterministic/trained separation stays governed by design rules and accepted decisions.
- Agent safety/authorization, build/test, change expectations and local commit rules are preserved.
  Only reading, learning-output and documentation-maintenance requirements are simplified. The old
  wrap-up prompt becomes an optional checkpoint aligned with the root guide; no safety rule is removed.
- System design's earlier verification-mode, dashboard-required and no-broker descriptions are scoped
  or corrected using current construction in `TraderVI.Engine/Program.cs` and
  `IndependentAuxiliaryPaperService`. Roadmap no longer lists engine extraction as unstarted.
- Status's old executive summary is explicitly historical; its `executive-summary` anchor remains.
  No evidence dates are advanced, and no full runtime qualification is claimed.
- S54 remains intact. The [Friday report](independent-engine-implementation-20261022.md#october-23-final-1605-watch-report),
  [failure chain](independent-engine-implementation-20261022.md#october-23-buy-preflight-account-lock-correction)
  and [October 26 assessment](independent-engine-implementation-20261022.md#october-26-readiness-assessment--october-23)
  remain in their owning review. Data/quote qualification is proposed, unstarted work; Friday's
  expired permission is not renewed. The strategy checklist's open acceptance items are unchanged.

## Learning record and remaining acceptance

The operator chose Paper versus Real as the first topic, correctly identifying transferred strategy
rules and separate confirmation, then questioning duplicate computation and "simulated histories."
The explanation now uses **Paper trade history**: observed market data can be genuine while fills,
cash and holdings are simulated. The Paper portfolio continues after Real approval. Shared rules
and inputs do not imply shared mutable account state; different fills/history can yield different actions.
The operator confirmed that distinction after the side-by-side stock walkthrough, then asked how
useful Paper returns are when Real trades diverge. The system explanation now separates strategy
evidence under Paper assumptions, execution feasibility and actual account outcomes. Source inspection
confirms that Delphi Live's scorecard includes tagged fallback fills while separately reporting their
count, no-fill counts and coverage. This is a clarified evidence limit, not a new performance gate
or an implemented Paper-to-Real attribution report. The full approval/protection flow remains to cover.
The next walkthrough traces how Delphi Live Paper saves a later-quote full fill and where fallback,
quantity and timing assumptions limit realism. The operator raises higher-price acceptance and
faster TMX requests as possibilities; they remain [open proposals](open-questions.md#monday-delphi-data-and-price-qualification--october-23-2026).
The recommendation is to keep explanation/cleanup here and investigate quote alignment separately;
the prompt below is prepared, not dispatched. The key correction is that Real compares its own
reference with the broker ask, not with Paper's purchase price.

- [x] Inventory families, baseline volume/churn, initial local-link scan and S54 recovery.
- [x] First orientation and workflow cleanup; preserve safety and operational evidence boundaries.
- [ ] Walk through daily selection, intraday confirmation, then permission/order/fill/protection.
- [ ] Compact status, roadmap and strategy checklist after a unique-information/anchor accounting.
  Retain Friday's failure history and Monday's active dependencies without duplicating the narratives.
- [ ] Reduce the register to useful source/unfinished-idea pointers, or merge those pointers if a
  separate file adds no value. Its final disposition is undecided; no source rows were discarded.
- [ ] Reconcile architecture/models/strategy and other concept/current-decision overlaps. In particular,
  `models.md` still describes pattern training as current, and `architecture.md` contains stale
  promotion status. Review remaining ADR-index implementation labels and draft/current boundaries.
- [ ] Split mixed native acceptance: the engine review's old "untested" list overlaps later observed
  owner/close/recovery checks. Preserve remaining drain/concurrency/restore/capacity cases separately;
  do not check off the whole group based on one observation.
- [ ] Recheck inbound references before moves, verify remaining roles and examples, and measure the
  actual final startup path. Full-file reading-volume reduction is not claimed by this first increment.
- [ ] Have the operator explain the main flow and approval boundaries, then give a final consolidation handoff.

Resume readiness through the [October 26 assessment](independent-engine-implementation-20261022.md#october-26-readiness-assessment--october-23)
and [strategy checklist](../strategy-alignment-implementation-checklist.md#paper-strategy-to-real-tfsa--current-checklist).
This learning/cleanup request does not start that implementation, install a build, probe a source,
change a safeguard, enable trading or create another monitor.

## Separate quote and fill investigation prompt

Copy the following into a separate TraderVI chat when ready. This is a proposed investigation scope,
not authority created by saving this document. It deliberately leaves price-policy choices open.

```text
Investigate the realism of Delphi Live Paper fills and the mismatch between Real's TMX-derived
decision reference and the broker ask in C:\src\TraderVI. Keep this separate from the ongoing
learning/documentation consolidation. Do not force Paper and Real to take identical trades.

Read AGENTS.md and task-relevant current status/roadmap/checklist sections. Recover S54 and:
- Docs/reviews/independent-engine-implementation-20261022.md, especially the October 26 assessment,
  Friday failures, final close and price-diagnostic limits.
- Docs/system-design.md, the Paper fill walkthrough and Real price-check explanation.
- Docs/reviews/open-questions.md, Monday data/price qualification and execution-realism research.
- Docs/design-rules.md and governing fill/order/approval ADRs, including 0053, 0074, 0092 and 0105.

Inspect code, tests and already available saved evidence. Trace Paper's saved decision, later quote,
Ask/Bid/Price selection, sizing and full simulated fill; trace Real's own reference selection,
broker preflight, market-order submission and reconciliation. Do not mistake a preflight ceiling
for an exchange-enforced limit, or a recent response for a verified recent market quote.

Classify possible causes: last-trade versus ask, unknown/stale provider data, mismatched instrument
or venue, elapsed request/processing time, genuine movement, or different account/confirmation state.
Friday's historical price pair is unavailable; do not reconstruct it from later observations or
assume the difference was small. Existing installed diagnostics were not exercised by a later
rejected buy that day. Preserve repaired Friday failures and remaining data/continuity gaps.

Compare the current guard with evidence-backed alternatives: improve the reference source/field,
reduce demonstrated latency, define a bounded adverse-price allowance, or a suitable limit-order
contract. Evaluate Paper assumptions separately. Do not adopt an arbitrary tolerance, remove the
guard, raise the reference to pass it, or copy Paper orders/holdings into Real. Explain cash/risk
sizing, quote expiry, non-fills, partial/unknown outcomes, protection and versioned-evidence effects.

Deliver a compact findings/options matrix, precise unknowns, any offline diagnostic replay that
can use existing non-sensitive evidence, and a concrete bounded quote-capture plan independent of
buy eligibility. Use reviewed fixed helpers where their scope suffices; broker quote reads require
a reviewed extension. Bring back the recommended behavior decision before implementing it.

This request authorizes read-only repository/existing-evidence investigation and documentation,
with focused offline checks that do not access databases, services or private production artifacts
outside the existing authorized scope. It does not authorize application behavior changes,
installation/restarts, SQL writes, new external probes, credential work, hold clearance, orders,
approval/enable or a monitor. Ask for the specific authority required by a concrete next step.
Preserve concurrent work and private evidence; commit reviewed documentation locally, never push.
```
