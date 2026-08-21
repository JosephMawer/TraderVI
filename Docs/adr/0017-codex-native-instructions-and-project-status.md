# ADR-0017 — Codex-native instructions and explicit project-status structure

- **Status:** Accepted
- **Date:** 2026-08-19
- **Tags:** architecture
- **Supersedes:** repository reliance on `.github/copilot-instructions.md` and `.github/instructions/*.instructions.md`

## Context

TraderVI was originally developed with GitHub Copilot-specific instruction files. After moving
daily development to the ChatGPT desktop app and Codex, the repository had three problems:

1. Codex discovered the root `AGENTS.md`, but the root file delegated substantial authority to a
   Copilot-named file rather than being self-contained.
2. The path-scoped Sandbox instructions were Copilot-specific and were not part of Codex's native
   root-to-working-directory `AGENTS.md` instruction chain.
3. Volatile implementation status and priorities were spread across prompts, design documents,
   TODO text, comments, and Git history, causing contradictions after a development pause.

## Decision

Use native, layered `AGENTS.md` files for durable agent behavior:

- Root `AGENTS.md` owns repository-wide safety, validation, change, documentation, and learning rules.
- `Sandbox/AGENTS.md` owns probe-specific conventions and side-effect controls.
- Retire the Copilot-specific instruction files after migrating their durable content.

Separate changing project facts from agent instructions:

- `Docs/project-status.md` is the dated operational and implementation snapshot.
- `Docs/roadmap.md` is the authoritative Now/Next/Later priority list.
- `Docs/design-rules.md` remains authoritative for decision behavior.
- `Docs/system-design.md` remains the architecture reference.
- `Docs/running.md` remains the side-effect-aware operational runbook.
- `TODO.txt` becomes a compatibility pointer rather than a second backlog.

## Alternatives considered

- **Copy all Copilot instructions into root `AGENTS.md`.** Rejected because Sandbox details would
  burden every task and volatile strategy facts would quickly make the prompt stale.
- **Keep Copilot files and reference them from root `AGENTS.md`.** Rejected because it preserves
  duplicate authorities and leaves path-specific instructions outside Codex's native layering.
- **Configure Copilot filenames as Codex fallback instruction files.** Rejected because native
  `AGENTS.md` is clearer for collaborators and does not require personal Codex configuration.
- **Use only code and Git history for orientation.** Rejected because reconstructing operational
  state after every pause is slow and error-prone.

## Consequences

**Easier:**

- Codex receives the correct repository and Sandbox rules automatically.
- A returning developer can distinguish what exists, what runs, and what is next.
- Strategy facts can change without bloating or destabilizing the agent prompt.

**Harder:**

- `project-status.md`, `roadmap.md`, and ADR indexes must be maintained deliberately.
- Contributors using another coding agent must either understand `AGENTS.md` or configure their
  tool to read it.

**Would tell us this was wrong:**

- The same guidance begins to diverge across nested instruction files.
- Project-status documents repeatedly become stale because no milestone workflow updates them.
- A required domain rule is missed because it was removed from instructions without being placed
  in an authoritative design document.

## Review questions

1. Why are volatile model and roadmap facts kept out of root `AGENTS.md`?
2. Why does Sandbox use a nested `AGENTS.md` instead of adding its probe contract to the root file?
3. Which document answers "what exists now," and which answers "what do we do next"?
