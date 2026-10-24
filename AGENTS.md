# TraderVI Agent Guide

## Purpose

TraderVI is a TSX-focused daily-stock advisory system for short-term momentum rotation with strict risk controls. Treat trading recommendations, database changes, model training, market-data collection, and model artifacts as consequential operations.

## Authoritative project references

- For orientation, start with [README.md](README.md). Read the current summary and task-relevant entries in `Docs/project-status.md` and `Docs/roadmap.md`; follow only the checklist sections needed for the task. Status owns dated facts, roadmap owns priority order, and checklists own acceptance criteria and dependency state. Do not read whole tracking files or archived reports by default, or infer priorities from old comments or commit dates.
- Use [the session-work register](Docs/session-work-register.md) only when recovering cross-chat work or an idea whose owner is unclear. Its source pointers are retrieval aids, not a parallel backlog. Read unreviewed relevant updates oldest to newest. Preserve open ideas, explicit deferrals and stable source IDs. Ask the user when conflicting evidence leaves consequential intent or scope unresolved; routine editorial placement does not require approval.
- Read `Docs/design-rules.md` before changing model selection, features, scoring, ranking lenses, gates, thresholds, or decision-engine behavior. Its exact rules and applicable accepted ADRs remain required even when the current summary is short.
- Use `Docs/system-design.md` for architecture and the relevant sections of `Docs/running.md` for workflows and side effects. Use `Docs/adr/README.md` and `Docs/adr/by-domain.md` to locate decisions; read those governing the affected behavior, including their explicit supersession links. There is no requirement to read every ADR for every task.
- The dated `*-history-20260922.md` documents preserve prior tracking and evidence, including superseded statements. Open them when tracing a fact, audit identifier or prior boundary; they are not startup reading or fresh operational authority. A later documentation edit is not later operational verification. Surface conflicting facts instead of silently choosing a convenient summary.

### Task reading routes

After the common startup above, use the route matching the work; follow additional governing links as needed.

| Work | Read next |
|---|---|
| App / nightly reliability | `Docs/running.md` sections for the affected host, `Docs/delphi-live-implementation-checklist.md` operational prerequisites, and the relevant calibration/Shadow checklist sections. Read design rules when changing evidence, valuation or policy behavior. |
| Strategy / gate analysis | `Docs/design-rules.md`, `Docs/calibration-implementation-checklist.md`, and applicable comparison contracts (ADRs 0051, 0053, 0062, 0067). Read current source/evidence definitions before drawing performance conclusions. |
| Real TFSA execution | `Docs/strategy-alignment-implementation-checklist.md`, `Docs/design-rules.md`, then the affected ADRs 0079–0083 and referenced broker/order contracts. Use the broker/setup sections of `Docs/running.md` for operational steps. |

## Safety and authorization

- Preserve existing modified and untracked files. Do not discard, overwrite, or reformat unrelated user work.
- Local commits are authorized under the commit cadence below. Never push, create a pull request, deploy a database, publish artifacts, train models, or access external market services unless the user explicitly requests that operation.
- Do not launch Hermes, Hercules, Delphi, TraderVI, Oracle, database backfills, or Sandbox probes merely to validate a code change. These programs can call external services, train models, or mutate SQL Server.
- Prefer read-only inspection, focused unit tests, and builds for routine validation. Ask before running workflows with database, model-artifact, trading-record, or external-service side effects.
- For task-relevant local database reads, use the operator-authorized [installed read-only helper](Operations/ReadOnlyChecks/README.md). Submit SQL through its ignored mailbox and run the exact fixed command separately; its narrow saved approval permits outside-sandbox execution. Verify the receipt's query hash. Do not request a new ad hoc sqlcmd approval or widen shell permissions when this path suffices. Failed permission checks require investigation; do not fall back to an unrestricted connection. Setup/upgrade and database writes still require their own authority.
- For task-relevant Wealthsimple account/holdings/pending-order/daily-performance checks, use the operator-authorized [installed broker-read helper](Operations/BrokerReadOnlyChecks/README.md) and its exact standalone command. It has a narrow saved outside-sandbox approval; do not generate another NativeRead/dotnet wrapper or ask for an ad hoc broker-read escalation when it suffices. Missing/expired authentication requires normal sign-in, never token refresh, credential writes or an unrestricted fallback. Other reads require a reviewed helper extension; broker mutation, SQL writes and execution retain separate authority.
- Never expose connection strings, credentials, API keys, private market/trading data, or model artifacts in output or commits.

## Build and test

- The solution targets .NET 10; confirm a compatible SDK with `dotnet --info` when validating a new environment.
- Run core tests with:
  `dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --verbosity minimal`
- For focused validation, build the affected `.csproj` directly with `dotnet build <project> --no-restore` after dependencies are restored.
- The complete solution contains the original-style `TraderDB/TraderDB.sqlproj`. Build `TraderVI.sln` with Visual Studio MSBuild plus SSDT/Data Build Tools; the `dotnet` CLI alone may not load its SQL targets.
- Report dependency-security warnings separately from compiler warnings, build failures, and test failures.
- Do not claim the complete solution is healthy if `TraderDB.sqlproj` was skipped or failed to load.

## Change expectations

- Keep changes focused and include tests for behavior changes when practical.
- Preserve the separation between deterministic rule-based indicators and trained ML models defined in `Docs/design-rules.md`.
- When adding a Delphi signal, gate, indicator, lens input, or data source, update `Core.Runtime.DelphiReportBuilder` and its construction in `Delphi/Program.cs` so diagnostic and human summaries remain complete.
- Keep database project definitions and runtime repositories synchronized. Apply schema changes only through individually reviewed manual migration scripts under `TraderDB/Migrations`; the DACPAC is a build/reference artifact and is not a deployment mechanism.
- Add new documentation files to the Visual Studio solution when repository conventions require it.

## Decisions, documentation, and learning

- For substantive design work, briefly restate the immediate problem, its parent problem, and the root goal before recommending a decision.
- Define technical or domain jargon concisely on first use in a substantive reply.
- Ask one question at a time, or a small tightly related group, and provide a recommended answer with reasoning.
- Before locking in a non-trivial decision, invite the user to rephrase the proposed solution as a learning checkpoint when appropriate.
- Record meaningful behavior, architecture, evidence or safety decisions and their rationale in `Docs/adr/`; preserve historical decisions with explicit supersession links. Routine troubleshooting, editorial changes and implementation of an existing decision do not automatically need another ADR.
- Record unresolved consequential choices in `Docs/reviews/open-questions.md`, with the evidence or decision needed to resolve them. Keep implementation tasks in their owning checklist. Add a concept explanation or learning card only when it helps understanding; neither is a mandatory companion to an ADR.

## Local commit cadence

- The user grants standing permission for local commits of reviewed project work. Do not ask again for each routine local commit. This does not authorize pushing or any operational action.
- Commit each coherent, validated increment, including its tests and documentation, before starting the next independent increment. During sustained editing, review the pending diff roughly every 45 minutes and commit a coherent checkpoint when ready; this is a checkpoint target, not a reason to commit broken or unreviewed work. Before handoff, commit any remaining ready work.
- At session start, inspect both staged and unstaged changes. Address accumulated older work in understandable groups as it is reviewed and validated; do not let known ready work accumulate indefinitely. Preserve unfinished, unrelated or concurrently changing work, and explain any changes intentionally left uncommitted and what is needed to commit them.
- Review the exact staged diff, run appropriate checks, and stage explicit files or hunks. Never use a blanket add/commit of a dirty workspace, include another session's staged work accidentally, or commit credentials, private account data, model artifacts, logs or generated build outputs. A checkpoint must be honest about any incomplete behavior or validation limits.
- Use descriptive commit messages and report the local commit IDs at handoff. Do not amend, squash, reset or otherwise rewrite existing history without an explicit request. If a commit is blocked by permissions or checks, report the blocker and preserve the work.

## Session completion and project tracking

- Update the authoritative owner of each changed fact at a coherent checkpoint. Change another document only when its own claim, priority, dependency, rule or navigation changes. Do not narrate a milestone in status, roadmap, checklist and register by default.
- Keep dated state and concise evidence links in `Docs/project-status.md`; priority order in `Docs/roadmap.md`; acceptance and dependency state in the relevant checklist. Paper-strategy promotion and real TFSA execution use `Docs/strategy-alignment-implementation-checklist.md`. Detailed incidents, experiments and reproducible validation belong in linked reviews when their detail is needed, not in every tracking file.
- Update a session-register source pointer only when reviewing that source or when it is needed to recover otherwise unrecorded work. Once an idea has an authoritative owner, link to it instead of maintaining another progress narrative. Never promote a proposal into a requirement or infer completion from an abandoned chat.
- Preserve completed work, open dependencies, explicit deferrals and important rejected alternatives. Re-read affected sections before writing in a shared checkout. Move history out of current summaries only after accounting for unique information and repairing references; keep critical evidence accessible without Git archaeology.
- Distinguish agreed, implemented, tested, installed and operationally verified. Check off only the action actually completed; split mixed items. A passing test or prepared migration does not prove installation, broker verification or enabled execution. Preserve evidence dates, scope and limitations; editing documentation does not renew operational verification or authority.
- Before handoff, check affected claims and links for agreement; correct stale claims instead of copying the latest narrative over them. Report the changed records, validation and remaining work. A question with no new finding or decision requires no documentation edits; a small fix may need only its owning state/acceptance record. Add an ADR, report, concept or flashcards only for the distinct purpose it serves.
- Save coherent reviewed checkpoints during long sessions under the commit cadence above. Documentation maintenance does not authorize database mutations, external services, live orders or pushes.

## Handoff

- Summarize changed files, validation performed, warnings or failures, and database/external operations intentionally not run.
- Call out assumptions and unresolved risks, especially for trading logic, thresholds, data quality, model freshness, schema changes, and execution behavior.
