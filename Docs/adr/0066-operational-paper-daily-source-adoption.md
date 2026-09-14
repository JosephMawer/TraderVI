# ADR-0066 — Operational paper portfolios adopt newer daily publications

Date: 2026-09-09  
Status: Accepted; first implementation milestone authorized by the operator.

The immediate problem is that a missed 09:30 Delphi publication leaves the day's paper sessions
bound to `NoValidDelphiRun`, even after recovery. The parent problem is brittle morning recovery.
The root goal is to observe the portfolio entry, sizing, exit and risk rules prospectively.

## Decision

System Shadow and operational Delphi Live adopt the latest completed, Valid OfficialPaper publication
for today's recommendation date, the immediately preceding reviewed TSX session and the currently
assigned daily strategy. Publication is atomic in the existing calibration repository. The active
strategy retains its immutable model bindings and input policy. This change does not assign a strategy,
train a model or bypass any daily eligibility gate.

Adoption happens under the existing settings lock and serialized paper processing. Delphi Live also
requires its current fenced host lease. Repeated reads and restarts do not repeat an adoption. Older,
future, invalid, exploratory and incompatible publications cannot displace the active source.

The original session source remains intact. Append-only source revisions record the previous and new
run IDs, publication, adoption and effective times. Shadow appends separate candidate tracking rows per
revision, retaining their order references. Live saves a checksum-protected candidate/lens/baseline
snapshot and adds observation membership without rewriting the original frozen rows. Baselines use
completed daily data available at adoption; missing or malformed coverage remains unavailable.

The effective boundary is the next five-minute boundary at or after actual adoption. Entry confirmation
restarts there; observations cannot create retroactive fills. Shadow requires adjacent fresh endpoints.
Live retains opening-session VWAP coverage requirements and restarts its rolling confirmation window.
Unfilled old-source buys expire or cancel before further processing. Cash, holdings, completed fills,
protective sells, loss guards and same-session exit history remain. Symbols already observed remain in
the observation ledger, including removed holdings and protection targets. Adoption does not liquidate.

Historical replay retains its opening cutoff. Operational Live's original opening freeze and research
baselines remain separate from the current source. A `DailySourceChanged` cohort flag permanently
excludes refreshed days from clean opening-source research. New operational rankings are not submitted
as opening-source ranking checkpoints. Research maturation uses the original context. Late starts and
missing opening observations remain disclosed; later data cannot repair operational timeliness.

The UI shows active source publication/adoption/effective times and preserved adoption history. The
ordinary near-close entry cutoff, quote causality, portfolio sizing and all risk controls continue.
WPF now waits for monitor shutdown before closing, so its durable lease can be released before exit.

## Storage and deployment

Manual migration `20260909_032_PaperDailySourceAdoption.sql` adds two revision tables, a nullable
Shadow candidate revision reference and an additive Live observation membership flag. Existing rank
and symbol uniqueness is scoped to the source revision; legacy rows retain the null revision group.
No existing source or trading record is rewritten by the migration. Append-only triggers and source,
time, identity and session constraints guard the new records. Deploy only after a verified checksum
backup, SQL project build, exact-script review and an orderly host stop. Retain the additive schema and
history if application binaries must be reverted; never erase source revisions as a rollback.

## Validation

Focused tests cover monotonic/idempotent adoption, forward effective times, fresh adjacent endpoints,
pending-buy cancellation without a quote, preserved cash/positions/fills/guards, restart confirmation
and exclusion from clean research. SQL rollback rehearsal compares all 17 protected ledger/source
tables. Operational acceptance requires today's corrected publication in all four Shadow sessions and
Live, one revision per session/source, unchanged original freezes, and resumed usable collection.

## Follow-on milestone

The operator also accepted separate normal and portfolio-rules-only paper comparisons. Their explicit
daily gate profile, independent capital and activation are the next milestone. This ADR only changes
source availability. It does not activate those comparison portfolios or broaden the published universe.

## Operational acceptance — September 9

Migration 032 followed a fresh verified backup and rollback rehearsal; all 17 protected tables matched. Explicit datetime2 parameters corrected the initial timestamp rounding rejection. At 11:04:58–59 Toronto, four Shadow sessions and Live adopted the corrected publication, effective 11:05. Repeated refreshes preserved one revision each. The 11:05 cycle completed with nine settled slots and eight usable receipts, with matching ledger cycle. Original frozen evidence remains unchanged. Daily gates admit zero candidates. Full Core suite: 806 passed; final focused suite: 371 passed. Release WPF and SSDT builds passed. Existing compiler and dependency-security warnings remain.
