# Manual database migrations

## Compact trading storage — prepared only October 30

`20261030_054_AddCompactTradingCurrentState.sql` prepares ADR-0117's seven isolated current-state,
order-receipt and checkpoint tables, active/unique indexes and approval/order guards. It is additive,
requires the existing account binding and a fresh verified checksum backup, and refuses existing or
partial compact tables. No data conversion, cleanup or activation is included. SQL project compilation
and offline schema-parity tests do not execute this script. It has **not been installed**; native
transaction/latency qualification and individual installation review remain required. Do not deploy
the DACPAC or use production SQL to rehearse the script without separate authority.

The prepared follow-up `20261031_055_AllowExpiredCompactOrderReceipt.sql` gives expiry its own
terminal receipt status. It requires 054's enabled guards, an empty compact order table, and a
fresh verified backup. Apply both reviewed scripts in sequence before any activation. The offline
schema build and parity tests cover the definitions. Installation remains pending.

## Independent collector storage — applied October 28 morning

`20261028_052_AddIndependentCollectorEvidence.sql` was applied under Joseph's explicit approval after
a fresh checksum/RESTORE VERIFYONLY backup and hash-matched backup copy. Six tables and five guards
were verified by the installed read-only helper; installation receipts remain private under
`artifacts/collector-install-20261028`. Remote backup synchronization is not established. ADR-0111 adds isolated
collector lease/session/request/first-observation/conflict tables and immutable evidence triggers.
The runtime repository fences each append with the database clock and owner generation, commits the
request and derived evidence atomically, and reads one coherent session boundary. These tables do not
replace existing strategy/financial history. The script requires stopped market writers, existing
engine schema and a fresh checksum-verified full backup. Do not use the DACPAC to deploy it.

Source/SSDT validation and offline reducer tests do not prove native SQL fencing, deadlock/crash behavior
or permissions. Rehearse in an explicitly authorized isolated database before requesting a production
migration/cutover. Keep all evidence on binary rollback; do not delete tables or rows to simulate recovery.

This directory records the exact schema scripts applied to `TraderDB` after adoption of ADR-0018. Existing top-level scripts in `TraderDB` predate this convention.

## Progressive protection rollout

- `20261017_043_AddProgressiveProfitProtection.sql`: additive per-position Shadow protection state,
  installed October 18 after a checksum-verified backup and hash-matched copy.
  The script does not enable the strategy or change existing stops/assignments.
- `20261018_044_AllowProgressiveComparisonProtection.sql`: installed October 18; 12 native checks pass.
  Permits only the progressive successor while preserving fixed comparison rules.
- `20261018_045_CompareCanonicalLiveProtectionSettings.sql`: installed October 18 canonical Live-setting
  comparison correction; SSDT build and native prepared-successor validation pass. No account/profile
  rewrite. All three scripts used the fresh verified rollout backup; do not replay installed migrations.

- `20261018_046_AddReadOnlyDiagnosticUser.sql`: security-only migration installed October 18 for
  ADR-0091's fixed local diagnostic helper, after a fresh verified backup/hash-matched copy. It adds
  a loginless dbo-SELECT user; no application rows or existing identity permissions change. Native
  permission and syntax checks are documented in `Operations/ReadOnlyChecks/README.md`.

## Naming

Use UTC-independent local calendar order and a same-day sequence:

```text
YYYYMMDD_NNN_ShortImperativeDescription.sql
```

Example:

```text
20260821_001_AddMarketRegimeIndex.sql
```

## Required script shape

Each migration must:

1. State its purpose, expected precondition, data effect, and rollback/recovery approach in the header.
2. `USE [TraderDB]` explicitly.
3. Enable `SET XACT_ABORT ON`.
4. Check the expected starting schema and use `THROW` on an unexpected state.
5. Use a transaction when the complete operation supports it.
6. Verify its postcondition before committing.
7. Avoid being edited after it has been executed; add a later corrective migration instead.

## Destructive operations

Migrations are additive by default. The following require a separately reviewed decision, a fresh verified backup, data-preservation evidence, and explicit authorization:

- dropping a database, table, column, constraint, or index;
- truncating or deleting rows;
- narrowing or reinterpreting a data type;
- rebuilding a table through copy/drop/rename;
- replacing data as part of a schema operation.

An `IF EXISTS ... DROP` wrapper does not make a destructive operation safe. If removal is intentional, preserve the old data in a reversible transition and separate deprecation from deletion whenever practical.
