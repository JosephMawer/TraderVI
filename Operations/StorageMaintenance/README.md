# Live storage maintenance

This tool uses the local TraderDB connection and does not construct a collector, model trainer or
trading runtime. See [ADR-0082](../../Docs/adr/0082-compact-live-snapshots-and-backup-retention.md).
Migrations 037/038, lossless conversion, verification of all 4,472 historical/current snapshots,
post-conversion backup/retention and native WPF restart acceptance completed September 17.
Operational evidence is in `artifacts/live-storage-20260917/`. Do not replay the installed migrations.

Build in an isolated location while WPF is running:

```powershell
dotnet build Operations/StorageMaintenance/StorageMaintenance.csproj --artifacts-path artifacts/storage-maintenance-build -p:NuGetAudit=false
```

Read-only preview (first/last historical revision per day plus current; `--all` reads all history):

```powershell
dotnet artifacts/storage-maintenance-build/bin/StorageMaintenance/debug/StorageMaintenance.dll preview
```

The preview round-trips raw JSON and domain readers, reports fingerprints and payload sizes, and never
saves portfolio data. It excludes SQL row/index overhead. Avoid printing or committing full snapshots.
`retention-preview <verified-staging-file> <verified-destination-file>` inspects paths and counts proposed
removals using SQL backup-history completion evidence without deleting anything or asserting cloud synchronization.
Unregistered/failed legacy files are not treated as completed backups and need separate review if they consume space.

## Reviewed installation procedure

1. Review migration `TraderDB/Migrations/20260917_037_AddCompactDelphiLiveSnapshots.sql`, this converter,
   preview evidence and a recent verified backup. Obtain explicit approval of the schema installation,
   lossless historical JSON conversion and controlled WPF interruption. Do not deploy the DACPAC.
2. Close WPF and other Live writers normally; confirm the host lease is released. Record current row
   counts, portfolio revisions, events and database/file sizes. Create a fresh full checksum backup,
   verify it and its destination copy before proceeding. Keep the latest two completed files per location.
3. Apply migration 037 manually with sqlcmd `-b`, setting `$env:VerifiedBackupFile` to the absolute staging `.bak` path in that process first. This avoids legacy Windows SQLCMD `-v` parsing of a drive-letter path.
   It checks the backup is at most two hours old and repeats checksum verification. Do not replay it
   if its objects exist. Migration 035 is unrelated and is not required or authorized by this operation.
4. Install follow-up migration `20260917_038_ValidateLegacySnapshotsBeforeReplacement.sql` using the
   same SQLCMD environment variable and `-b`. It keeps the history guards unchanged but validates the
   old JSON before replacement to avoid SQL Server large-value version-reading costs. Never replay
   either installed migration. After approval and installation, run `verify-storage` once while hosts remain stopped. This native
   check saves nine original beginning/middle/current snapshots using the actual SQL storage path,
   rereads older snapshots after later versions exist, and rolls back the entire transaction. It never
   updates portfolio or historical-root rows. Its native regression coverage includes temporary-table
   lifetime, binary hash collation, report sharing and revision isolation. Then perform conversion:

   ```powershell
   dotnet artifacts/storage-maintenance-build/bin/StorageMaintenance/debug/StorageMaintenance.dll convert --verified-backup '<absolute staging .bak path>'
   ```

   The converter refuses active hosts, acquires an exclusive storage lock, commits one verified row
   at a time and preserves all row identities and timestamps. If interrupted, keep hosts closed,
   inspect the failure, refresh the backup if necessary, then resume the same command. It verifies
   converted rows and skips rewriting them. No destructive recovery is automatic.
5. Run `verify-all`; it takes the exclusive storage lock and uses two bounded read-only workers to
   reconstruct, fingerprint-check and domain-decode every stored snapshot without redundant repacking.
   Verify unchanged revision/event/portfolio counts, no remaining legacy roots,
   current roots equal their matching historical revision, and restored holdings/cash/guards match.
   Build and launch the updated WPF normally and verify its saved portfolio/restart state before
   resuming collection. Do not run an older WPF binary against converted roots.
6. Create a post-conversion verified backup and copy, inspect measured size, and verify retention and
   OneDrive synchronization. SQL free pages are reusable; this procedure does not shrink the data file.

Conversion is not an ordinary validation test. Production installation, conversion, app restart and
post-conversion native acceptance require their own operational evidence. A failed row rolls back its
transaction; previous converted rows remain valid and readable by the new binaries. Preserve them and
the verified backups. Restoring TraderDB or removing storage objects requires a separately reviewed plan.

`backup` invokes only the verified backup/copy/retention service, without running Hermes collection.
Use it for the explicitly approved pre- and post-conversion backups, saving stdout as the receipt and
stderr as the operational log. Its completion receipt includes both paths, size and SHA-256.
