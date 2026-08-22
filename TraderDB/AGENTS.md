# TraderDB Agent Guide

These rules extend the repository-root `AGENTS.md` for work under `TraderDB`.

## Role of the database project

- `TraderDB.sqlproj` is the source-controlled representation of the current schema and a build-time validation artifact.
- Do not use DACPAC publish, schema compare update, or project deployment against `TraderDB`.
- The canonical current-schema definitions live under `dbo/Tables` and `dbo/Indexes`. Top-level legacy SQL files predate the migration convention and are not authoritative schema definitions.

## Manual migrations

- Put each new schema change in a dated, immutable script under `Migrations` and update the matching canonical schema definition in the same change.
- Prefer additive changes. Do not generate or execute `DROP TABLE`, `DROP COLUMN`, `TRUNCATE`, unbounded `DELETE`, table-replacement, or data-copy/rebuild operations unless the user explicitly authorizes that exact destructive change after reviewing a fresh backup.
- Make preconditions explicit and fail with `THROW` when the actual schema is unexpected. Do not silently discard or coerce existing data to make a migration succeed.
- Use `SET XACT_ABORT ON` and a transaction where SQL Server supports transactional execution for the complete change.
- Never execute a migration merely to validate it. Build the SQL project, inspect the script, and obtain explicit authorization before running it against SQL Server.

## Backup and operational scripts

- Operational scripts under `Operations` are not compiled into the DACPAC and must not be run without explicit authorization.
- A successful checksum backup and verification is required before changing the recovery model or applying a schema migration.
- Never overwrite a backup file. Do not commit `.bak`, `.dif`, or `.trn` files to Git.
