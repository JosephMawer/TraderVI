# TraderVI Agent Guide

## Purpose

TraderVI is a TSX-focused daily-stock advisory system for short-term momentum rotation with strict risk controls. Treat trading recommendations, database changes, model training, market-data collection, and model artifacts as consequential operations.

## Authoritative project references

- Read `Docs/project-status.md` first when orienting to the current implementation and operational state.
- Read `Docs/design-rules.md` before changing model selection, features, scoring, ranking lenses, gates, thresholds, or decision-engine behavior.
- Use `Docs/system-design.md` for architecture and `Docs/adr/` for prior decisions and their rationale.
- Use `Docs/running.md` for Hermes, Hercules, Delphi, TraderVI, Oracle, and Sandbox workflows.
- Use `Docs/roadmap.md` for Now/Next/Later priorities. Do not infer current priorities from old comments or commit dates.

## Safety and authorization

- Preserve existing modified and untracked files. Do not discard, overwrite, or reformat unrelated user work.
- Never commit, push, create a pull request, deploy a database, publish artifacts, train models, or access external market services unless the user explicitly requests that operation.
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
- Keep database project definitions and runtime repositories synchronized. Generate and review deployment plans before applying schema changes; never assume a successful DACPAC build deployed the database.
- Add new documentation files to the Visual Studio solution when repository conventions require it.

## Decisions, documentation, and learning

- For substantive design work, briefly restate the immediate problem, its parent problem, and the root goal before recommending a decision.
- Define technical or domain jargon concisely on first use in a substantive reply.
- Ask one question at a time, or a small tightly related group, and provide a recommended answer with reasoning.
- Before locking in a non-trivial decision, invite the user to rephrase the proposed solution as a learning checkpoint when appropriate.
- Record meaningful decisions in `Docs/adr/`, add a concept note when a new idea needs explanation, and add 1–3 review cards to `Docs/reviews/flashcards.md`.
- Record deferred decisions in `Docs/reviews/open-questions.md`. Mechanical changes do not require an ADR.

## Handoff

- Summarize changed files, validation performed, warnings or failures, and database/external operations intentionally not run.
- Call out assumptions and unresolved risks, especially for trading logic, thresholds, data quality, model freshness, schema changes, and execution behavior.
