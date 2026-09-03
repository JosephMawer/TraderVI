# ADR-0046: Guarded nightly operations runner

- **Status:** Accepted
- **Date:** 2026-09-02
- **Domains:** architecture, data-pipeline, decision-engine, risk-management

## Context

The immediate problem is to run Hermes, Delphi, and Athena without depending on an operator to remember
three commands. The parent problem is to restore a dependable daily advisory loop without allowing an
agent, an operating-system trigger, or a transient retry to create overlapping runs or duplicate official
evidence. The root goal is trustworthy, attributable daily advice whose data ingestion, publication, and
outcome evaluation remain reviewable after unattended execution.

These programs are not equivalent background utilities. Hermes calls external sources, mutates market
data, and creates the post-update database backup. Delphi writes operational recommendations and appends
immutable official calibration evidence. Athena idempotently writes matured outcomes, but uses exit code 2
when it records invalid evidence that requires attention. Hercules, WPF monitoring, migrations, Sandbox
probes, and broker operations are outside this nightly scope.

Codex can inspect and explain a durable run result, but the model should not be the component responsible
for deterministic process ordering or automatic retries.

## Decision

Use a repository-owned PowerShell runner as the deterministic execution boundary and Windows Task
Scheduler as its clock. Use a separate Codex heartbeat only as a read-only supervisor.

The accepted operating contract is:

1. At the start of every run, fingerprint all tracked and untracked non-ignored files in the current Git
   working tree, then build Hermes, Delphi, and Athena in Release with `--no-restore --no-incremental`.
   A build failure stops the pipeline before any operational program starts.
2. Fingerprint the source again after the build and refuse to start an operational stage if the source
   changed during preflight. Record SHA-256 aggregates for each build output, and verify the applicable
   output again immediately before each stage. This makes each run use the latest source while preventing
   a partially concurrent edit or later artifact replacement from changing that run underneath it.
3. Schedule the pipeline at 00:30 machine-local Toronto/Eastern time on Monday through Friday. This lets
   Delphi's recommendation date be the new calendar day while its market data remains the prior completed
   TSX session.
4. Run under the current user's interactive Windows token, with `WakeToRun` and `StartWhenAvailable`. Do not
   store a Windows password. The user must remain signed in.
5. Acquire an exclusive file lock before starting. Suppress a second completed run on the same local date
   unless an operator deliberately supplies `-Force` after reviewing the first run.
6. Execute the freshly built stages in the order Hermes, Delphi, Athena. A Hermes failure stops the pipeline.
   A Delphi failure is recorded but Athena may still mature earlier evidence. No task-level automatic retry
   is allowed.
7. Treat Hermes' non-zero failure count or explicit failure marker as failure even if its process returns
   zero. Treat Delphi's `audit=Invalid` as failure and `audit=Degraded` as attention. Treat Athena exit code
   2 as attention rather than ordinary success.
8. Write an atomic machine-readable `status.json` after every state transition plus one append-only log per
   attempt under the current user's local application-data directory.
9. Have Codex inspect only that status and its referenced log on weekday mornings. Stay quiet on a timely
   success; report missing, stale, long-running, failed, or attention states with evidence and a safe next
   step. Codex must not retry a program, modify SQL, call a market service, or repair data automatically.

Installing the task is recurring authorization for exactly these three configured program invocations and
their documented side effects. It is not authorization for model training, schema migration, database
deployment, WPF/paper-monitor hosting, Sandbox probes, Oracle, or any broker action.

## Alternatives considered

- **Three independent Windows tasks** — simpler triggers, but ordering and shared failure semantics would be
  implicit. Delphi could publish after an unprotected Hermes failure, and overlapping jobs would be harder
  to reason about.
- **Codex as both scheduler and executor** — convenient and good at diagnosis, but process ordering, locking,
  exact artifact identity, and retry semantics belong in deterministic code.
- **Deploy after every source edit** — depends on an editor or agent observing every change, can publish an
  intermediate non-building state, and makes the deployment hook another source of truth. Building under
  the run lock gives the schedule one auditable source-to-artifact boundary.
- **Pin artifacts until explicit reinstallation** — maximizes reproducibility, but conflicts with the operating
  requirement that reviewed source edits take effect on the next nightly run without a separate deployment step.
- **A continuously running Windows service** — suitable for future intraday monitoring, but unnecessary for
  three once-daily console programs and carries a larger installation and service-account surface.
- **Automatic Task Scheduler retries** — helpful for stateless jobs, but unsafe for official Delphi evidence
  until every stage has a stronger durable run-key/idempotency contract.

## Consequences

- Every scheduled execution uses the source present at run start, including intentional uncommitted edits,
  and records both the source fingerprint and the exact per-stage artifact aggregate.
- The workstation must be powered, Windows must be able to wake it, the user must remain signed in, and SQL
  Server plus external sources must be available. `StartWhenAvailable` handles a missed clock time only while
  those prerequisites can subsequently be met.
- Source or project changes that require dependency restore fail the unattended build until dependencies are
  restored separately. The scheduler never performs an implicit network restore.
- An edit made while the preflight build is running fails the pipeline before Hermes starts. The next normal
  schedule, or a deliberately reviewed `-Force` invocation, can then build the settled source.
- A dirty working tree can still cause Delphi to persist degraded official evidence because provenance is
  resolved at execution time. The runner elevates that marker to an attention state so supervision cannot
  report a silent clean success.
- The runner does not solve Delphi's broader same-day publication transaction boundary. Same-date suppression
  protects this task's ordinary repeat path, while ADR-0042's immutable evidence remains authoritative.
- A future Windows service may supersede only the hosting mechanism. The manifest, stage semantics, status
  contract, and read-only supervision boundary should remain.

## Review questions

1. Why does the nightly task run shortly after midnight instead of immediately after the TSX close?
2. How does the runner ensure current source is used without running a mixed build when source changes mid-build?
3. Which Hermes result prevents Delphi and Athena from starting, and why are automatic retries disabled?
4. What may the Codex supervisor do when it finds a failed run?
