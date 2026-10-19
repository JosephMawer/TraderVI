# TraderVI session-work register

**Reconciled October 16, 2026.** Scope: September activity through this review, all three pinned
TraderVI chats, and the September continuation of an August chat. This register preserves unfinished
implementation and ideas so work can resume from disk without reopening a long conversation.

**October 17 research update:** R01/R05 include the expanded read-only gain-retention review across
all 15 saved paper accounts through October 16. The source inventory retains the October 16
consolidation's chat review points; other work was not requalified.
The operator subsequently clarified that the intended research unit is **individual stocks within
a portfolio** and accepted both gain references separately. The stock-level review is now linked
in R01; whole-account results remain background.

[Roadmap](roadmap.md) owns priorities; [project status](project-status.md) owns dated implementation
and operational evidence; [checklists](strategy-alignment-implementation-checklist.md) own completion
criteria; [open questions](reviews/open-questions.md) own unresolved design. This is their compact
cross-session index, not a second authority or permission to run anything.

## Resume from here

IDs are stable references, not a new implementation order. A source/test milestone does not imply
installation, continuous operation or trading readiness. Dates below describe recovered evidence;
the original consolidation performed no fresh operational qualification. The October 17 research
extension reads saved paper SQL records only, without a producer, broker or market-service run.

| ID / work | Reconciled state and next step | Owning record / source chats |
|---|---|---|
| **R01 — Preserve intraday gains; revisit soon** | **Installed/enabled October 18; important continuing research goal.** All 15 active paper portfolios (12 Shadow/three Delphi Live, including the ten comparison accounts) use prospective progressive versions. All 37 holdings have saved protection; native post-start checks preserve stronger stops, earned state, pending sells, financial facts and profiles, with original entry controls restored. Migrations 043–045 are installed after verified backup/copy; desktop hashes, responsive host and off-session wake are checked. Validation: 59 rollout tests, full Core 2,423 pass/eight skips, WPF/SSDT and 12 native guard checks pass; earlier 166 focused checks retain their scope. Next observe normal regular-session polling/touches when available. Quote investigation remains deferred; receipt age does not qualify upstream age. Keep morning/noon alternatives and whole-portfolio protection as background research. | [Accepted contract](adr/0090-progressive-individual-profit-trailing.md), [rollout evidence](project-status.md#reference-map-and-maintenance), [research](reviews/open-questions.md#intraday-gain-retention-revisit-soon); [S31](#s31), [S34](#s34); October 16–18 continuation in this chat. |
| **R02 — Visible AI reviewer, then paper/live AI** | Milestone 1 remains pending. Client, scheduling, risk core, SQL records and bounded desktop tests exist. October 9 v3 input omissions/counting passed; fresh v3 interpretation, qualified desktop installation, recurring successful reviews and basic display remain. Restore qualified inputs before the retained bounded-review step. Protection/actions, switching, evaluated learning and live qualification remain later milestones. | [AI ten steps](strategy-alignment-implementation-checklist.md#ai-momentum-delivery-sequence), ADR-0087; [S40](#s40), [S42](#s42), [S46](#s46). |
| **R03 — Dependable collection and valuation** | October 14 source-context, sparse-record and receipt-clock fixes are tested/installed, with bounded scheduled-cycle and current-mark recovery verified. The AI chat's October 9 “inspect provider contract next” is superseded by that work. Missing exact bars, canonical conflicts, historical gaps and continuous held-value/full-watchlist capacity remain open. Recovery of a Live mark does not qualify the separate AI risk reader. Latest nightly watcher reports success October 16; that is a recovered chat result, not an independent audit here. | [Latest evidence](project-status.md#latest-operational-evidence), [Live prerequisites](delphi-live-implementation-checklist.md#operational-prerequisites), [coverage question](reviews/open-questions.md#continuing-tmx-coverage-after-the-delphi-live-correction--october-14-2026); [S05](#s05), [S25](#s25), [S46](#s46). |
| **R04 — Real TFSA strategy management** | ADR-0092 first-day cash/native contracts accepted; all four native/five scoped checks/close expiry remain. Operator UI shows manual round trip complete and original saved-reference readback. Core mapping fix 71207c86 has 257 focused/2,614 full passes, nine skips. WPF diagnostics a78f0810 identify SQL 51413 during reconciliation (settings-lock wait). A WPF-only cycle/setup barrier is installed as 943f964b (15:30, PID 74844; hashes/one owner/no authority/checkpoint verified): finish active desktop paper cycles, defer new ones during setup/enabled-host work, retain SQL fences, bound wait/resume safely. Five barrier cases/199 focused checks/WPF build pass; Core unchanged. Native capture now passes reconciliation but rejects the assigned progressive successor. Exact source/assignment correction passes 217 focused/2,621 full offline tests (nine skips)/WPF build; bounded 15:43 read confirms one Normal/latest allowed assignment. Next install, capture, same-copy restart/packet/approval/enable. No agent financial action; host off, SQL rehearsals paused. | [Native steps](reviews/delphi-live-native-qualification-20261019.md), [ADR-0092](adr/0092-scoped-supervised-delphi-live-tfsa-session.md), [decision history](reviews/delphi-live-morning-decisions-20261019.md); [S51](#s51), [S32](#s32), [S33](#s33), [S35](#s35), [S38](#s38). |
| **R05 — Trustworthy account-value paths and daily closes** | October 17 saved-record extension reconciles all 15 cash books and 192 available Shadow closes. Exact 09:35/noon/close values support 74 of 177 invested/traded account-days; recorded Live marks and reconstructed Shadow values retain separate meanings. Receipt/execution chronology and complete peak paths remain limited. No persistence repair or new capture implemented; keep those follow-ups distinct from the happy-path research discussion. Original 446-checkpoint replay remains documented. | [Operational follow-through](strategy-alignment-implementation-checklist.md#operational-follow-through), [expanded review](reviews/paper-gain-patterns-20261017.md), [initial review](reviews/intraday-gain-evidence-20261017.md); [S31](#s31), [S34](#s34), [S25](#s25). |
| **R06 — Strategy evidence and opportunity research** | Continue clean matched Shadow/Live observations, gate comparisons, mature Athena outcomes and leadership coverage. Preserve the proposed **TSX gainers versus Delphi** study, including missed winners/losses avoided; it is analysis, not gate-retuning authority. September 4 shakedown and September 8 engineering overrides are not clean performance evidence. Older invalid outcomes remain separate from successful daily collection; retain source/identity/coverage boundaries. | [Calibration checklist](calibration-implementation-checklist.md), [fixed pairs](reviews/open-questions.md#fixed-gate-paper-comparison-follow-up), [Live research](reviews/open-questions.md#delphi-live--future-research-after-adr-0053); [S09](#s09), [S12](#s12), [S23](#s23), [S28](#s28). |
| **R07 — Operation independent of WPF** | Requested direction remains shared Core plus an independent host with a trading calendar, single active owner, durable recovery and controlled upgrades. Initial WPF hosting was accepted; a standalone pre-session host/task is still future work. Preserve “resume durable obligations, never reconstruct missed opportunities.” Existing leases/calendars/tests do not prove a standalone rollout. | [Host follow-through](strategy-alignment-implementation-checklist.md#operational-follow-through), [roadmap Later](roadmap.md#later--expand-system-capability); [S11](#s11), [S14](#s14), [S12](#s12). |
| **R08 — Bounded desktop memory/history** | September 24 direct typed reconstruction, shared reports and streaming validation were installed and updates resumed. Sustained memory/capacity remains unqualified. Bound display/history loads and avoid reconstructing unchanged revisions under explicit freshness/integrity checks; repeated reads and lifetime engine collections remain. | [Audit S4](strategy-alignment-implementation-checklist.md#remaining-audit-work); [S25](#s25), [S37](#s37). |
| **R09 — Recovery and storage** | Latest-two backup retention, compact storage and separately authorized one-time output cleanup/SQL shrink are completed milestones. Independent download/test restore and ongoing synchronization checks remain. Recurring captured-source/build/cache retention is undecided; backup retention does not answer it. Preserve evidence/model/source boundaries and existing recovery checkpoints. | [Backup questions](reviews/open-questions.md#backup-capacity-and-retention), [build retention](reviews/open-questions.md#nightly-build-retention-adr-0078-2026-09-14); [S05](#s05), [S25](#s25), [S36](#s36). |
| **R10 — Historical Practice extensions** | The exploratory **20-session, four-account trial was completed and deployed** September 8; do not treat it as still unrun. Current-universe/models limitations and missing bars remain explicit. Deferred extensions include checkpoint resume, isolated SQL/time/lease recovery and load/failure trials, Live replay, historical membership/quotes/corporate actions and walk-forward evaluation. Broader datasets remain separate work. | [Practice follow-ups](reviews/open-questions.md#historical-practice-environment--follow-up-milestones), [implementation](concepts/historical-practice-implementation.md); [S20](#s20), [S22](#s22). |
| **R11 — Maintainability and audit tail** | Dated RS alignment, leadership missingness, score-once, initial atomic publication/position entry, canonical-write reconciliation and initial CI are completed foundations. Preserve remaining F/S/D audit items: typed immutable Delphi stages, explicit Hermes completion, legacy model/activation paths, immutable outcome/strategy ownership, schema drift, broader host CI, modular boundaries, warnings/analyzers and dormant code. Calendar/short-session work is due **before December 24, 2026**. | [Remaining audit register](strategy-alignment-implementation-checklist.md#remaining-audit-work), [roadmap prerequisites](roadmap.md#reliability-and-correctness-prerequisites); [S02](#s02), [S10](#s10), [S17](#s17). |
| **R12 — Manual broker/settings follow-ups** | Manual buy quantity/selected-row controls, immediate sell-all UI and resolved external-order synchronization were implemented; old “wire the button/import history” prompts are superseded. Remaining native mutation/lifecycle qualification, working partial orders, verified limit sells, cancel/replace, cross-device coordination and settings/draft/editor extensions stay open. A limit-sell preview is not placement. | [Manual sell](reviews/open-questions.md#remaining-manual-sell-integration), [broker extensions](reviews/open-questions.md#external-broker-trade-synchronization-extensions), [settings](reviews/open-questions.md#central-settings--strategy-version-adoption); [S27](#s27), [S30](#s30), [S32](#s32), [S18](#s18). |
| **R13 — Limited broker-price fallback for AI** | Explicitly deferred/lower priority. TMX remains normal. Timestamped broker quote samples exist, but source semantics/adoption and behavior for held stocks with unavailable TMX prices remain unresolved. No system-wide switch to broker quotes was agreed. | [AI Step 3](strategy-alignment-implementation-checklist.md#ten-step-implementation-checklist), [AI scope](reviews/open-questions.md#ai-managed-portfolio-and-proposed-live-adoption--september-26-2026); [S42](#s42). |

## Preserve these unfinished ideas

An unanswered assistant proposal remains a proposal. Resuming a row begins with its unresolved
question, not an assumed design choice. R01 above has the operator's explicit revisit-soon priority.

| ID / idea | What survives and what is unresolved | Owner / source |
|---|---|---|
| **R14 — Liquidity / execution realism** | Explore spread, dollar volume and planned position participation before scaling or changing entry rules. “Measurement-only V1, with no blocking/resizing” was an assistant recommendation; the chat ended before acceptance. Preserve the distinction between zero commissions and uncertain executable prices; no new cost deduction or execution gate is agreed. | [Open research](reviews/open-questions.md#liquidity-and-execution-realism-research); [S13](#s13). |
| **R15 — News context for AI** | The operator asked whether a WSJ subscription could help the AI's decisions. Timestamped news/context and with-versus-without-news paper comparison were discussed. No ingestion feature, source/subscription choice or expenditure was approved. | [Open research](reviews/open-questions.md#news-context-for-the-ai-reviewer); [S43](#s43). |
| **R16 — Future short-horizon models** | Trade-profit, early-danger and daily-ranking models were proposed during model-fit discussion. The accepted exploration evolved into continuous five-minute opportunity research and Delphi Live V1. Preserve the earlier model ideas as unapproved future research; do not mistake them for a separate accepted training queue. Derived price-move event grouping and later model targets still need their own evidence/decisions. | [Live future research](reviews/open-questions.md#delphi-live--future-research-after-adr-0053), [opportunity evidence](concepts/delphi-live.md#opportunity-discovery-evidence); [S12](#s12). |
| **R17 — AI expansion and learning criteria** | AI Explorer is deferred to a separate future portfolio; first delivery remains AI Momentum. More portfolio choices are desired over time, with no design/order selected. Useful-lesson criteria are deliberately open until Step 6 requires them; this is not a reason to restart the framework interview. | [AI checklist](strategy-alignment-implementation-checklist.md#ai-momentum-delivery-sequence); [S40](#s40), [S42](#s42). |
| **R18 — Requirements-gathering personal skill** | The requested `gather-what-you-need` skill should elicit requirements, leave decisions with the user unless delegated, and play back shared examples/intent. Its chat was interrupted at installation approval. On October 16 the temporary draft still exists and the expected personal installation does not. Preserve the draft at `%TEMP%/codex-skill-draft-20260927/gather-what-you-need/SKILL.md`; installation/reconciliation with the existing guided-design skill is unfinished, with no replacement decision made here. | Supporting workflow; [S41](#s41). |
| **R19 — Sandbox/read-helper recovery** | October 18 SQL/broker v1 helpers remain installed with their earlier validation. October 19 separately approved history v2 is installed alongside v1: 66 offline/four rule checks pass; after normal reconnect its 10:38 native read succeeds (42 reads, 33 historical orders/matching holdings, zero pending). No SQL/journal/trade/refresh capability is linked and no execution qualification follows. Fixed SQL receipts at 10:33/10:57 verify query hashes. Reload saved rules/use exact commands; no broad shell permission. This does not requalify the October 9 Windows helper failure or its permanent fix. | [SQL procedure](../Operations/ReadOnlyChecks/README.md), [broker procedure](../Operations/BrokerReadOnlyChecks/README.md), [dated status](project-status.md), ADRs 0091/0093; [S47](#s47), [S05](#s05), [S50](#s50), [S51](#s51). |

## Dots alternative raised October 17

**R20 — Dots as the AI decision connection (unresolved).** Current-chat source reviewed through
the returned Dot answer and its historical validation. Official documentation supports ongoing
work and connected local tasks, but a TraderVI bridge, decision timing, structured results and
sustained allowance are unqualified. One historical `wait` passes schema/domain validation and 16
factual checks (15 choices/four eligible); earlier ten synthetic validator checks remain separate.
Pre-review missing-AI-attention must not become a circular wait condition. No selection/switching or
unattended operation is qualified. No API replacement or
recurring operation is approved. R02 remains the delivery work; [open questions](reviews/open-questions.md#ai-managed-portfolio-and-proposed-live-adoption--september-26-2026)
own the saved-input, non-executing feasibility check. [Setup/return validation](running.md#dots-one-saved-input-replay). No operating AI qualification changed.

## Four-screen UI redesign opened October 18

**R21 — Fresh, clear main layouts before detailed views.** This chat reviewed the existing UI source
and ADR-0068, then the operator selected clearer priorities/less clutter plus a fresher style, with
detailed drill-downs afterward. Workspace is selected; Trading's accepted arrangement is being refined
before moving to the other screens. Both Buy and Sell belong in the holding panel. A Trading-only refinement
is prepared and its arrangement is accepted. Performance first is now selected and integrated in the
full Trading concept. Today is now the default with an Overall switch. After the title/right-hand-badge
cleanup, the whole account heading block is removed and overview figures/spacing are compacted in the
single-account draft. Net deposits are now removed from the overview without changing financial records
or calculations. The operator selects shares in the panel only; the integrated concept now has Stock,
Position value, Open gain and Strategy status columns. A proposed panel refinement groups position
facts before strategy explanation with both Buy/Sell below; this grouping is now accepted. The operator
requests clear Hold/Sell reasons without repeated “saved strategy evaluation” wording and one selected
strategy dropdown at the portfolio placeholder for manual switching. The new draft uses decision/reason/
evidence/next trigger, separate missing-data/order states and local fictional strategy changes across
table/panel. The operator accepts the explanation and authorizes Trading-only implementation. They
confirm queued old-only exits when checks allow, even at a loss; shared holdings/protection remain and
required exits reconcile before buys. Trading WPF now contains the compact account overview, table/panel
and dropdown-to-existing-transition-review path. Other tabs/shared styles are unchanged. Actual strategy
assignment/execution remains unfinished; missing verified decisions remain unavailable rather than
fictional runtime defaults. The real-data follow-up accepts Wealthsimple's previous-close one-day view.
Its captured native simple-return contract is now wired through the existing connection/refresh owner,
with dated prior-day headings and no fallback to Overall or older complete points. WPF build and 233
focused tests pass; the initial layout has four offline synthetic renders. Current native daily-read
availability/operator comparison remain unverified; risk references and trading authority are unchanged;
full decision-source integration remains open. The operator then authorized a rebuild/restart and push:
normal Release build passes with existing warnings; no prior TraderVI process was running. The app
launched at 22:15 Toronto and PID 20036 is responsive at 22:16. Operator UI feedback/native Trading
binding verification remains pending; launch does not establish broker or execution readiness.
Current daily-read verification and proposed in-panel lookup remain open. Portfolio
choice is explicitly deferred. Existing broker-return source
fields differ from local-record complete-gain availability; no native broker read ran. The tentative
Delphi Live preference stays context only; one engine/three independent paper portfolios is explained
again, including the original book, comparison pair and current comparison-selection exclusion. No
exact choice, changed eligibility or wider family/AI contract is agreed; no fresh SQL/host check ran.
All four concept files' wrapper/script checks pass; browser local-file policy blocked visual inspection.
Trading-only WPF source is implemented/offline-tested and the rebuilt desktop is now launched under
explicit authority. Normal configured startup services run; no separate migration, training, broker
diagnostic, manual order or activation was invoked. Detailed views remain deferred.
Starting housekeeping pushed at `65956789`; personal photo attachments remain local/ignored.
Source review point: this October 18 chat through the accepted arrangement, gains/performance request,
explicit portfolio-choice deferral, Performance first/Today-default decisions, Overall switch, full account
heading removal, compact overview figures/spacing, net-deposit removal and the accepted panel-only shares
choice, accepted table/panel/explanations, Trading-only implementation authority, queued-exit clarification
and the validated first WPF implementation, followed by authorized Release rebuild/launch and commit/push,
then the accepted native one-day definition and implemented/tested real-data reader. The operator
explicitly defers rebuilding/restarting until concurrent strategy-switching work is ready; this daily
increment is not installed in the running app yet.
Operational trading activation is not inferred.
[Draft](concepts/trading-workspace-redesign-draft.md#october-18-visual-redesign--draft),
[checklist](strategy-alignment-implementation-checklist.md#four-screen-ui-redesign--october-18-design-track).
Earlier S19/S26 implementations remain closed milestones, not proof of this new redesign's completion.

## Closed or superseded handoffs

- Initial RS/leadership/score-once corrections, transaction/schema milestones and initial CI should
  not be reopened because an early chat still names them as “next.”
- Dirty working-tree state alone stopped degrading Delphi under ADR-0052; September 3 guidance to
  commit solely to clear that audit state is historical. Provenance remains required.
- The original 09:30-only publication rejection was superseded by ADR-0066 source adoption.
  This does not permit historical backfilled entries or erased coverage gaps.
- Initial comparison UI, recommendation-only promotion, manual order controls, initial history import,
  external resolved-order synchronization and the memory correction have later completion records.
  Their remaining qualification boundaries are R02–R12.
- Interrupted Live implementation and nightly-check starts explicitly continued in later chats;
  the duplicate opening AI idea continued into the AI framework. Keep one work item per continuing
  goal, with both source references. The interrupted personal-skill installation remains R18.

## Source coverage and incremental review

The inventory below is ordered by chat start, oldest first; overlapping updates were reconciled by
their actual dated events, with October 14 collection evidence taking precedence over October 9
handoffs. The August chat contributes its September continuation only. All three pinned chats are
included. Sidebar “updated” time is not treated as operational evidence or the date of a new turn.

**47 prior TraderVI/support chats accounted for**, plus this consolidation chat
[`01a144ea-5a1c-7b81-907f-9cc9c3fac069`](codex://threads/01a144ea-5a1c-7b81-907f-9cc9c3fac069).
Review used user requests, completed-turn summaries and targeted detail for interrupted/unclear work;
it did not reread every tool log. Credential lookup used metadata and the existing completion record
only. Eight unrelated same-folder chats and the separate general laptop-migration discussion were
excluded from TraderVI work. The archive listing contained no additional in-scope TraderVI chats.
No credentials, private financial amounts, full transcripts or model artifacts are copied here.

“Through” is the last reviewed result/request time in UTC, not an independent verification time.
For interrupted sources it is only the last reviewed request; interruption details were checked
separately. Dates in the work records use Toronto time.

| Source | Chat | Through (UTC) | Disposition |
|---|---|---|---|
| <a id="s01"></a>S01 | [Reorient TraderVI project](codex://threads/01a048a0-a7cb-7252-b76c-4d74e3687781) | 2026-09-02 01:40:24Z | September continuation: continuity guard completed; later evidence in R03/R06. |
| <a id="s02"></a>S02 | [Assess TraderVI code quality](codex://threads/01a05fc7-0be0-7eb2-ae70-4ca359cfb94b) | 2026-09-02 05:53:19.516Z | Pinned; RS/leadership/score-once fixes completed; R11 remains. |
| <a id="s03"></a>S03 | [Reorient TraderVI for today](codex://threads/01a06217-937b-71f0-86eb-45047af907c2) | 2026-09-02 14:50:34.568Z | Initial operating day and collector correction; later R03/R06. |
| <a id="s04"></a>S04 | [01a06368-8732-7481-8d6d-0cf4c177d28d](codex://threads/01a06368-8732-7481-8d6d-0cf4c177d28d) | 2026-09-02 18:37:18Z | Untitled phone recap; old uncommitted-work prompt superseded. |
| <a id="s05"></a>S05 | [Night Time Runs [Operations]](codex://threads/01a06528-3f47-7161-a319-16047e4a4202) | 2026-10-16 10:57:55.354Z | Pinned; R03/R09/R19; latest watcher reports success, without a new audit here. |
| <a id="s06"></a>S06 | [Reorient project priorities](codex://threads/01a06576-d1b2-7b73-8281-c72f1f58da5f) | 2026-09-03 13:07:31.711Z | Priorities/commit checkpoint; later R11 supersedes old next steps. |
| <a id="s07"></a>S07 | [Investigate nightly Attention run](codex://threads/01a0659e-fe21-73a2-9f81-09bd2c252956) | 2026-09-04 14:34:16.068Z | Historical invalid-evidence diagnosis; R06; dirty-tree policy later superseded. |
| <a id="s08"></a>S08 | [Inspect rejected intraday outcomes](codex://threads/01a06765-5973-7623-a364-959b2cf34f25) | 2026-09-05 00:14:21.869Z | Shadow design/monitoring and shakedown; R03/R06; replay fixes later completed. |
| <a id="s09"></a>S09 | [Investigate Delphi buy scarcity](codex://threads/01a067ef-a5df-7772-a63c-b943f1e25d75) | 2026-09-03 17:59:48.167Z | Breadth question and discretionary entry; R06; no threshold change adopted. |
| <a id="s10"></a>S10 | [Stabilize Real-trade workflows](codex://threads/01a069bc-af23-7f93-b94d-7f558565874b) | 2026-09-04 02:36:03.353Z | Transactions, schema reconciliation and initial CI completed; R11. |
| <a id="s11"></a>S11 | [Fix Shadow bar replay and restart](codex://threads/01a06ef4-a4dd-7740-b796-548165c9d5f6) | 2026-09-05 04:50:27.948Z | Replay/restart fixes and migration 021 completed; R07. |
| <a id="s12"></a>S12 | [Evaluate trading model fit](codex://threads/01a06ef7-12a0-7d82-ba05-2e624cbe49bd) | 2026-09-06 01:07:23.526Z | Model-fit discussion became Live design; R06/R16. |
| <a id="s13"></a>S13 | [Explore liquidity-aware trading](codex://threads/01a06fe2-42dd-7fb1-86fc-014cf0dca9a0) | 2026-09-05 04:46:26.972Z | Unanswered measurement-first proposal; R14. |
| <a id="s14"></a>S14 | [Extract Shadow reliability core](codex://threads/01a06fe9-ee3a-75e0-84cf-7fb9bd8bb083) | 2026-09-05 04:59:50.520Z | Requested independent Shadow hosting; R07; no host installed here. |
| <a id="s15"></a>S15 | [Implement Delphi Live V1 design](codex://threads/01a07448-d8d2-7052-9b28-f509eb2539bf) | 2026-09-06 01:15:36.914Z | Interrupted; explicitly continued by the next source. |
| <a id="s16"></a>S16 | [Implement Delphi Live V1 design](codex://threads/01a07474-43b4-7472-ab62-834326b68e0b) | 2026-09-06 18:30:54.859Z | Continued Live implementation/audit and promotion direction; R03/R04/R11. |
| <a id="s17"></a>S17 | [Implement audit stabilization phases](codex://threads/01a077fd-7185-7c80-8d76-c609aaa75e09) | 2026-09-07 02:52:52.948Z | Corrected model/input cutover completed; remaining R11. |
| <a id="s18"></a>S18 | [Add Delphi strategy settings](codex://threads/01a079cd-874b-7c70-9995-a7091edfb4e1) | 2026-09-07 18:15:23.129Z | Central settings implemented; deferred settings in R12. |
| <a id="s19"></a>S19 | [Improve portfolios UI UX](codex://threads/01a07e06-a56c-7bf0-980e-562b0c81da90) | 2026-09-07 23:24:14.701Z | Initial comparison UI completed; promotion gap later closed. |
| <a id="s20"></a>S20 | [Explain Shadow, Delphi Live, Paper](codex://threads/01a07e18-538a-7721-a446-6091d7822a4f) | 2026-09-08 02:48:25.685Z | Domain explanation and handoff prompts continued in R04/R10/R11. |
| <a id="s21"></a>S21 | [Implement approved live promotion](codex://threads/01a07e8f-b7c3-72e2-b002-0b1f96b6b2ab) | 2026-09-08 03:13:40.519Z | Recommendation promotion/migration 029 completed; execution R04. |
| <a id="s22"></a>S22 | [Design historical simulation harness](codex://threads/01a07eeb-e038-77e0-857e-ccdf08054a83) | 2026-09-08 06:13:06.699Z | Exploratory 20-session trial completed; extensions R10. |
| <a id="s23"></a>S23 | [Prepare TraderVI monitoring](codex://threads/01a08138-c268-7dc2-87c6-6353a12c418b) | 2026-09-09 03:25:18.147Z | Engineering comparison/attribution and gate questions; R06. |
| <a id="s24"></a>S24 | [can you make sure the nightly runs ran and that hermes, del…](codex://threads/01a0863b-04aa-7c53-991b-5aa8b0e2e659) | 2026-09-09 12:56:55.364Z | Interrupted initial request; continued in the next source. |
| <a id="s25"></a>S25 | [Verify nightly trading runs](codex://threads/01a08668-1b72-7310-a01c-b199e65b996a) | 2026-10-14 15:18:39.883Z | R03/R05/R08/R09; October 14 fixes supersede October 9 next step. |
| <a id="s26"></a>S26 | [Redesign Trading app UI](codex://threads/01a08781-2574-7bc2-bdf8-3494be67f469) | 2026-09-10 01:57:01.327Z | Trading/comparison layout and daily-return UI completed; R05. |
| <a id="s27"></a>S27 | [Find Wealthsimple client relics](codex://threads/01a088b6-78ec-7f81-933b-8f52366f2303) | 2026-09-10 18:15:22.969Z | Authentication/account/manual-buy foundation; remaining R04/R12. |
| <a id="s28"></a>S28 | [Check overnight market workflows](codex://threads/01a08b62-0b62-7d22-9a6d-5b2c37ba75ed) | 2026-09-23 01:45:20.411Z | TSX-gainers idea R06; documentation cleanup completed. |
| <a id="s29"></a>S29 | [Fix GTE trade entry error](codex://threads/01a08c6f-de4a-7cb1-ab07-54f055073b07) | 2026-09-10 18:02:47.330Z | Tracked-entry insert-order fix completed; no pending restart from that handoff. |
| <a id="s30"></a>S30 | [Wire WPF market buy UI](codex://threads/01a08c9a-a6f7-7940-8b25-df6a19c2fc2e) | 2026-09-10 21:19:31.905Z | Shares ticket and selected-row buy controls completed; R12. |
| <a id="s31"></a>S31 | [Assess TraderVI priorities](codex://threads/01a0908e-613a-7713-9de1-dd3851a8a71c) | 2026-09-11 20:54:46.059Z | Morning-gain idea R01; monitoring limits R03/R05. |
| <a id="s32"></a>S32 | [Implement market sell from HAR](codex://threads/01a091b5-d872-7180-9d01-78137344ff01) | 2026-09-17 02:09:59.582Z | Sell UI/history import completed; remaining R04/R12. |
| <a id="s33"></a>S33 | [Complete Wealthsimple TFSA trading](codex://threads/01a0ad21-15d1-72b2-a160-ad231418f5e2) | 2026-09-23 01:44:31.200Z | Cash-flow percentage preservation implemented; native R04. |
| <a id="s34"></a>S34 | [Review breakout 3 portfolio history](codex://threads/01a0b4ed-4c70-7ab3-96ff-4b5f52b5fc8a) | 2026-09-18 14:47:55.071Z | Missing closes and value-path evidence R05; supports R01 without proving a rule. |
| <a id="s35"></a>S35 | [Prepare supervised strategy session](codex://threads/01a0cbf1-beed-73b3-909c-4d68b72c3ebe) | 2026-09-25 21:08:01.173Z | Integrated controls/recovery rehearsal completed; launch remains NO-GO; R04. |
| <a id="s36"></a>S36 | [Find laptop storage usage](codex://threads/01a0d0ed-9e7b-7563-b217-cffa199e2c9a) | 2026-09-24 04:00:09.308Z | TraderVI storage cleanup/shrink completed; R09; unrelated personal cleanup excluded. |
| <a id="s37"></a>S37 | [Investigate WPF memory issue](codex://threads/01a0d386-cb18-74d3-92ec-f860bfa84770) | 2026-09-24 13:35:07.206Z | Memory correction installed; remaining R08. |
| <a id="s38"></a>S38 | [Verify GTE.TO sale record](codex://threads/01a0d3b1-0e2a-7df3-b031-f7f37bb10d51) | 2026-09-24 15:10:56.038Z | Resolved external-order synchronization installed/verified; remaining R04/R12. |
| <a id="s39"></a>S39 | [Explore AI-controlled portfolio](codex://threads/01a0e03e-c9f8-7471-a04e-af079dd8e8b5) | 2026-09-27 00:26:34.305Z | Duplicate opening AI idea continued in the next source. |
| <a id="s40"></a>S40 | [Explore AI portfolio framework](codex://threads/01a0e040-edcf-7140-8b11-93de85e0019f) | 2026-09-28 00:19:06.012Z | AI framework/risk core and handoff; R02/R04/R17. |
| <a id="s41"></a>S41 | [Create gather-what-you-need skill](codex://threads/01a0e087-443c-7e21-959c-57dbe868f31c) | 2026-09-27 01:44:40.738Z | Interrupted personal-skill installation; R18. |
| <a id="s42"></a>S42 | [Identify next AI Momentum decision](codex://threads/01a0e561-6b49-7941-a8ce-10232f0043c9) | 2026-10-02 13:01:41.277Z | Data/catalogue/reviewer integration and October 1 review; R02/R13/R17. |
| <a id="s43"></a>S43 | [Assess WSJ for TraderVI decisions](codex://threads/01a0eed7-1ef3-70f2-8eba-20efb0ff4b6c) | 2026-09-29 20:27:16.829Z | News/subscription discussion only; R15. |
| <a id="s44"></a>S44 | [Visualize TraderVI architecture](codex://threads/01a0f510-b0b3-7740-8c36-8d84022846a7) | 2026-10-01 01:32:27.365Z | Pinned; architecture visualization delivered; no new operating authority. |
| <a id="s45"></a>S45 | [Find the existing OpenAI key](codex://threads/01a0f56f-04e2-7c80-8123-684280e035d2) | 2026-10-01 03:11:49Z | Completion checked through metadata/checklist only; credential contents excluded. |
| <a id="s46"></a>S46 | [Continue AI Momentum MVP](codex://threads/01a0fcc2-7b2f-70f3-9e83-777575beb770) | 2026-10-09 18:53:49.795Z | v3 interpretation/desktop follow-up R02; provider investigation continued in S25. |
| <a id="s47"></a>S47 | [Explain sandbox failure message](codex://threads/01a12141-99c8-7b31-9d21-9935c3556672) | 2026-10-09 15:24:25.726Z | Sandbox helper diagnosis; recovery not verified in that chat; R19. |
| <a id="s50"></a>S50 | Current sandbox diagnostic-approval setup conversation | October 18, through the separately requested broker helper's installed native check and handoff | R19; SQL v2 has 33 offline/12 native checks; broker helper has 54 offline/four rule checks and a successful seven-request read. Both fixed rules are saved. Existing chats must reload/use them; old mutable commands remain outside the new approvals. No cross-chat message or broker mutation was authorized here. |
| <a id="s51"></a>S51 | Current Delphi Live TFSA preparation conversation | October 18–19, through original-reference readback, mapping fix, staged diagnostics and SQL 51413/local scheduling fix 943f964b and verified 15:30 startup | R04/R19; manual trades and saved-reference readback reviewed on operator screenshots. Mapping fix 71207c86: 257 focused/2,614 full, nine skips. Diagnostic a78f0810 running at 15:15 identifies settings-lock wait timeout (51413), not a saved checkpoint. WPF-only cycle/setup coordination 943f964b installed/verified at 15:30 as PID 74844, no authority/checkpoint; five barrier cases/199 focused checks/build pass, Core unchanged. Wait for current desktop paper work, defer new cycles during setup/enabled-host operation, retain SQL fences and bounded cancellation/recovery. Actual SQL owner uninspected. Next install the exact progressive-successor review correction (217 focused/2,621 full offline passes, nine skips/WPF build; bounded 15:43 assignment verified), capture, same-copy restart/native packet/approval/enable; no financial reset or agent financial action. Execution off; SQL rehearsals paused. |

## Maintenance contract

1. At the start of related work, scan the relevant register rows alongside the authoritative records.
   Resume from their owning checklist/question rather than replaying a whole chat.
2. At each meaningful milestone and handoff, update affected rows and their source “Through” times.
   Include newly raised unfinished ideas even if no code changed. Keep IDs stable; append new IDs.
3. Review unprocessed chats oldest first, then reconcile overlapping events oldest to newest.
   Read only newer turns after the recorded point, with older passages fetched only to resolve context.
   Never infer completion from silence, a thread title, recency, a commit date or a passing build.
4. If intent, scope, placement or conflicting evidence is unclear, stop and ask the operator.
   Record explicitly deferred choices as open; do not decide them in order to close a row.
5. Keep one concise row per continuing goal. Link superseded/continued chats to its surviving row.
   Preserve a short closed disposition and source pointer when work completes; do not erase the idea.
6. Keep detailed dated facts in project status, priorities in roadmap, acceptance criteria in the
   relevant checklist and unresolved behavior in open questions/ADRs. Reconcile the links together;
   do not copy entire milestone narratives into this register.
7. Update the file once per coherent review/checkpoint. Cache only the small working summaries
   needed during a review; keep private captures and generated review files out of commits.
   No automation, operational approval, market access or external messaging is created by this file.
