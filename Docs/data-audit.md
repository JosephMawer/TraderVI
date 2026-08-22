# TraderVI Local Data Audit

`DataAudit` is a read-only console application for regularly checking the complete local symbol universe and its core market-data relationships.

## Safety boundary

The default audit:

- reads local `TraderDB` tables with `ApplicationIntent=ReadOnly`;
- makes no external HTTP or market-service calls;
- performs no inserts, updates, deletes, migrations, or repairs;
- writes no report files or model artifacts.

It intentionally reports classification and listing **candidates** rather than changing them. Confirm each candidate against an official exchange, issuer, or fund-provider source before creating a guarded manual migration.

## Checks

Every row in `dbo.Symbols` is inspected, including inactive history. The audit reports:

- empty or unexpected symbol/security-type values;
- active symbols with no bars, future bars, or stale bars;
- likely funds still classified as stocks;
- leveraged/inverse names missing the dedicated exclusion flag;
- active stocks with missing, unknown, stale, or invalid sector mappings;
- missing or stale history for referenced TSX sector indices;
- invalid OHLC invariants, negative volume, duplicate symbol/date bars, and orphan bars or mappings.

Freshness is measured in completed XIU trading sessions, not calendar days. By default, two missed sessions are a warning and five are an error. Adjusted closes may legitimately sit outside the raw daily high/low after distributions or corporate actions, so that relationship is not treated as corruption.

## Running

Run after Hermes and its backup have completed, when no data import is in progress:

```powershell
dotnet run --project DataAudit
```

Optional thresholds:

```powershell
dotnet run --project DataAudit -- --warning-sessions 2 --error-sessions 5 --mapping-age-days 14
```

Set `TRADERVI_CONNECTION_STRING` only when intentionally auditing a different TraderDB instance. The connection string is never printed.

Exit codes:

- `0`: no findings;
- `1`: warnings only;
- `2`: one or more errors;
- `3`: invalid arguments or the audit could not run.

## Review workflow

1. Run the audit after a successful Hermes update.
2. Start with errors, especially stale active symbols and structural bar problems.
3. Group overlapping warnings; a fund-like stock with an unknown sector is normally one classification investigation, not two independent defects.
4. Verify current security type and listing status using authoritative sources.
5. Prepare a narrow, guarded manual migration; never let the audit repair rows automatically.
6. Rerun the audit after the approved script is executed.

The audit detects stale data, but it is not a runtime safety gate. Delphi must independently exclude stale symbol histories before evaluation; that is a separate follow-up change.
