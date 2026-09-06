# ADR-0052: Working-tree state is provenance, not evidence quality

- **Status:** Accepted
- **Date:** 2026-09-04
- **Domains:** architecture, data-pipeline, decision-engine
- **Related:** ADR-0020, ADR-0042, ADR-0046

## Context

The immediate problem is that Delphi marked an otherwise complete official run `Degraded` solely because
the source tree contained uncommitted work. System Shadow correctly accepts only `Valid` daily runs, so a
source-control hygiene marker prevented it from using a complete set of current recommendations.

The parent problem is that provenance and evidence quality are different questions. Provenance describes
where a run came from; audit state says whether the captured trading evidence is usable. The root goal is
to let calibration and safe paper automation use complete causal evidence without hiding how that evidence
was produced.

## Decision

Always persist the code commit, code-version source, and working-tree state. Treat `Clean`, `Dirty`, and
`Unknown` as visible provenance metadata only; working-tree state by itself does not change a Delphi run's
audit state.

An official run remains `Invalid` when its code commit is unavailable or its loaded model provenance is
incomplete. `Degraded` remains available for an actual evidence-quality limitation, not for the mere
presence of local source edits.

Apply the rule prospectively in one deterministic audit-policy function. Correct the 2026-09-04 official
run only through an exact, guarded manual migration that preserves `WorkingTreeState = Dirty`. Reset the
associated empty Shadow generation in that same reviewed operation so a same-session restart can freeze
the newly valid run; refuse the reset if candidates, orders, or positions exist.

## Alternatives considered

- **Keep dirty runs degraded.** Rejected because it conflates repository cleanliness with the quality of
  the captured market, model, candidate, and lens evidence.
- **Require a clean checkout before every official run.** Rejected because the nightly system deliberately
  builds the settled source present at run start, including intentional local work.
- **Discard working-tree state.** Rejected because operators still need to know that the commit does not by
  itself describe every source change used by the run.

## Consequences

- System Shadow and other `Valid`-only consumers may use a complete official run created from a dirty tree.
- Operators can still distinguish clean, dirty, and unknown source provenance in the stored row.
- A dirty run is not fully reconstructible from `CodeCommit` alone. The nightly source fingerprint and
  artifact hashes improve operational traceability, but the audit state must not be read as “clean source.”
- Missing commit or model identity still blocks official use as `Invalid`.
- Any future run-level `Degraded` state must identify an evidence limitation rather than source-control
  cleanliness.

## Review questions

1. Why does a dirty working tree no longer make a Delphi run degraded?
2. Which provenance failures still make an official run invalid?
3. What important limitation remains when a valid run records `WorkingTreeState = Dirty`?

