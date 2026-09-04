# ADR-0049: Canonical database write contracts

- **Status:** Accepted
- **Date:** 2026-09-03
- **Domains:** architecture, data-pipeline, risk-management
- **Related:** ADR-0018, ADR-0039, ADR-0043, ADR-0048

## Context

The immediate problem is that several runtime repositories depend on database behavior that the canonical
SSDT project does not declare. The largest example is `DailyBars`: runtime inserts omit `Id` and upsert by
`(Symbol, Date)`, while the project omitted both the identity property and the unique natural key. The live
database already contains those safeguards. Similar drift exists for required defaults, foreign keys,
checks, and indexes used by Symbols, Quotes, Delphi publication, model metadata, and tracked positions.

The parent problem is that a successful application build cannot establish that a newly created or reviewed
database would satisfy runtime write assumptions. The root goal is to keep advisory evidence and Real-trade
records trustworthy without treating a DACPAC as a deployment mechanism.

Read-only inspection found no duplicate `DailyBars` natural keys, no orphan Quotes or tracked-position
references, and no invalid Leadership counts or benchmark prices. It also found that the existing
`FK_Quotes_Symbols` relationship is enabled but untrusted, meaning SQL Server enforces new writes but has
not certified all older rows.

## Decision

1. Treat `TraderDB/dbo/Tables` and `TraderDB/dbo/Indexes` as the complete canonical representation of
   runtime-required identities, defaults, relationships, checks, and indexes. Keep top-level legacy SQL
   files non-authoritative.
2. Restore the live identity/default/index/foreign-key semantics omitted from the project, including the
   `DailyBars` identity and `(Symbol, Date)` uniqueness rule, Symbols/Quotes writer defaults, tracked-position
   relationships, Delphi publication relationships, and existing lookup indexes.
3. Make repository write methods name their intended table columns. Do not use positional `INSERT` or an
   obsolete table contract. Symbol insertion relies only on explicit canonical defaults for `IsActive`,
   `CreatedUtc`, `SecurityType`, and the leveraged/inverse flag.
4. Validate Leadership NHNL counts at the repository boundary: counts are nonnegative and cannot exceed
   `IssuesTraded`. Optional benchmark prices must be positive when present. Partial benchmark availability
   remains valid; the two sources need not arrive together.
5. Add the same Leadership invariants as trusted database checks through guarded manual migration 018, and
   revalidate the existing zero-orphan Quotes relationship so its foreign key becomes trusted. The migration
   changes no rows and refuses unexpected or invalid state.
6. Continue ADR-0018's operating rule: build the SQL project for validation, but apply migration 018 only
   after a fresh verified backup, script review, and separate explicit authorization. Never publish the
   DACPAC.
7. Keep performance-only index cleanup out of this reconciliation. The project mirrors currently relied-on
   live indexes even where a later measured review might find redundancy.

## Alternatives considered

- **Leave the live database as the undocumented source of truth.** Rejected because a future rebuild or
  schema review would silently omit behavior required by repository writes.
- **Generate and publish a schema comparison.** Rejected because broad DACPAC reconciliation remains unsafe
  and is prohibited by ADR-0018.
- **Change only repository validation.** Rejected because another writer or manual correction could bypass
  it; important stored facts also need database checks.
- **Require both leadership benchmark prices together.** Rejected because they are independent sources.
  Missing one is explicit degraded coverage, not an invalid observation.

## Consequences

**Easier:**

- SSDT builds now validate the schema behavior on which active writers rely.
- `DailyBars` cannot silently admit duplicate natural sessions in a canonical deployment.
- Symbol/quote and tracked-position relationships are explicit and reviewable.
- Invalid Leadership denominators, counts, or prices fail before they become decision evidence.

**Harder:**

- Future changes to these contracts retain the same manual backup, authorization, migration, and verification
  boundary.
- The canonical project intentionally retains some existing indexes pending a separate measured cleanup.

## Implementation status

Migration 018 was manually applied and verified on 2026-09-03 after a fresh checksum-verified full backup
and hash-matched OneDrive copy. It preserved 117 Leadership rows and 462 Quote rows, found zero invalid
Leadership rows and zero Quote orphans, and left both new checks plus `FK_Quotes_Symbols` enabled and trusted.
No DACPAC was deployed.

**Would tell us this was wrong:**

- A repository legitimately needs to persist a new-high/new-low count greater than its stated denominator,
  or a non-positive benchmark price. That would indicate the stored field semantics are wrong and require a
  new decision rather than weakening the check silently.

## Review questions

1. Why does `DailyBars.Id` need to be an identity when the repository upsert does not supply it?
2. Why is `(Symbol, Date)` a database constraint rather than only a MERGE predicate?
3. Why does migration 018 trust the Quotes foreign key only after proving there are no orphans?
4. Why can one Leadership benchmark price be null while the other is present?
