# Morning readiness report

Joseph requested a chat-owned HTML report at 09:15 America/Toronto every weekday. The
automation belongs to this Codex chat, not to the trading engine or Windows Task Scheduler.
It requires the local laptop and Codex to be running. Holidays still get a report, with
the installed calendar's session status stated; a weekday is not proof the market is open.

## Read and report, never repair automatically

Read AGENTS.md, current status/running instructions and relevant recent evidence first.
Discover current process owners and installed paths; never trust yesterday's PIDs. Use the
existing reviewed read-only capture at `artifacts/market-watch-20261029/Read-Status.ps1`
as its exact standalone command, subject to tool approval. Inspect every component result,
not only capture completion. The verified archive diagnostics RPC's Succeeded and Receipt.Payload
are required; raw shared archive JSON is not a substitute. If paths/contracts have changed,
report the gap and review the replacement rather than running an old installer or inventing a wrapper.

Use `Operations/ReadOnlyChecks/README.md` for bounded, reviewed SELECT diagnostics and verify
the receipt query hash. Respect the single-writer mailbox. Broker authentication/account checks,
if needed, use only the installed fixed helper documented in `Operations/BrokerReadOnlyChecks/README.md`.
Never refresh credentials or widen permissions. Missing/failed/stale reads are Unavailable.
No starts/restarts, backfills, strategy/financial changes, orders, database writes, or repair workflows.

Check engine, collector and dashboard presence/continuity; current execution mode and blocked
reasons; broker connection/auth; integrity/worker failures; native reconciliation/protection;
collector status/subscriptions; last shared publication; prior completed-session coverage and
available nightly readiness. Distinguish a fresh status read from fresh market data. Dashboard
process responsiveness does not prove displayed-data freshness. Historical notices are not new failures.

Before open, previous-session bars and a collector OutsideSession status are expected, but do
not certify today's source/risk readiness. Separate current actionable faults from checks awaiting
the regular session: exact opening/risk evidence, fresh qualifying stock/benchmark inputs and quotes.
If run late, state the actual execution time and coverage gap; never backdate a pre-open check.

## Output

Save private evidence under ignored `artifacts/morning-readiness/YYYY-MM-DD/`. Create a sanitized
`report.json` with Title, GeneratedAt (Toronto timestamp), Verdict, Summary, Actions (strings),
PendingAtOpen (strings), Limitations and Checks. Each check has Name, State, Detail and ObservedAt.
States: Verified, Waiting, Attention, Unavailable, Not applicable. No symbols, account identifiers,
prices, credentials or raw payloads in the report. Explain what each observation does and does not prove.
Then render a self-contained, responsive and printable HTML file:

```powershell
./Operations/MorningReadiness/Write-Report.ps1 -InputPath artifacts/morning-readiness/YYYY-MM-DD/report.json -OutputPath artifacts/morning-readiness/YYYY-MM-DD/report.html
```

The renderer only reads the supplied JSON, HTML-escapes all text, and writes HTML. It performs
no operational checks. Inspect the rendered result, then give Joseph a short chat summary,
any required action and a clickable absolute local link. Report delivery is requested every weekday,
including an honest unavailable report if checks cannot run; do not claim healthy from old evidence.

Offline renderer validation: `./Operations/MorningReadiness/Test-Report.ps1 -OutputDirectory artifacts/morning-readiness-tests`.
This tests escaping and rejects absent evidence or unknown states without any operational reads.

## Dashboard presentation

The display-only `ExecutionReadiness` projection reads the existing host status. Only the exact
fresh-input/no-entry message may show Ready — no qualifying signal. Known session/data waits are
distinct; stopped/paused/blocked states and their reasons remain visible. Unknown Running messages
show Checking execution. Missing, future-dated or more-than-two-minute-old status shows Unable to
verify. A five-second UI timer ages displayed status without performing any I/O. This is not a new
trading gate, source qualification or order permission. The original host checks remain authoritative.
