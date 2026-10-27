# ADR-0110: Continue nightly work after isolated ticker-source failures

**Date:** October 27, 2026 Toronto. **Status:** Accepted; implemented and offline tested. Native recovery pending.
**Domains:** architecture, data-pipeline.
**Supersedes:** ADR-0046 rule 7 only for explicitly identified individual daily-bar/sector-metadata reads.

## Problem and decision

One missing WM sector-metadata response stopped the October27 pipeline before Delphi, despite completed
daily collection and a verified backup. The parent problem is an overly broad text-based failure rule;
the root goal is dependable overnight publication using eligible data. Joseph explicitly requires that
an unavailable ticker not prevent the rest of the overnight run.

Hermes marks unavailable individual daily-bar and stock-sector source reads with
`HermesSymbolUnavailable:`. Empty responses and source timeouts receive the same visible warning.
Continue to the next symbol, preserve existing data, and do not invent bars/mappings or refresh their
timestamps. Explicit cancellation, database reads/writes and processing errors remain fatal; only the
source-read boundary catches an exception. Request pacing also applies after missing-source responses.

The runner classifies a normally completed Hermes process with that marker as **Attention**, then runs
Delphi and Athena in order. A warning cannot mask timeout, an unaccepted exit code, backup failure or
an explicit global failure marker. Legacy generic nonzero `Failed:` output remains fatal so unknown
failures are not silently reclassified. No blanket continue-after-failure setting is enabled.

Delphi's existing individual history/feature exclusions and required XIU/SPY benchmark checks remain
unchanged. Missing/stale stock history does not become a fresh candidate. Old sector metadata retains
its actual age; no new staleness policy is implied. Unusable required shared inputs may still prevent
publication. No percentage-of-universe threshold, model/gate/ranking change or source substitution is
introduced. A warning is not clean success or proof that a stock is tradeable.

The exclusive run lock, captured source and artifact checks, immutable official evidence, backup,
same-date suppression and no automatic operational retry rules remain. The task invokes repository
scripts directly, so the fix applies on its next scheduled run without an engine restart. A manual
recovery run remains a separately authorized operational action; successful offline tests do not publish.

## Validation and current state

Five focused collection tests cover next-ticker continuation, unchanged prior data, empty responses,
source timeout, explicit cancellation and storage/processing failures. Existing daily benchmark and
history-freshness tests also pass (34 combined). Synthetic runner tests exercise the actual stage loop,
warning continuation, fatal precedence and unchanged Delphi/Athena contracts. Hermes Release builds
with existing warnings; no strategy or running engine binary is replaced.

At09:58 the read-only database check confirms Joseph's saved ongoing approval and zero October27
official publications. Initial approval is no longer pending; fresh source readiness remains separate.
Private evidence: `artifacts/nightly-symbol-warnings-20261027`.
