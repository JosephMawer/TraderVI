# TraderVI Agent Guide

## Scope and intent

TraderVI is a TSX-focused daily-stock trading system for short-term momentum rotation with strict risk controls. Treat trading recommendations, database writes, model training, and market-data collection as consequential operations.

## Authoritative guidance

- Follow `.github/copilot-instructions.md` for domain rules, architecture, learning-oriented conversation style, and documentation expectations.
- Read `Docs/design-rules.md` before changing model selection, features, scoring, gates, thresholds, or decision-engine behavior.
- Use `Docs/system-design.md` for the broader architecture and `Docs/adr/` for prior decisions.
- Use `Docs/running.md` for the Hermes, Hercules, and Delphi workflows.

## Working safely

- Preserve existing modified and untracked files. Do not discard, overwrite, or reformat unrelated user work.
- Never commit, push, create a pull request, deploy a database, or publish artifacts unless the user explicitly requests it.
- Do not launch Hermes, Hercules, Delphi, TraderVI, database backfills, or probes merely to validate a code change. These programs can call external services, train models, or write to SQL Server.
- Prefer focused unit tests and builds for routine validation. Ask before running a workflow that can mutate application data or make external requests.
- Never expose connection strings, credentials, API keys, model artifacts, or private market/trading data in output or commits.

## Build and test

- The solution targets .NET 10; confirm a compatible SDK with `dotnet --info`.
- Run the core tests with:
  `dotnet test TraderVI.Core.Tests/TraderVI.Core.Tests.csproj --verbosity minimal`
- For a focused project, build its `.csproj` directly with `dotnet build <project> --no-restore` after dependencies are restored.
- The full solution includes the original-style `TraderDB/TraderDB.sqlproj`. Build the complete solution with Visual Studio MSBuild plus the Data Build Tools/SSDT workload; the `dotnet` CLI alone may not resolve its SSDT targets.
- Report warnings separately from failures. Do not claim the full solution is healthy when the SQL project was skipped or could not load.

## Change expectations

- Keep changes focused and include tests for behavior changes when practical.
- When adding a Delphi signal, gate, indicator, or data source, update `Core.Runtime.DelphiReportBuilder` and its wiring in `Delphi/Program.cs` as required by the repository instructions.
- Meaningful design decisions require the ADR/concept/flashcard updates described in `.github/copilot-instructions.md`; mechanical changes do not.
- Add new documentation files to the Visual Studio solution when the repository conventions require it.

## Handoff

- Summarize changed files, validation performed, warnings or failures, and any database/external operations that were intentionally not run.
- Call out assumptions and unresolved risks, especially for trading logic, thresholds, data quality, and schema changes.
