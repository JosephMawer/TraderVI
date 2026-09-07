# ADR-0058: Daily ingestion and observed benchmark confirmation

- **Status:** Accepted; implemented and operationally verified
- **Date:** 2026-09-06
- **Domains:** architecture, data-pipeline, decision-engine
- **Related:** ADR-0004, ADR-0046, ADR-0055, ADR-0056, ADR-0057

## Context and authority

The immediate problems are missing Friday daily data and a SPY confirmation reported without SPY data.
The parent problem is dependable, attributable daily recommendations. The root goal is comparison of
independent paper strategies before human-approved real-account recommendations and later broker execution.

The operator authorized investigating and repairing both issues, including the missing-data refresh.
The task had been scheduled Monday–Friday at 00:30: Friday's successful Hermes invocation collected
Thursday, and no Saturday invocation collected Friday. This was a schedule gap, not a failed Friday
collection. The later run's Attention status came from Delphi/Athena evidence diagnostics.

Delphi reads SPY from DailyBars, which contains no SPY rows. The existing optional benchmark path defaults
both SPY flags to true. Existing collection loads TSX listings plus separate Genuity indices, not SPY.

## Decisions

1. Run the existing guarded pipeline at 00:30 Toronto time every calendar day. Preserve exclusive locking,
   same-date suppression, source/build identity, stage order, backup requirements and no automatic retry.
   Weekend and holiday recommendations keep their actual run date and last completed market-session date;
   repeated observations of the same market session do not become independent performance evidence.
   The existing read-only 07:00 supervisor also runs every calendar day and avoids unchanged repeated alerts.
2. Collect the actual SPY ETF through the already used Yahoo daily-chart adapter into DailyBars, separate
   from the TSX stock universe and from the Genuity indices. Use the provider's quote OHLCV fields, not its
   adjusted-close column. Reject malformed/duplicate bars and exclude out-of-request observations. Append
   missing dates without updating existing bars. Collect completed dates only. No substitute index is allowed.
3. For the new daily strategy policy, require valid XIU and SPY histories before official evaluation.
   Use the reviewed TSX calendar to identify the immediately preceding session and require XIU to reach it.
   SPY must cover that target date and have the existing 200-observation minimum. Reject incomplete shared
   inputs explicitly; do not represent missing information as bullish or bearish. Preserve the existing
   moving-average, 20-observation return, volatility and regime-gate formulas for complete observations.
   A US-only closure with no matching SPY observation therefore stops the new daily set; no unreviewed
   staleness tolerance is introduced. Existing-position protection retains its own rules. Internal
   historical US session-gap detection needs a separate reviewed US calendar; this change validates
   the target endpoint, minimum depth, ordering, duplicates and OHLCV without claiming that capability.
4. Register this behavior under a new strategy version with ADR-0058, reusing the four reviewed dated ML
   artifacts unchanged. The SPY filter is deterministic and does not change their training/input contract.
   Preserve prior strategy assignments and historical evidence. Runtime dispatches the reviewed benchmark
   policy from the strategy decision identity; rollback retains the predecessor's explicit behavior.
5. Report benchmark identity, dates, coverage and calendar version with new evidence. No trade, paper
   monitor, broker workflow or manual Delphi publication is launched as part of this repair.

## Validation boundary

Focused fixtures must cover stale/missing/invalid histories, no future data, unchanged complete-data
calculations, independent stock/model input handling, daily trigger configuration and recorded identities.
Operational validation checks the installed schedule, stored date coverage, selected preserved models and
benchmark-policy prerequisites. A data refresh does not prove future strategy performance. Frozen Delphi
Live V1 and existing thresholds remain unchanged.

## Completed repair

The installed task covers all seven days; its actions, principal and remaining settings match the saved
pre-repair export. Hermes added 481 daily bars with zero symbol-download failures, then the new SPY-only
maintenance path appended 503 observations. Both operations completed verified backups and hash-matched
local OneDrive copies. XIU/SPY and supporting indicators reach September 4. No remote sync attestation is made.

`v3.4-observed-benchmarks` (`48864449-92F4-4E33-99CF-40039210786E`) is selected with the same model-set ID
`46957ff9-826b-4bc7-8f7f-fed06cd426d7`. Its exact source archive and working-tree SHA-256 identity were
retained with review files. All 676 Core tests and the complete Release solution build, including SSDT,
passed. Actual benchmark/bootstrap checks and both transaction rehearsals passed. All 232 model rows and
38 historical evidence tables remain unchanged. No new Delphi run, Athena run or broker operation occurred.
The [repair review](../reviews/model-input-cutover-20260906.md) retains evidence and remaining limitations.
