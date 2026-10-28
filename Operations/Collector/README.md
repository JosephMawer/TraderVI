# Independent collector package — prepared, not installed

ADR-0111 separates shared intraday requests from trading. `TraderVI.Collector` references Core but
constructs no broker, credentials, order adapter, financial journal or execution host. Its explicit
`--run <absolute-config-path>` entrypoint reads a reviewed calendar, a dated subscription union and
bounded settings. `--check-config` validates configuration/calendar without SQL or market access.

Configuration fields (PascalCase) are `CalendarPath`, `SubscriptionPath`, `ControlDirectory`,
`SourceContract` (`TmxMinuteCollectorV1`) and `Limits` (`DeadlineOffset`, `RequestTimeout`,
`MaximumSymbols`, `MaximumRequestsPerSession`). There are no selected production defaults; provider
capacity/freshness qualification must settle the package values. The subscription JSON contains UTC
`SessionOpenUtc`, `SessionCloseUtc`, and arrays `Candidates`, `Holdings`, `PendingExits`. An unavailable
or wrongly dated subscription file blocks requests. The producer must atomically publish the complete
union, preserving holdings/exits when candidate inputs change or entries are paused. Optional
`UseVerifiedRealSubscriptions: true` refreshes that file every 30 seconds from the trader's
revision-verified `EngineRealObservation` projection and adopted daily sources. It performs no broker
reads. Missing daily candidates retain holdings and pending orders; an integrity/read failure reports
attention and leaves the previous complete file intact. Legacy Paper subscription integration remains
part of the pending ownership cutover.

The collector reads one completed minute at a time; each bounded TMX response includes the current
session's available history for recovery. No catch-up request burst or automatic retry exists. XIU,
holdings and pending exits precede candidates. A deadline/capacity shortfall is a qualification failure,
not permission to omit symbols silently or relax freshness. Transport startup waits one minute to
avoid repeating a prior process's request; durable history and confirmation are not reset by that wait.
Each SQL request intent is committed before HTTP and consumes the session budget even if the process
crashes. Receipts retain that intent identity and owner generation. Lease loss fences the write.

`status.json` records dated activity without prices; `stop.request` requests a draining stop and remains
present across restarts. Removing a deliberate stop is an explicit operational action. The supervisor
`Watch-Collector.ps1` requires a reviewed manifest with executable/configuration absolute paths,
`ConfigurationSha256`, a complete `Files` array (`RelativePath`/`Sha256`) for every runtime file,
matching control directory and an explicit maximum of zero to five restarts. Keep configuration/control
files outside that immutable runtime directory. It starts
hidden, owns an exclusive supervisor lock and never clears a stop. It is not registered or launched by
builds. Startup registration and live use are part of the guarded cutover, not these source changes.

An opt-in native rehearsal is prepared in `TraderVI.Core.Tests/MarketCollectorSqlTests.cs`. With explicit
authorization only, `TRADERVI_COLLECTOR_SQL_QUALIFICATION=1` enables its single filtered test. It creates
and retains one uniquely named `TraderVI_CollectorQualification_<random ID>` database on localhost,
installs only the six synthetic collector tables/five guards, and checks competing owners, request
reservations, crash/idempotent recovery, conflicts, immutable records and transactional reader isolation.
It never connects to TraderDB or constructs market/broker clients. Ordinary test runs skip it. This
qualifies repository behavior, not the production migration's backup/stop preconditions.

Migration052 remains unapplied and now includes immutable request intents. Native SQL concurrency,
crash/idempotency and permissions rehearsal, native subscription/local-consumer verification,
source qualification and compatible installation are still required. Do not launch alongside the
installed intraday poller. Do not replace the approved runtime until ownership/cutover checks pass.

## Legacy compatibility during cutover

The prepared `collector-routing.json` contract routes intraday bar calls from the desktop and trading
engine to the same-user local collector pipe. It contains only `Contract` set to
`IndependentIntradayCollectorRoutingV1` and `SourceContract` set to `TmxMinuteCollectorV1`. Absence keeps
the existing runtime's behavior; an invalid present file fails startup. This file has not been installed.
Once configured there is no direct intraday HTTP fallback, including diagnostic entrypoints.

The collector's optional `LegacyIntradayConcurrency` (zero disabled; explicitly selected one through
eight for cutover) serves the original bounded five-/fifteen-minute TMX requests. Legacy consumers retain
their current collection lease, source contracts, publication/SQL audit and independent Paper lifecycle.
The original provider receipt times are transported unchanged; minute aggregation is never relabeled
as an original five-minute TMX receipt. New minute entries consume durable SQL books directly. Broker
executable quotes/protective bids remain trading-engine responsibilities. Legacy quote operations are
unchanged; this routing controls intraday bars only.

Each relay request/response is bounded, same-user only, with cancellation and no relay retry. The old
provider's bounded retry contract remains visible in its original attempt count. A fixed per-user process
lock prevents separate configuration paths from starting competing collectors. Deliberate stop refuses
new relay requests and drains/cancels accepted work. Eight offline tests verify receipt identity/times,
failure/no-fallback, framing, routing and in-process local-pipe behavior. Collector and isolated WPF/engine
Release builds pass. Native installed process ownership, stopped-trader continuity and combined minute
plus legacy provider load are still unqualified; three-symbol TMX sampling cannot establish that capacity.
