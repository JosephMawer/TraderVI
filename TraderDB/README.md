# TraderDB schema and migration workflow

`TraderDB.sqlproj` represents the schema TraderVI expects. It exists for source history, code review, and SSDT build validation; it is not published to the live database.

## Directory roles

- `dbo/Tables` and `dbo/Indexes`: canonical definitions of the current intended schema.
- `Migrations`: dated scripts applied manually, one reviewed change at a time.
- `Operations`: explicitly invoked backup, integrity, and recovery-management scripts. These files are not migrations and are excluded from the DACPAC.
- Top-level SQL files: legacy scripts from before this convention. Do not use them as the current-schema authority for new work.

## Schema-change sequence

1. Confirm a recent full backup with checksums exists and has been verified.
2. Create a dated migration under `Migrations`.
3. Update the matching canonical definition under `dbo`.
4. Build `TraderDB.sqlproj` with Visual Studio MSBuild and SSDT.
5. Review the migration for data movement or destructive statements.
6. Obtain explicit authorization to execute that exact script.
7. Run it manually against `TraderDB`.
8. Verify the expected object, columns, constraints, indexes, and preserved row counts.

Never use Visual Studio Publish, `SqlPackage /Action:Publish`, MSBuild `Deploy`, or schema-compare update against the live database.

See `Docs/database-operations.md` for backup, retention, recovery-model, and restore-test guidance.
