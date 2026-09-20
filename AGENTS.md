# TraderVI Agent Guide

## Purpose

TraderVI is a TSX-focused daily-stock advisory system for short-term momentum rotation with strict risk controls. Treat trading recommendations, database changes, model training, market-data collection, and model artifacts as consequential operations.

## Authoritative project references

- Read `Docs/project-status.md` first when orienting to the current implementation and operational state.
- Read `Docs/design-rules.md` before changing model selection, features, scoring, ranking lenses, gates, thresholds, or decision-engine behavior.
- Use `Docs/system-design.md` for architecture and `Docs/adr/` for prior decisions and their rationale.
- Use `Docs/running.md` for Hermes, Hercules, Delphi, TraderVI, Oracle, and Sandbox workflows.
- Use `Docs/roadmap.md` for Now/Next/Later priorities. Do not infer current priorities from old comments or commit dates.
- At the start of each work session, read project status, the relevant roadmap entries, and the linked implementation checklist before choosing the next increment.

## Safety and authorization

- Preserve existing modified and untracked files. Do not discard, overwrite, or reformat unrelated user work.
- Local commits are authorized under the commit cadence below. Never push, create a pull request, deploy a database, publish artifacts, train models, or access external market services unless the user explicitly requests that operation.
- Do not launch Hermes, Hercules, Delphi, TraderVI, Oracle, database backfills, or Sandbox probes merely to validate a code change. These programs can call external services, train models, or mutate SQL Server.
- Prefer read-only inspection, focused unit tests, and builds for routine validation. Ask before running workflows with database, model-artifact, trading-record, or external-service side effects.
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
- Record meaningful decisions in `Docs/adr/`, add a concept note when a new idea needs explanation, and add 1–3 review cards to `Docs/reviews/flashcards.md`.
- Record deferred decisions in `Docs/reviews/open-questions.md`. Mechanical changes do not require an ADR.

## Local commit cadence

- The user grants standing permission for local commits of reviewed project work. Do not ask again for each routine local commit. This does not authorize pushing or any operational action.
- Commit each coherent, validated increment, including its tests and documentation, before starting the next independent increment. During sustained editing, review the pending diff roughly every 45 minutes and commit a coherent checkpoint when ready; this is a checkpoint target, not a reason to commit broken or unreviewed work. Before handoff, commit any remaining ready work.
- At session start, inspect both staged and unstaged changes. Address accumulated older work in understandable groups as it is reviewed and validated; do not let known ready work accumulate indefinitely. Preserve unfinished, unrelated or concurrently changing work, and explain any changes intentionally left uncommitted and what is needed to commit them.
- Review the exact staged diff, run appropriate checks, and stage explicit files or hunks. Never use a blanket add/commit of a dirty workspace, include another session's staged work accidentally, or commit credentials, private account data, model artifacts, logs or generated build outputs. A checkpoint must be honest about any incomplete behavior or validation limits.
- Use descriptive commit messages and report the local commit IDs at handoff. Do not amend, squash, reset or otherwise rewrite existing history without an explicit request. If a commit is blocked by permissions or checks, report the blocker and preserve the work.

## Session completion and project tracking

- Keeping project tracking current is part of completing the work. Do not wait for the user to request a documentation update. After each meaningful milestone and before the final handoff, reconcile the affected entries in `Docs/roadmap.md`, `Docs/project-status.md`, and the relevant implementation checklist.
- Keep their roles distinct: the roadmap records priorities and next steps; project status records dated implementation, validation and operational evidence; the checklist records completed and remaining work in dependency order. For paper-strategy promotion and real TFSA execution, use `Docs/strategy-alignment-implementation-checklist.md`.
- Update completed work, partial progress, remaining dependencies, blockers and the immediate next step. Record accepted decisions and newly discovered follow-up work even when no code changed. Preserve unrelated priorities and edits; re-read affected sections before writing when other sessions may be working in the repository.
- Distinguish agreed, implemented, tested, installed and operationally verified. Check off only the action actually completed; split mixed items when necessary. A passing test or prepared migration does not prove installation, broker verification or enabled execution. Include the evidence date and a reference to validation where applicable.
- Correct affected stale summaries as well as adding new evidence. Keep historical events clearly dated; do not refresh an operational verification date merely because documentation was edited. Keep detailed rationale in ADRs and unresolved behavior in `Docs/reviews/open-questions.md`, with links from tracking documents.
- Before finishing, check that the three records agree on the affected work. In the final response, briefly identify the tracking documents updated, or state that no tracking changes were needed. For a question-only session with no new decision, finding or project change, do not create artificial progress or date-only edits.
- Save a checkpoint after meaningful progress during long sessions so an interruption leaves useful state on disk, following the local commit cadence above. Documentation maintenance does not authorize database mutations, external services, live orders or pushes.

## Handoff

- Summarize changed files, validation performed, warnings or failures, and database/external operations intentionally not run.
- Call out assumptions and unresolved risks, especially for trading logic, thresholds, data quality, model freshness, schema changes, and execution behavior.
