# Trading market context — selected design and source check

November 5, 2026. The layout below is accepted by the operator; the collection design remains
provisional pending a market-hours source check. This record is not authority to install a collector,
change trading rules, write reference data or submit orders.

## Selected behavior

The immediate problem is seeing current market context alongside holdings. The parent problem is
having to navigate away from the account to understand sector conditions. The root goal is informed
account oversight while preserving the independent trading engine's decisions and protection.

- Use the **Holdings and sectors** layout, with a compact **Market now** summary above the holdings
  and sector performance/account exposure below. Each benchmark label and value share a line,
  such as `TSX Composite +0.42%`. Write the index/sector percentage-change baseline once.
- Add **Sector** immediately after Company name. Show industry, original source sector and the
  saved classification date on hover. Unknown or ambiguous classifications remain **Unknown**.
- The operator accepts one-, two- or five-minute refreshes and explicitly does not require
  exchange real-time data. Distinguish receipt time from source observation time. A two-minute
  collection cadence is the current recommendation, not an installed setting.
- These are display inputs. They do not alter selection, ranking, gates, valuations used for
  execution, broker prices, protection, or approval/enablement.

## Source inspection

Existing code already exposes `TmxClient.GetSectorIndicesAsync`, `GetBenchmarkIndicesAsync` and
`GetMarketSummaryAsync`. Delphi's visible sector card reads saved presentation evidence; its data
binding can guide presentation, but that captured daily snapshot cannot supply intraday updates.
The independent collector currently subscribes to XIU, holdings, pending exits and candidates; it
does not already publish the proposed complete sector/benchmark/breadth snapshot.

Bounded, operator-requested, read-only GraphQL checks ran **16:32–16:35 Toronto**, after the
regular close. No operational executable or Sandbox probe was launched and no SQL was written.
Private request/response evidence is in `artifacts/market-context-20261105/`.

| Check | Observation | Limit |
|---|---|---|
| Existing 13-symbol quote request | Eleven sector symbols and TSX 60 had values; configured Composite `^GSPTSE` had null prices/changes | Existing conversion to zero would conceal this missing quote; the new feed must preserve nulls |
| Provider Composite identity | `^TSX` resolved to `S&P/TSX Composite Index`; corrected request returned values for all 13 symbols | This is a prospective display-source correction; existing shared constants/financial behavior were not changed |
| Ordinary batch quote timestamp | `datetime` is not a supported field on `Quote`; current production query supplies no source timestamp | Receipt time does not prove the age of those prices |
| Separate dated feed | Composite, TSX 60, Energy and Materials each reported `2026-11-05T16:00:00-05:00` via `getDelayedQuotesFromQMBySymbols.lasttradedatetime` | This timestamps that separate feed, not the ordinary batch quotes; it cannot establish intraday latency after close |
| Market breadth | TSX, TSXV and ALPHA entries returned non-null advancing/declining/unchanged counts | Select TSX explicitly; the existing summary query supplies no observation time |

Public TMX descriptions also differ in scope: the [real-time quote announcement](https://www.blog.tmx.com/money/tmx)
describes selected last-sale-derived equity/index information as real-time, while the
[general terms](https://money.tmx.com/en/terms-of-use) describe market information as generally
delayed and direct readers to individual timestamps. The [Composite chart](https://money.tmx.com/en/advanced-chart/%5ETSX)
identifies `^TSX`. None of those statements measures this application's observed API latency.

**Conclusion:** the sources return same-day index data and TSX breadth, so intraday display is
plausible. A precise one-, two- or fifteen-minute data-delay claim is not established. The close-time
observations must not be described as a successful live cadence test.

## Sector-column implementation

The dashboard-only reader reads `StockSectorMap` plus verified `SymbolAliases` with source `Broker`.
It uses exact saved identities, never guesses a mapping by stripping a ticker suffix, and makes no
provider request. Known sector-index mappings supply the canonical display label; the raw source
classification and industry remain available in the tooltip. Conflicting identities remain Unknown.
The metadata read has a five-second cancellation bound. A failed read retains previous metadata
with an explanation and does not turn a successful broker/account refresh into a failure.

A hash-verified read-only SQL check at 16:36 Toronto found **387 mappings**, **57 Unknown** source
sectors and **five Broker aliases**. Classification dates ranged from April 17 through November 3.
This is reference-table coverage, not proof that every currently held broker symbol is mapped.
Receipt: `result-20261105T213609382Z-c5c907a8adce4b7f9f37955d8d4dabcf.json`, request SHA-256
`E137805E243CD027044E48AFDAF27DE427503996A9991806AEA3609184B43B04`.

The 239 focused tests pass, including exact/alias resolution, missing/conflicting metadata,
retained classifications after failure, unchanged valid sell selection and logout during a read.
The isolated WPF Release build passes with two existing unused-variable warnings. No dependency
restore/security audit was run. The revised interactive concept passed local, network-blocked
browser checks at 1024, 736 and 320 pixels with working sector highlighting and no page overflow.
This does not establish native WPF layout or installation.

## Dashboard-only installation

At **16:47 Toronto November 5**, the operator-authorized installation replaced only
`TraderVI.WPF.dll` and its symbols from tested commit `6f61c278`, after a normal dashboard close.
The new dashboard process is 20208. Engine 22780 and collector 32356 retained their original
November 4 start times, and all 116 other runtime files retained their hashes. Both installed
host manifests continue to select root Core/Calibration assemblies; neither manifest was changed.
Rollback files and hash/process receipts are in `artifacts/sector-column-install-20261105/`.

At 16:48, native accessibility confirmed Sector after Company name, populated saved classifications
and an Unknown mapping. The account snapshot had refreshed; the footer reported Engine Running
and Collector Outside session. This verifies populated controls, not complete mapping coverage.
Screenshot capture timed out, so pixel layout remains unverified. No engine/collector restart,
manual database mutation, financial-control change or intraday-feed installation was performed.
The dashboard resumed its existing account refresh/synchronization workflow.

### Unknown-classification diagnosis

The November 5 16:50 hash-verified read-only check found the displayed Unknown has a populated
canonical sector record, but the broker spelling has neither an exact sector record nor a saved
Broker alias. The new display reader therefore cannot join it. It does not yet reuse the verified
tracked-holding correspondence available through `HoldingMarketSymbolResolver`; that correspondence
must be checked for the current holding before use. This is a display-resolution gap, not evidence
that sector classification is absent or that engine resolution failed. No alias or trading state was
changed. Private evidence: `artifacts/read-only-db/result-20261105T215014631Z-a7190e9abfa14dbb82a63341bef914db.json`,
query SHA-256 `49FB621800343222E480F48864DE9478C006BF8D45FF5D14F58F13091D1B3FCD`.

## Next source check and implementation boundary

Use a bounded regular-session sample of benchmark/sector quotes and TSX breadth across several
two-minute intervals. Record actual receipt times, source timestamps where supplied, null/partial
rows, response durations and value changes. Check dated and ordinary quote feeds independently;
never attach one feed's timestamp to another feed's price. Unchanged values alone cannot identify
provider caching or delay. No recurring job or future-session probe has been scheduled.

After that evidence, implement the display-only collection/cache path under the independent
collector ownership contract ([ADR-0111](../adr/0111-independent-collector-and-recovered-confirmation.md)).
Protect existing collection capacity and separate the new source schema from immutable decision
evidence. Define unavailable, retained-after-failure, overdue and market-closed states; do not
replace missing data with zero, renew the data's timestamp merely because it was fetched again,
or use yesterday's Delphi snapshot as current intraday context.

The [UI checklist](../strategy-alignment-implementation-checklist.md#main-account-market-context--november-5)
owns remaining implementation and installation acceptance.
