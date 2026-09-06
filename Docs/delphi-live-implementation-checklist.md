# Delphi Live V1 implementation checklist

Frozen behavior: [Delphi Live](concepts/delphi-live.md), accepted by [ADR-0053](adr/0053-delphi-live-v1.md).
This checklist records source implementation, validation and separately authorized migration work;
it does not itself grant operational authorization.

## Reviewable phases

| Phase | Source boundary | Status |
|---|---|---|
| 1 | ADR, review aids, immutable identities and validated settings | Reconciled; frozen concept unchanged |
| 2 | Measurements, four families, combined order, lifecycle, protection, quote and portfolio arithmetic | Implemented; focused regression tests passing |
| 3 | Frozen session/source, durable lease, collection slots/receipts, independent portfolio revisions, evaluations | Implemented; offline persistence contracts and SQL syntax validated |
| 4 | Shared Core scheduling, recovery, evaluation and causal action orchestration | Implemented; synthetic host and recovery regression tests passing |
| 5 | Research outcomes/baskets, cohort coverage, experiment phases and human promotion | Implemented; focused protocol and reporting regression tests passing |
| 6 | Inactive WPF host, activation command, independent diagnostics and portfolio views | Implemented; complete Release solution build passing |
| 7 | Migration, source-capacity shakedown and operational activation | Migrations 022–025 applied and verified on 2026-09-06; calendar, capacity shakedown and activation remain outstanding |

## Source organization

- `Core/Trader/DelphiLive`: host-neutral contracts, deterministic measurements/judgments, policy selection, lifecycle, safety, collection, action and monitor workflows.
- `Core/Db/DelphiLive*`: transactional storage with durable lease fencing, immutable source snapshots, evidence and isolated portfolio history.
- `Core/Runtime/DelphiLiveDesktopService`: initial desktop composition; read-only status and explicit activation are separate entry points.
- `TraderVI.WPF/Views/DelphiLiveView*` and `Viewmodels/DelphiLiveViewModel.cs`: display and operator commands.
- `TraderDB/Migrations/20260905_022_*` through `025_*`: individually reviewed additive migrations, applied on 2026-09-06 after separate authorization and backup.
- `TraderVI.Core.Tests/DelphiLive*`: synthetic/offline regression tests; no market, broker or live-database fixtures.

## Operational prerequisites

The initial policy and generated schema install inactive. Simulation capital has no default. An explicit positive capital amount, currency and operator reason are required; activation takes effect at the next regular-session boundary.

The host requires a locally supplied, reviewed official TSX calendar snapshot through `TRADERVI_TSX_CALENDAR_PATH`. `ReviewedTsxSessionCalendar` validates a version, source reference, declared date coverage and distinct regular-session dates. It fails outside coverage and never substitutes a weekday guess. Calendar source verification and installation belong to separately authorized rollout; no external calendar service is called while implementing or testing.

The WPF process is the V1 host. A closed or interrupted host leaves visible coverage gaps. Assigned Delphi Live policies share each V3 observation; legacy Ghost and frozen System Shadow retain their existing collectors and accepted timing while sharing compatible canonical facts. Combined provider load must be measured during authorized shakedown.

Start WPF sufficiently before 09:30 Toronto time to establish successful pre-open heartbeats, and keep it open until the final 16:02 collection and closing persistence complete. The eligible official daily run and required daily baselines must already be available before the session freezes; starting Delphi Live does not generate them.

## Integration guarantees covered in source

- Shared market judgments do not share portfolio ownership or confirmation. The continuing champion and its cash-only experiment control can hold, exit and re-enter independently. Only a portfolio's own current holding or same-session completed exit grants carry authority.
- A recent successful pre-open heartbeat whose pending wake spans the regular open can attest to opening liveness; stale heartbeats, missed wake intervals and late starts record a gap even before the first collection. The WPF host supplies its thirty-second cadence. Completed session finalization is idempotent. Restart clears unfinished confirmation, expires buys and retains protective sell identities.
- A source receipt is operational only after a post-commit SQL time check proves the canonical evidence was already durable before the deadline. Provider request/attempt metadata and untouched deadline misses remain visible.
- Every expected research slot keeps its original operational disposition. Later canonical facts can mature research, while conflicts and corporate-action audits exclude affected evidence without rewriting earlier observations or fills.
- Daily portfolio return uses the exact previous canonical closing NAV, or immutable starting capital on inception; it includes overnight movement. Missing prior/closing marks cannot be replaced with an opening mark or an older close.
- Research maturity updates the cohort's original phase. Promotion rechecks the durable untouched evidence at its effective boundary and is cancelled if a later audit invalidates the previously passing result.
- Research refreshes use a completed-review watermark and changed-source dates. Historical reports load for an explicit date range of at most 366 calendar dates, separately from the monitor's periodic refresh. Portfolio history selects immutable revisions before the report cutoff and any generation ending, preserving historical policy identities. The fill diagnostic compares all closed trades with the bid/ask-only closed-trade slice and labels its distinct basis beside official portfolio NAV return.
- Read-only component, threshold, safety and confidence tables retain coverage, timing, forward outcomes, downside and opportunity capture, including missed opportunities. Their research-only comparisons have no action or promotion authority.
- Descriptive portfolio statistics use each generation's own inception and report cutoff, including ended comparison runs. Win rate counts profitable completed full-position trades. Exposure averages exact marked position value/NAV within sessions, then weights sessions equally; gross turnover is total filled buy-plus-sell notional divided by immutable starting capital. The no-fill rate counts requested actions without a fill at the cutoff, with still-pending actions shown separately. These diagnostics do not replace the frozen promotion test.

## Validation record

- .NET SDK 10.0.400 verified in this environment.
- The interrupted prior dependency restore was repaired with explicitly approved dependency restoration. Subsequent validation uses `--no-restore`.
- All 581 Core tests passed in both Debug and Release: zero failures and zero skipped tests. This includes deterministic policy, collection capacity/deadline, session recovery, independent portfolio actions, research, experiment, diagnostics and persistence contract regressions.
- Offline SQL Server 2019 `TSql150` parsing passed for 59 embedded SQL blocks and migrations 022–025 with their table includes expanded. Dapper list placeholders were normalized for syntax parsing only.
- Synthetic collection tests include a full disjoint Top-25 union, ten additional held symbols and XIU (61 unique symbols) across three policy copies, and a persistence delay that crosses the collection deadline. These establish orchestration behavior, not real provider throughput.
- The complete Release `TraderVI.sln` build succeeded with Visual Studio 18 MSBuild and SSDT, including `TraderDB.sqlproj`; the DACPAC was not deployed.
- Existing compiler warnings remain, principally nullable-context annotations and unused/unreachable code. Dependency-security auditing was not rerun; no-restore builds do not establish that dependency advisories are resolved.
- `git diff --check` passed. The frozen concept and concurrent calibration/System Shadow work were preserved; no commits were created.

## Migration application — 2026-09-06

The user explicitly authorized backup and application of migrations 022–025. Independent review added
missing precommit checks to the then-unapplied 023–025 scripts; their final versions and all 27 table
includes passed offline SQL parsing. SQLCMD applied each migration separately, in order, with the
required index session settings. The SQL database project built successfully and 18 focused database
contract tests passed.

- Backup: `TraderDB_FULL_20260905_235931_669.bak`, 41,238,016 bytes, with checksums enabled and no damage.
  `RESTORE VERIFYONLY WITH CHECKSUM` passed. The staging and OneDrive copies had matching SHA-256:
  `6E4574B7A6EBC40CAE243C28BB3297BAA3ED27CAB82990572B23BBEDE82A76EE`.
  Windows Cloud Files reported the OneDrive copy in sync before application.
- All 37 existing tables retained matching exact row counts, aggregate content checksums and schema
  fingerprints. Aggregate checksums are supporting preservation evidence, not a restore rehearsal.
- Exactly 27 tables were added, with one inactive V1 definition and zero operational rows. Its 48-key
  settings JSON matches the stored UTF-8 SHA-256. All new constraints are enabled and trusted, and all
  indexes are enabled. `DBCC CHECKDB` reported no errors; recovery remains `SIMPLE`.
- The installed catalog has zero structural differences from the source manifest across 356 columns,
  210 constraints and 79 indexes, including types/nullability, key mappings and index columns. SQL
  Server's normalized expression text was retained separately from this structural comparison.
- The [application record](reviews/delphi-live-migrations-20260906.json) preserves backup verification,
  validation results and the SHA-256 of every applied script and included table definition.

## Authorization boundary

The user separately authorized backup and application of migrations 022–025 on 2026-09-06. They are now
applied and must not be edited or rerun. The authorization did not include application/trading activation,
external market calls, model training, model-artifact publication, broker operations, commits, pushes or
pull requests. Calendar and operational rollout remain separate explicit operator actions.

The remaining rollout risks are live SQL concurrency/latency, combined provider capacity, reviewed calendar coverage,
and actual WPF operation through complete sessions. Offline tests and build/parser checks do not establish those
operational facts. The application record below identifies the exact reviewed migration files and their
referenced table definitions.
