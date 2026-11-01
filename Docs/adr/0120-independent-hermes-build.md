# ADR-0120: Hermes builds independently of Core

- **Status:** Accepted; implemented in source, operational collection not exercised
- **Date:** 2026-11-01
- **Domains:** architecture, data-pipeline
- **Related:** [ADR-0119](0119-independent-data-audit-build.md), [ADR-0078](0078-isolated-latest-source-nightly-builds.md), [ADR-0058](0058-daily-ingestion-and-observed-benchmark-confirmation.md), [ADR-0114](0114-explicit-nightly-preparation-date.md)

## Decision and purpose

Hermes owns its collection-specific code and builds without Core. It shares existing implementations
only where other applications use them. The immediate problem is Hermes compiling and distributing
Core's unrelated ML/trading dependencies. The parent problem is unnecessary build coupling between
nightly apps and the rest of the system. The root goal is dependable nightly preparation with bounded
storage.

This reduces the dependencies compiled/copied for Hermes. It does not by itself stop nightly source
capture or repeated builds. Reusing precompiled releases and limiting retained versions remain separate
work in the [release design](../reviews/nightly-precompiled-releases-20261101.md). A change to shared
code that Hermes uses still requires rebuilding Hermes; merely running another build does not change
that code. Unrelated Core changes no longer invalidate Hermes's project dependencies.

## Ownership

| Location | Contents and actual consumers |
|---|---|
| `Hermes/Db`, `Hermes/Runtime`, `Hermes/TMX`, `Hermes/Indicators` | Backup/retention, SPY ingestion, Yahoo source and interface, sector classification, leadership acquisition and new-high/low collection helpers. These have no remaining production caller outside Hermes. |
| `TraderVI.MarketData` | Package-free daily bars, market/sector/benchmark identities, session calendar, benchmark validation, nightly preparation date, market record shapes and deterministic breadth/OBV/climax calculations. Hermes, remaining Core workflows and DataAudit use the relevant shared definitions. |
| `TraderVI.MarketStorage` | Quote, symbol, sector, breadth, OBV, climax, leadership and US-index repositories plus SQLBase. Hermes writes these observations; Delphi, saved-result readers, Athena and other Core consumers read them. SQLBase remains the same implementation for the remaining Core repositories. |
| `TraderVI.MarketProviders` | Existing TMX client, mapping/response types and intraday timestamp inspection; shared by Hermes and Core's collector/quote consumers. The collector request/client/framing contract is shared here; the collector server and host stay in Core. |

Hermes references MarketStorage and MarketProviders; each references MarketData. Core references all
three. None references Core. DataAudit still references only MarketData and its own SQL packages; it
does not inherit SQLBase or the provider clients. Unlike the audit's former use of a default connection
setting, these market repositories actually use SQLBase's query implementation. That existing reuse
justifies its shared home here. This supersedes only ADR-0119's description of SQLBase's location.

Original namespaces remain to limit caller churn. A `Core.*` namespace in an extracted file does not
mean a dependency on Core.dll. No source file is compiled into both assemblies.

## Accidental dependencies removed

- DailyBar moves out of the file containing the ML training prototype. Training remains in Core.
- The pure calendar interface/session bounds move separately from live-workflow contracts.
- DailyBenchmarkSnapshot holds market facts; the `ComputeRegime` extension stays in Core and invokes
  the same TradeDecisionEngine method. Benchmark validation retains its existing formulas and limits.
- Only OBV extensions move out of the older mixed Indicators utility. Indicator defaults are shared;
  Core.Constants retains aliases for existing consumers.
- Delphi uses the shared SPY identity directly, allowing SPY ingestion to belong solely to Hermes.
- The collector request retains the same canonical-symbol and UTC guards without depending on a
  trading evidence type. Client framing, timing, provenance and no-HTTP-fallback behavior are unchanged.

No SQL query, connection default, provider request, backup rule, trading rule, formula, threshold,
CLI option or exit-code policy changes. Package versions/hashes retained in the existing applications
are unchanged. Core's ML packages remain; Hermes no longer inherits them. The SQL library explicitly
preserves AsyncInterfaces 9.0.4 instead of allowing removal of ML to downgrade that dependency.

## Validation and installation boundary

Hermes.Tests references the Hermes executable project and owns the collection, backup, leadership and
SPY/Yahoo fixture tests. Its dependency test rejects Core, engine and ML dependencies in the resolved
runtime graph. Core tests retain shared calculations and collector transport coverage, including
canonical-symbol and UTC guard regressions. Provider tests use fixtures; backup tests use temporary
files. No operational executable is started for validation.

Hermes Debug/Release builds, standalone tests, Core regression tests, affected application builds and
nightly lock/regression checks use isolated artifact directories. Current measured results and their
limits belong in [project status](../project-status.md), with private logs under
`artifacts/hermes-extraction-20261101/`.

Moving types between assemblies requires a complete rebuilt package for a future installation.
Do not copy individual new DLLs over the running collector, engine or desktop. Their processes and
original executable/Core hashes are preserved. No deployment, schedule change, database operation,
provider call, training or artifact cleanup is part of this increment. The installed source-capturing
nightly runner can pick up the new project/lock graph at its next capture.

## Review questions

1. Why must shared market-repository changes still rebuild Hermes while unrelated Core changes need not?
2. Which ingestion code belongs only to Hermes, and why do the repositories and TMX client stay shared?
3. Which separate change will stop rebuilding and retaining a fresh nightly output every night?
