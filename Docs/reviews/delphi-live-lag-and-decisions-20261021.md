# Remaining Delphi Live lag and decision evidence — October 21

The operator requests remaining processing lag and exact confirmed-signal-to-order diagnostics fixed
now, in preparation for tomorrow, preserving risk controls. This is implementation authority, not a
renewal of today's expired trading permission. Diagnostics and installation/operational acceptance
remain separate milestones; existing October21-only approval paths cannot authorize October22.

## Evidence and implementation

At15:54, the four most recent captured paper engine cycles take286.11/284.31/284.75/297.41seconds.
Their actions total551.97seconds; snapshot pack164calls/320.08seconds, unpack1,236calls/468.50seconds,
read1,402calls/360.31seconds and verification163calls/167.11seconds are nested timing samples, not
additive wall-clock attribution. Collection/evaluation take only seconds. Paper-work admission still
delays real execution; the seven-minute freshness condition is unchanged.

[ADR-0099](../adr/0099-exact-report-sharing-in-live-writes.md) removes expanded report buffers from
typed writes and repeated escaping from reopened historical quote/action comparisons. All stored
hashes, original report text, collection order, full fingerprint verification, SQL readback and
financial/lease guards remain. All44focused codec/action/transition tests pass; focused synthetic packing measurement:
1,000quotes/16KiB repeated report, expanded256ms/192,609,624allocated bytes versus direct159ms/
12,545,280bytes. This is not native session capacity. Reopened history regressions reject changed reports, quote fields, numeric representations and completed action fields.
Artifacts: `artifacts/lag-and-decisions-20261021-{snapshot-tests,packing-benchmark,storage-tests}.log`.
The first isolated no-restore attempt lacks assets; sandbox restore cannot reach NuGet and is
cancelled. The separately approved normal restore succeeds outside the sandbox. No dependency
version change is intended. Existing compiler warnings remain distinct from security advisories.

Next implement bounded durable per-evaluation decision and quote-attempt evidence, then complete
full offline tests/builds and review before any standard Release replacement. No database mutation,
market call, app launch/restart, approval, enable or broker order was performed by this work.

## Final October 21 watch

Final bounded read16:06:29: latest realStrategyEvaluated15:59:21/revision259,23real evaluations after
the14:06:29updated-build review. Actual15:50bar has31mature/zero confirmed; source inputs reach16:00,
source ledger advances16:02:53. Zero automatic intents, transmissions, fills, managed/protected
positions and post16:00buy intents. No saved account/risk blocks or financial holds. Source/profile1,
reviewed installed Core and original financial authority/history remain. The saved permission ends
exactly16:00Toronto/20:00UTC and is now expired. ExplicitNewBuysStopped=false is distinct from dated
expiry; no post-close broker revision/attempted-order expiry test is observed. Strategy entry cutoff
remains15:45. Session is Incomplete/CoverageBlocked/HostGapObserved; zero paper leases after close.
Do not classify the expected end-of-session state as a new execution stall or clean-day qualification.

Query `artifacts/delphi-live-watch-20261021/FinalRead.sql` hash
4DB49BE7A05A15573C5AC140B82050EE243102BB07FC2AACB63888E8F725B5D9; verified receipt
`result-20261021T200627565Z-aa4d23a4a1f44857b54a65adc2b82386.json` under read-only-db.
Final result reported to the operator and today's heartbeat deleted successfully. No next-day renewal,
chat archive, trading control change or diagnostic beyond the monitoring window is authorized by it.
The unrelated evening SQL reminder retains its residual-work scope; older retry draft remains parked.
