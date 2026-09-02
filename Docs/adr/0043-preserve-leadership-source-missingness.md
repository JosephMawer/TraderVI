# ADR-0043: Preserve leadership-source missingness

- **Status:** Accepted
- **Date:** 2026-09-02
- **Domains:** data-pipeline, data-sources, decision-engine, technical-indicators
- **Related:** ADR-0006, ADR-0020, ADR-0041, ADR-0042

## Context

The immediate problem is that Hermes represents an unavailable or empty TMX market-movers response as
`0` active advancers, `0` active decliners, and a `0` basket size. Historical rows that predate live
mover collection use the same values. The database cannot represent absence because all three fields are
non-null, and `LeadershipSnapshot.ActiveBreadthRaw` turns the zero denominator into numeric `0.0`.
`LeadershipCalculator` then smooths that artificial value and can count unavailable or flat inputs as
falling votes.

The parent problem is that Delphi checks total leadership-row depth rather than source coverage. Twelve
NHNL rows can therefore make Leadership #7-#10 and Light Volume #25-#28 appear evaluable even when the
active-breadth layer is not observed. Those signals feed the Granville composite and gate diagnostics,
so a source outage can masquerade as market evidence.

The root goal is to keep TraderVI's recommendations attributable to observed facts as the system grows:
absence must survive acquisition, persistence, calculation, and presentation without silently acquiring
a market meaning.

## Decision

1. Make `LeadershipData.ActiveAdvancers`, `ActiveDecliners`, and `ActiveN` nullable as one atomic
   observation. Enforce either all three null, or all three present with non-negative counts, `N > 0`,
   and `advancers + decliners <= N`. A reported basket with equal advancers and decliners remains a
   genuine numeric zero; an unreported basket is null.
2. In guarded manual migration 017, convert only the exact legacy sentinel `(0, 0, 0)` to null. Refuse
   the migration if another partial or invalid combination exists rather than guessing its meaning.
3. Make Hermes record null when the movers call fails or returns no usable basket. A usable top-50
   response contains exactly 50 distinct, nonblank symbols and an explicitly present price change for
   every row. Because the payload has no as-of date, attach it only to a computed session whose date is
   the current local market date; a retry may repair that same date but must never infer that today's
   response belongs to yesterday. Require the XIU bar to match the same date, and preserve an already
   stored valid observation when a retry has no source data.
4. Evaluate active-breadth leadership only from the newest contiguous observed suffix. The version-1
   requirement is 12 sessions (`EMA10 + two slope observations`). Do not filter gaps out and compress
   non-adjacent observations into a synthetic series.
5. Treat rising, falling, flat, and unavailable as distinct states. Flat and unavailable sources cast no
   falling vote. When the required active-breadth window is absent, Leadership #7-#10 and Light Volume
   #25-#28 return an explicit neutral/no-data result.
6. Report total leadership history, contiguous active-breadth coverage, and required coverage in Delphi
   diagnostics, the human summary, and the WPF presentation so degradation is visible to an operator.
7. Create fixed strategy identity `2BD1A7D0-D144-4A7B-9FA4-49606AB7E963` /
   `v3.2-leadership-missingness` when migration 017 is applied. Clone every threshold and model mapping
   from the sole active `v3.1-rs-date-aligned` predecessor unchanged. Existing runs and outcomes remain
   immutable under their original identities and are excluded from the new active comparative scope.
8. Correct the existing `CK_StrategyVersion_CodeIdentity` implementation while migration 017 already
   holds the strategy boundary. SQL `CHECK` constraints accept `UNKNOWN`, so length checks alone do not
   enforce paired nullability when only one identity field is null. Preflight must reject any existing
   partial identity, then the constraint must require either both fields null or both explicitly non-null
   with valid lengths. This is a database-integrity correction, not a strategy-behavior change.
9. Build the database project only as a schema check. Apply migration 017 manually only after a fresh
   verified and synchronized backup, exact-script review, and separate authorization; never publish the
   DACPAC.

## Alternatives considered

- **Keep `0/0/0` and add a calculator special case.** Rejected because persistence would still erase the
  distinction and every future reader would need to remember an undocumented sentinel.
- **Treat an empty response as genuine zero breadth.** Rejected because a basket of size zero contains no
  market observation. Genuine neutral breadth has a positive reported basket and equal counts.
- **Filter every missing row before calculating the EMA.** Rejected because that compresses time across
  gaps and makes stale, non-contiguous observations look current.
- **Use the current movers response to repair yesterday.** Rejected because the response has no as-of
  date; a next-day attribution would invent provenance even if it often appears plausible.
- **Let unavailable series vote falling as a conservative default.** Rejected because caution should be
  expressed as no-data/degradation, not fabricated directional evidence.
- **Reuse `v3.1-rs-date-aligned`.** Rejected because the correction can change Granville scores and gates;
  pooling both behaviors would violate ADR-0042's strategy/code identity boundary.

## Consequences

**Easier:**

- Source outages and historical unavailability are explicit at every boundary.
- A true zero active-breadth reading remains usable and testable.
- Gaps cannot be hidden by list filtering, and operators can see exactly why a signal degraded.
- Future leadership sources have a concrete observation-validity pattern to follow.

**Harder:**

- Leadership and light-volume signals remain neutral until 12 consecutive eligible Hermes observations
  accumulate after a gap.
- Runtime writers require migration 017 before the nullable contract can be used safely.
- The corrected official evidence scope starts without borrowing performance from earlier identities.

**Would tell us this was wrong:**

- Provider documentation and captured payloads establish a different explicit semantic for a valid
  zero-size basket. That evidence would require a new source contract rather than silently changing this
  one.

## Operational rollout

The rollout required Hermes, Delphi, and Athena to remain paused until a fresh verified full backup and
synchronized secondary copy were confirmed, separate authorization was obtained, and migration 017's
nullable columns, constraints, normalization, row preservation, exact v3.2 identity, thresholds, and model
mappings were verified.

After successful migration, run Hermes once after market close and inspect the newest
`LeadershipData` observation and backup result. Run Delphi only if a deliberate official recommendation
should start the new strategy cohort. Athena does not collect leadership data and should wait until new
eligible outcomes have matured.

Migration 017 was separately authorized, applied, and verified on 2026-09-02 after
`TraderDB_FULL_20260902_012154_689.bak` passed checksum verification and its 37,497,344-byte staging and
OneDrive copies matched SHA-256 `D74FF2F3F3B18AED0C8C72BCCB99D6214497B7BFA3738B6E9420B7FA5EACF658`;
the operator confirmed synchronization. Postflight found 101 exact sentinels normalized, all 115 leadership
rows preserved, both constraints enabled/trusted/non-replication, sole active v3.2, inactive v3.1, zero
threshold/model differences, zero v3.2 runs, and all calibration counts/references unchanged. No application,
external-service, training, outcome, publication, or artifact workflow ran.

## Review questions

1. How does a genuine zero active-breadth observation differ from missing mover data?
2. Why must active-breadth coverage be contiguous rather than merely contain 12 non-null rows?
3. Why do flat and unavailable inputs cast no falling vote?
4. Why does this correction require a new strategy identity when no weight or threshold changes?
