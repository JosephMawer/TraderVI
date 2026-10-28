# October 28 minute-source study

## Installed collector observation — October 28 morning

The independent collector is installed. Do **not** launch the separate sampler below while it owns
intraday collection. Inspect `artifacts/collector-install-20261028/control/status.json` and
`subscriptions.json`, then submit [CollectorSourceSummary.sql](CollectorSourceSummary.sql) through
the installed read-only SQL helper. Verify its receipt SHA256 and restore the previous mailbox only
if it still contains your query. Keep captures private in `artifacts/minute-source-check-20261028`.

Compare every subscribed candidate, XIU and held symbols across captures. RY is unsampled unless it
actually appears in subscriptions. Missing candidate subscriptions after the first five-minute checkpoint
need attention. Separate reserved requests without receipts, failed receipts, first on-time arrivals,
late backfill, gaps and conflicts. A current lease or running process is not source qualification.
Do not infer no trades from missing bars, or count repeated/recovered bars as new on-time observations.
The same-chat five-minute follow-up ends at 16:05 Toronto with an evidence/uncertainty report. Actual
Real evaluation/protection readiness requires separate read-only execution diagnostics and user review.

The original standalone fallback below retains its original bounded authorization and requires proof
that it cannot overlap an active collector; an unhealthy collector is not permission to start it.

Read-only diagnostic for ADR-0111. This is separate from the production collector and never publishes
strategy inputs, writes SQL, authenticates to a broker or grants financial authority. It has a fixed
October 28, 2026 session window, three symbols, 90 sampling slots and 270 attempted-request ceiling.
State/raw responses are private under ignored `artifacts/minute-source-check-20261028`.

## Offline validation and preparation

Run `Invoke-October28Study.ps1 -SelfTest` before launch. Eight synthetic checks cover first arrivals,
completion at request start, gaps, duplicates, revisions, restart preservation and the sampling budget.
Running `-Run` outside October 28 09:25–16:05 Toronto must fail before any network request or file write.
The time guard was verified on October 27 evening. These checks do not prove a successful TMX response.

Use the installed read-only SQL helper to select one current Delphi candidate after the nightly
publication. Review a bounded query and verify its receipt hash; do not rerun the nightly pipeline.
Write `target-private.json` in the private study folder with a single `Symbol` field containing that
candidate's reviewed TMX symbol. XIU and RY are the fixed controls. Do not change the target after a run
starts: a checkpoint with a different target is rejected. Preserve all previous evidence.

## Launch on October 28

The same-chat 09:25 automation reads this guide and reviews the script. The following exact command
was submitted for a narrow sandbox exception on October 27; the invocation verified the date guard.
Actual unattended permission availability must still be checked by the tool, never bypassed.

```powershell
& 'C:\Users\joseph.mawer\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe' -NoProfile -File 'C:\src\TraderVI\Operations\TmxQualification\Invoke-October28Study.ps1' -Run
```

Use the command runner's asynchronous session for the long-running study. Do not keep an agent tool
blocked waiting all day. The script acquires an exclusive file handle, rejects concurrent runs, waits
in bounded sleeps and starts no network request until a scheduled completed endpoint. Calls are
sequential, bounded to twenty seconds and one MiB each, without retries. Request starts for each symbol
are at least sixty seconds apart. Crashes retain attempted slots and counters rather than replaying them.
If a slot is already missed, it is marked missed instead of generating a catch-up burst.

Endpoints are 09:31–10:00, 11:01–11:15, 13:01–13:15 and 15:31–16:00 Toronto; sampling starts ten seconds
after each endpoint and skips starts after the next minute. The last diagnostic may finish just after
the close, within the 16:05 study limit. This does not alter the trading entry cutoff. Computer, network
and app/process availability are necessary; a missing sample must be reported as missing.

## Review

Each private receipt records scheduled/request/receipt times, HTTP outcome, invalid/duplicate/forming
rows, completed coverage, missing endpoint list/streak and changed completed rows. The private state
preserves first completed receipt times and fingerprints even if later responses repeat or revise bars.
Use `Summarize-October28Study.ps1` for aggregate arrival counts; raw prices are omitted from its output.
Never promote a forming bar merely because receipt happened after its close. Empty/missing intervals
are not proof of no trades. Historical rows returned outside an active sampling window are separately
classified and cannot establish on-time coverage for that window.

At 16:05 summarize results and remove day-only monitoring. At intermediate reviews notify only material
source changes or failures. Do not select/adopt a new source or financially activate one-minute trading.
Three-symbol evidence cannot establish full-universe load capacity, venue completeness, provider terms
or every future session's fitness. All remain explicit qualification limits.
