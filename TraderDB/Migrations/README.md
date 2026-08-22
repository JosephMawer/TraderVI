# Manual database migrations

This directory records the exact schema scripts applied to `TraderDB` after adoption of ADR-0018. Existing top-level scripts in `TraderDB` predate this convention.

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
