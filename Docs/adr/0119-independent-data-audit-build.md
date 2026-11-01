# ADR-0119: DataAudit builds independently of Core

- **Status:** Accepted; implemented in source, operational run not exercised
- **Date:** 2026-11-01
- **Domains:** architecture, data-pipeline
- **Related:** [ADR-0113](0113-nightly-data-audit-report.md), [ADR-0078](0078-isolated-latest-source-nightly-builds.md)

**Later ownership update:** [ADR-0120](0120-independent-hermes-build.md) moves SQLBase and the market
repositories used by Hermes/Core into MarketStorage. Statements below that SQLBase stays in Core
describe this earlier increment. DataAudit's own SQL implementation and dependency boundary remain.

## Decision and rationale

DataAudit owns its audit workflow, report models, rules, SQL reader and connection configuration.
It references one shared, package-free project, TraderVI.MarketData, plus its SQL client packages.
Neither project references Core. Core also references TraderVI.MarketData.

The immediate problem is rebuilding and copying Core's ML/provider dependencies for a read-only
SQL audit. The parent problem is unnecessary coupling between nightly applications and unrelated
Core changes. The root goal is dependable nightly preparation with bounded build storage.

Application-specific code belongs in DataAudit itself. Its SQL reader already executes its own
SELECT statements with SqlClient; it only used SQLBase for a default connection setting. DataAudit
now owns equivalent local integrated-authentication defaults, preserves the explicit configured
connection, and forces ReadOnly application intent. SQLBase stays in Core unchanged. ReadOnly
application intent is retained behavior; the SELECT-only implementation supplies the audit's
no-write behavior, not a claim that connection intent enforces database permissions.

## Shared boundary

TraderVI.MarketData contains only these currently shared pieces:

- TsxSectorSymbols, used by auditing, collection, saved presentation and Granville calculations.
- DailyBenchmarkSymbols.Spy, shared with SpyBenchmarkIngestion through its retained Symbol constant.
- MarketDataFreshness, used by auditing and Core's HistoryFreshnessEligibility.
- SecurityNameHeuristics, used by auditing and Delphi's universe checks.

The original namespaces remain to avoid unnecessary caller/serialization changes. A namespace
starting with Core does not mean the containing assembly references Core. No rules, thresholds,
sector mappings or report schema change. The audit retains report version 2 and its existing CLI
options, findings and exit codes. ADR-0113 still governs the nightly stage and reporting contract.

DataAudit.Tests references DataAudit directly. It owns the moved audit tests, a resolved-dependency
check rejecting Core/ML packages, and tests of default/explicit SQL connection options without
opening a connection. There is no separate audit service library or shared SQL project.

## Build and operational consequences

The solution includes the new market-data library and audit test project. The existing recursive
nightly lock check includes TraderVI.MarketData; all affected nightly lock files are updated.
Retained package versions/hashes are preserved. DataAudit explicitly retains AsyncInterfaces 9.0.4
because removing ML otherwise changes its transitive version. Core's ML packages are unchanged.

DataAudit can compile and test without compiling Core or distributing its ML/native/provider
packages. Other apps still depend on Core; their extraction and precompiled releases remain work
in the [release design](../reviews/nightly-precompiled-releases-20261101.md).
Existing old nightly folders are unaffected by this source change.

All validation uses isolated output directories. Moving shared types requires complete rebuilt
packages for future installation; do not copy a new Core DLL over a running host's dependencies.
The running collector, trading engine and WPF app retain their original binaries/processes.
Consequently the already-running WPF still shows its old Delphi run control until a separately
installed desktop build incorporates ADR-0118. No desktop/engine switch is part of this change.

## Review questions

1. Which code is shared because Core actually uses it, and which code belongs only to DataAudit?
2. Why does owning SQL configuration remove DataAudit's dependency on SQLBase?
3. Why does this build change neither delete old artifacts nor update the running desktop?
