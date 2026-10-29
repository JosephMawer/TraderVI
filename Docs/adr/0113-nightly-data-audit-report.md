# ADR-0113: Nightly data-quality audit and report

- **Status:** Accepted
- **Date:** 2026-10-28
- **Domains:** architecture, data-pipeline, user-interface
- **Supersedes:** the stage list/authorization in [ADR-0046](0046-guarded-nightly-operations-runner.md) and the Data Audit desktop surface in [ADR-0033](0033-tabbed-desktop-shell-and-shared-workflows.md)
- **Related:** [ADR-0078](0078-isolated-latest-source-nightly-builds.md), [ADR-0110](0110-nightly-isolated-symbol-warnings.md)

## Decision and rationale

The immediate problem is that useful daily-data checks require a manual dashboard action. The parent
problem is making data problems visible after collection; the root goal is dependable, reviewable
nightly preparation. The user authorizes moving DataAudit into the recurring nightly run after Hermes,
including its findings in the report, and removing the desktop menu while retaining the CLI.

Run **Hermes → DataAudit → Delphi → Athena**. DataAudit starts only after Hermes completes its existing
collection and backup requirements, including the accepted isolated-source Attention path. A fatal
Hermes failure still stops later stages. Capture, locked dependency restore, separate builds, artifact
checks, single ownership, same-date suppression and no automatic operational retry remain unchanged.

DataAudit remains a read-only local SQL scan with unchanged checks and thresholds. It makes no external
requests or repairs. Its CLI retains normal human-readable output and adds `--json` for the complete
version-1 report. Exit 0 is clean; exits 1/2 are completed findings and produce nightly **Attention**.
Exit 3, timeout, malformed output, contradictory counts/exit code or report-storage failure produce
nightly **Failed**. Delphi and Athena still run after an audit failure: this diagnostic is not a new
publication or trading gate. Their independent input checks and failure contracts remain authoritative.

## Reporting and maintenance

The private nightly status contains the complete report, options, market date and audit timestamps;
the per-run log retains the JSON output. A separate atomic `data-audit-last-completed.json` preserves
the last valid completed scan across failed nights. Failed/incomplete scans never advance it.

Compare findings by code and symbol under matching options and non-regressing, known market dates.
Treat lagging/stale stock codes as one freshness issue, so crossing from warning to error is an
escalation. Report new findings, severity escalations and findings no longer reported. Unchanged
findings stay visible without being labelled new. The first scan establishes a baseline. An unreadable
baseline or incompatible options/dates explicitly makes comparison unavailable. Text/count drift within
one severity is not labelled escalation. Changes to check semantics require review of the report schema
and comparison compatibility; a review date does not prove clean data.

The audit does not qualify intraday collection, broker execution, model quality or financial authority.
Remove its WPF view/view model and navigation entry; preserve Core checks, tests and the console project.
The installed task reads repository scripts directly, so this authorized addition applies at its next
scheduled capture without reinstalling the task. A manual operational run is not part of validation.

## Review questions

1. Why are completed audit findings different from an audit that could not run?
2. Which checks still decide whether Delphi may publish or trading may proceed?
3. When can two audit reports legitimately be compared?
