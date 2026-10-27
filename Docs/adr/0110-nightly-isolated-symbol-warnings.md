# ADR-0110: Continue nightly work after isolated ticker-source failures

**Date:** October 27, 2026 Toronto. **Status:** Accepted; implemented, tested and operationally verified at10:19.
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

## Authorized recovery — October 27

Joseph explicitly authorized one corrected Hermes → Delphi → Athena run. The installed Windows task
points to this repository's runner/configuration; no reinstallation or trading-engine restart was needed.
Run1605619d-1332-4a10-a05f-50bf1d621b34 captured clean commit4c8a204c and completed10:19:36 Toronto.
Hermes reported five unavailable daily-bar reads (JPHE, PDC, RCTR, SYZ, WM), retained their prior data,
and completed a checksum-verified/hash-matched backup copy. It exited0 with Attention; Delphi and Athena
then both succeeded. Overall Attention/exit2 preserves the warnings without stopping publication.
Sector metadata was already recently refreshed; its missing-response path is covered offline.

The hash-matched10:20 SQL check proves one Valid official October27 run using October26 observations:
208 candidates,416 lens evaluations, zero wrong observation dates. The saved Real strategy's four models,
input contract/configuration and policy still match. Nine symbols were excluded for invalid/unavailable
required model inputs. The approved comparison source adopted the run10:19:51, effective10:20; this is
prospective adoption, not backdated evidence for the missed open. Fresh intraday observations remain
required before buying. Zero Broker/Global holds and nine retained Paper restrictions were observed.

Reproducible private receipts: `completed-status.json`, recovery log SHA256
47FDE89AAB3FA0C3C1A1A96F6B03635154E899994D9D2E8E77C20A8B82C11212,
postflight SQL SHA25644F028E3305E9362E76AA40BC586F58D73072A64084E0BF4595B7BCC34E088D3.
Validation:73 runner assertions pass under both PowerShell runtimes;34 focused Core tests pass;
Hermes Release succeeds with259 existing compiler warnings. No fresh dependency-security audit or full
SSDT solution build is claimed. No model training, schema migration, source substitution or financial
control was performed by this recovery.

At 10:27, the first adopted candidate cycle published 28 inputs for the 10:25 bar. Twenty-five
candidate slots and XIU were OperationalOnTime; one candidate had NoCompletedBar and two were
StaleNoNewBar. Their rejected evidence did not block the other candidates. At 10:27:40 the unchanged
approved engine reported Running: fresh shared inputs evaluated, no confirmed entry signal. This
verifies restored data-to-execution flow, not a fill or native multi-day/protection qualification.
Private `intraday-postflight.sql` hash:
974FC15C699508EB4504FFDD962BAE908CF85C2477157D1223BFEF41258D2D70;
`execution-after-first-cycle.json` preserves the installed status receipt.
