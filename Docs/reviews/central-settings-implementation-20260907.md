# Central settings implementation — 2026-09-07

Status: implemented and rolled out with explicit operator authorization on 2026-09-07.
Decision: [ADR-0060](../adr/0060-central-settings-and-scoped-configuration.md).

## User flow

Open **Settings** in the main TraderVI tab bar. The Delphi, Delphi Live, Trading and Portfolios pages
link to the relevant section. All links reach the same editors.

1. Select the system and a saved version or template.
2. Edit supported fields, supply a new name and reason, then **Save Version**. No assignment changes.
3. Select a saved, unedited version and an existing target, then **Review assignment**.
4. The review names the target, prior version, new version, impact on holdings/actions and reevaluation.
5. Assignment is committed atomically. Reevaluation starts separately; its failure does not silently
   restore the previous version. The UI distinguishes these outcomes. Scheduled hosts retry with the
   assigned rules; closed markets and unavailable evidence remain explicit waiting conditions.

## Implemented control inventory

| Section | Editable controls | Scope / retained contracts |
|---|---|---|
| General & operations | Automatic eligible Ghost exits; durable local preference | Trading monitor only; Real exits remain manual. Schedule, timezone, calendar and freshness contracts are shown as reference. |
| Daily Delphi | Preserved four-model strategy selection; eight decision gates | Save and Assign are separate. Assign starts an official local-data reevaluation. Input compatibility, source DecisionRef and four model hashes remain checked. |
| Delphi Live | 16 supported movement, volume, structure, protection, sizing and risk settings | Exact policy validation applies. Fixed evidence horizons, observation counts and research contracts are not exposed as editable. |
| System Shadow | Nine allocation, loss, friction and re-entry fields | The existing Continuation/Breakout Top 3/5 portfolio definitions remain fixed. Every executed policy/fill path receives the assigned config. |
| Trading monitor | Ten delayed-swing exit, cost, session-limit and probability settings | All tracked positions share this exit-policy target. Fifteen-minute bars and freshness remain fixed. Entries and actual fills are not rewritten. |
| Portfolios & accounts | Existing target selection and strategy assignment | Capital, holdings, entry facts and completed history remain account facts. Creation, activation, rename, pause/resume and fill recording retain their operational-page workflows. No second account-cap layer. |

This is a bounded initial settings surface, not an editor for every application constant or service.
Daily trained model sets are distinct from the deterministic policies used by the intraday systems.

## Assignment and state rules

- Immutable `EngineStrategyVersion` snapshots carry a family, name and verified settings checksum.
  `EngineStrategyAssignment` is append-only; the newest event for a target is its sole assignment.
  Save never inserts an assignment. Cross-family assignment, unsaved edits, stale target reviews and
  corrupted snapshots are rejected.
- Production Shadow, Trading and Delphi Live cycles hold the shared `TraderVI.EngineSettings` SQL
  application lock through evaluation and writes/fills. The assignment transaction obtains the exclusive
  lock. An already-running cycle completes before assignment succeeds; it cannot continue under its old
  snapshot after the switch. Daily Delphi retains its separate selection lock through publication and
  honors the nightly file lock.
- Live and Shadow pending internal buys/sells become cancelled with `StrategyReassigned`. Their original
  dossiers/events remain auditable. Fresh decisions require the new rules and causal evidence.
- Protection levels are recalculated from actual entry costs and observed highs. They may tighten or
  loosen with the new rules. Observed highs and their event timestamps are retained. A new floor is not
  applied retrospectively to a pre-assignment intrabar low. Subsequent complete bars keep normal OHLC
  behavior; the Trading monitor replays from its recorded assignment high and boundary.
- Live portfolio candidate confirmation is cleared on reassignment. New policy observations mature
  from fresh continuity; valid judgments for another unchanged policy remain usable.
- Existing daily-loss and capital-review holds remain latched until the existing reviewed resume flow.
  Changing settings does not erase a loss, reset the high-water mark or acknowledge a required review.
- Cash, quantities, cost basis, entry price/time, observed highs, realized history, immutable fills,
  quotes and marks are preserved. A dedicated audited Live revision changes its governing policy;
  ordinary worker revisions still reject any attempted policy change.
- Manual Live reassignment pauses the existing research promotion/boundary machinery, including queued
  promotion. Mixed-policy history must not count as an untouched comparison. Resuming automatic research
  needs a separately reviewed fresh comparison; returning to a familiar version alone does not repair it.
- Frozen session reads remain historical. The live host uses a separate operational context containing
  the current assignments; unchanged control portfolios retain their own policy. Research policy-stability
  checks consult the dated assignment audit instead of altering historical host-coverage facts.

## Persistence and rollout

Migration: `TraderDB/Migrations/20260907_027_AddCentralSettings.sql`.
It adds two tables and immutable triggers, plus an assignment-authority reference on Live portfolio
revisions. It does not seed an assignment, change account balances, retrain models or alter active rules.
It runs in a transaction; use SQLCMD with error stopping enabled. The SQL project definitions match it.

Before applying: create and verify a uniquely named full backup using the existing database-operations
procedure, capture the existing table counts, and stop any old TraderVI hosts. Old binaries do not
participate in the new settings fence and must not run alongside an enabled Settings writer. Build/launch
the updated application only after the reviewed migration succeeds. No DACPAC deployment is involved.

Without migration 027, existing engines retain their defaults. The central page permits reference and
template browsing but disables new-family Save/Assign. Daily settings continue to use installed migration
026. General preferences are stored under the current user's local TraderVI settings directory.

After rollout, verify zero seeded settings assignments and unchanged preexisting account/evidence
tables. A first real strategy assignment remains a separate explicit operator action in the UI.
Rollback of a strategy means assigning a saved earlier version; do not delete assignment history.
Do not revert to an old binary after assignments exist without a reviewed compatibility rollback.

## Validation

- Core tests: 699 passing, including round trips, invalid settings, checksum rejection, preserved facts,
  floor recalculation, pending-action supersession, tracked cutover causality and immediate Live protection.
- WPF and Delphi Debug/Release builds pass; SQL/SSDT project builds successfully and produces a
  reference DACPAC. The final whitespace check passes. These are affected-project checks, not a
  claim that every project in the solution was rebuilt.
- Five isolated UI layouts render without binding errors, including a compact layout and assignment
  review. The editor rejects unsaved changes while accepting equivalent decimal formatting. The
  read-only installed catalog returned five targets and confirmed that migration 027 is not installed.
- Existing compiler warnings remain. Dependency advisories are separate: System.Data.SqlClient 4.8.1
  (moderate/high), System.DirectoryServices.Protocols 5.0.0 (moderate), System.Drawing.Common 5.0.0
  (critical), System.Security.Cryptography.Xml 5.0.0 (moderate).
- No database migration, real assignment, model training, market-service call or trading application
  launch was performed during source validation. New work remains uncommitted; the earlier requested
  checkpoint is `6868299`.

## Authorized operational rollout — 2026-09-07

The operator explicitly requested the database migration, backups and necessary rollout work. No older
operational hosts were running. The standard full backup completed with checksums, RESTORE VERIFYONLY,
HasBackupChecksums=1 and IsDamaged=0. The uniquely named generation is
`TraderDB_FULL_20260907_010638_814.bak` (43,702,272 bytes); the staging and approved OneDrive-folder copies
have SHA-256 `2BC87C73B99DFA5C2ABE83F2643CFD3E26B31BE4EF5B7B469A0FB3ACB8B57C3D`.
Cloud synchronization is not independently verified. No backup was overwritten or removed.

The first migration attempt failed at SQL batch compilation: its new constraint referenced the column
added in that same batch. SQL rolled back the entire transaction; both new tables and the new column
were confirmed absent, and preservation checks matched. A GO boundary after ADD SettingsAssignmentId
corrected the script. The SQL/SSDT rebuild passed and the second SQLCMD execution committed successfully.

All 66 preexisting table counts match the pre-migration capture. Deterministically ordered SHA-256
snapshots of all prior columns in account, portfolio, position, order, ledger and strategy tables also
match. New tables contain zero versions and zero assignments. New immutable triggers are enabled;
settings/Live-revision foreign keys and checks are enabled and trusted; focused DBCC CHECKCONSTRAINTS
returned no violations. The single active daily strategy is preserved.

The app repository's SELECT-only catalog reports installed=True, zero saved engine versions and five
targets. The updated WPF Release app launched from its Release output and is responsive with the
TraderVI window. Desktop accessibility inspection confirmed the top-level Settings tab and the System
Shadow editor, including Save Version, target selection and Review assignment. Screenshot capture was
unavailable through the desktop capture API; prior isolated layout renders remain the visual QA evidence.
The headless TraderVI Release build also passes (zero warnings in that incremental build). No strategy
was saved or assigned, no portfolio activated, and no training or data backfill was run in this rollout.

Local audit artifacts are in the ignored `artifacts/central-settings-rollout` directory. No DACPAC was
deployed. See the [system map and proposed research refinement](../concepts/settings-system-map.md).
