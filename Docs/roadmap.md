# TraderVI Roadmap

Last reconciled **November 1, 2026**. This page owns priority order; [project status](project-status.md)
records dated evidence, the relevant checklists own acceptance and dependencies, and
[design rules](design-rules.md) and accepted ADRs govern behavior. A roadmap item does not authorize
live operations, trading, training, market access or deployment.

## Direction

Compare complete strategies in independent paper portfolios, deliberately approve one for the permanent
TFSA, and manage it with explicit risk, protection and execution controls. Recommendation approval and
permission for automatic trading are distinct ([ADR-0055](adr/0055-independent-strategies-to-approved-live-execution.md),
[ADR-0062](adr/0062-complete-strategy-recommendation-promotion.md)). Support ongoing management and
intentional handover without losing existing protection or financial history.

## Now — protect and qualify the operating system

1. **Reduce regular trade-evaluation history work** ([issue #19](https://github.com/JosephMawer/TraderVI/issues/19)).
   The November 1 instruction places this ahead of previously queued engineering: start with the current
   account and original unfinished orders, measure SQL and broker calls against existing checks, and
   preserve risk and protection decisions. Keep the approved running release available through Monday's
   open, the collector operating, trading settings unchanged and the held-position bid investigation on
   hold. [ADR-0117](adr/0117-compact-trading-current-storage.md) and the
   [restart review](reviews/restart-recovery-design-20261030.md) own the boundary and proposals.
2. **Resolve known reliability work in its existing scope.** The identified Paper-input symbol-resolution
   gap is planned under [issue #24](https://github.com/JosephMawer/TraderVI/issues/24), including real
   service wiring and verified identity, without arbitrary suffix stripping. Prospective native new-holding
   acceptance remains in issue #20. Safe execution-error cause logging is planned under
   [issue #23](https://github.com/JosephMawer/TraderVI/issues/23), preserving retries and trading behavior.
   Both were planned, not established as installed, at the last roadmap checkpoint. Keep hosts running;
   intervene sooner only for an actual engine failure, stuck execution or trading block.
3. **Qualify protection, recovery and market inputs.** The held-position broker bid-read failure is an
   unresolved protection gap; its investigation is on operator hold. Do not claim reliable automatic
   selling without restored and verified bid-based protection. Resolve retained Paper recovery separately.
   Qualify collector coverage, capacity, venue and native recovery before proposing one-minute financial
   adoption; the approved Real strategy remains five-minute. Preserve existing gates, indicator windows,
   VWAP and protection. [Dated state](project-status.md),
   [collector acceptance](strategy-alignment-implementation-checklist.md#independent-collector-and-recovered-confirmation--october-27-overnight).
4. **Complete real-account and execution qualification in dependency order.** Verify cash-event timing,
   account risk inputs, native order/reconciliation and recovery, broader strategy-family handover and
   replacement, and sustained host operation. Keep financial approval and enablement separate from
   implementation. The [execution checklist](strategy-alignment-implementation-checklist.md#left-to-do--development-order)
   owns the detailed sequence and outstanding boundaries.
5. **Establish trustworthy comparisons.** Accumulate mature outcomes and comparable strategy histories;
   verify leadership continuity, coverage, causal fills, risk exits and clean Shadow sessions before
   performance conclusions or promotion. Observe the installed individual-stock profit protection in
   normal sessions; synthetic studies and off-session checks do not establish touch behavior or bid
   freshness. Follow the [calibration checklist](calibration-implementation-checklist.md),
   [Live checklist](delphi-live-implementation-checklist.md) and [ADR-0090](adr/0090-progressive-individual-profit-trailing.md).

### Reliability and correctness prerequisites

- Complete a documented restore to a separate test database; continue backup synchronization checks.
- Resolve short-session behavior and reviewed calendar coverage, including carried-holding protection,
  before **December 24, 2026**.
- Qualify sustained memory/capacity and bound account-history reads and displays without weakening
  freshness, immutable history or fingerprint checks. Reconcile schema drift through reviewed migrations;
  continue universe/listing checks.
- Address dependency advisories, broaden CI host coverage and protect consequential architectural
  boundaries before broader refactoring. The [audit register](strategy-alignment-implementation-checklist.md#remaining-audit-work)
  retains the detailed work.

## Next — research and supporting capabilities

- **AI Momentum:** Continue the visible reviewer, then a complete tested paper trader and supervised
  live qualification in the [delivery sequence](strategy-alignment-implementation-checklist.md#ai-momentum-delivery-sequence).
  Reliable invested-account valuation, cash-event coverage, integration and protective host qualification
  remain dependencies. Preserve stale-input blocks and the accepted budget/risk boundaries in
  [ADR-0087](adr/0087-ai-momentum-portfolio-boundaries.md); paid tests and activation need authorization.
- **Practice:** Extend checkpoint resume, isolated recovery/load and failure trials, Live replay and
  historical qualification. The exploratory 20-session trial is complete; retain its data and universe
  limitations. [Scope](reviews/open-questions.md#historical-practice-environment--follow-up-milestones).
- **Data and models:** Backfill relative-strength features with coverage diagnostics and compare raw and
  Z-score ranks before any executed-lens change. Audit enabled model-registry entries; gather OBV, CLX
  and Granville evidence before changing weights. Keep the individual-stock gain-retention research goal
  visible ([scope](reviews/open-questions.md#intraday-gain-retention-revisit-soon)).
- **Hosting and product:** Qualify unattended pre-session hosting and sustained native operation; the
  independent engine itself is already installed. Consider Scorecards charts after enough mature cohorts,
  Hermes/System Log surfaces, Oracle Phase 3, later indicator groups and broader walk-forward simulation.
  [Operational follow-through](strategy-alignment-implementation-checklist.md#operational-follow-through).

## Explicit holds and deferrals

- The broker bid investigation remains on hold. The October 30 below-one-share sizing decision was
  scheduled for a later read-only investigation; consult its follow-up before assuming an outcome.
- Exceptional damaged-database/old-backup recovery design is deferred to
  [issue #25](https://github.com/JosephMawer/TraderVI/issues/25); separate SQL cleanup is tracked in
  [issue #26](https://github.com/JosephMawer/TraderVI/issues/26). Neither blocks the current restart increment.
- Buy-at-open, same-stock manual additions, morning/noon trailing alternatives and the limited AI quote
  fallback remain deferred. Dullness requires better history or universe; CLX gating and executed RS
  Z-score require their respective evidence and decisions. New ML features, thresholds or models require
  design-rule review and an ADR for behavioral changes.
- Recurring artifact retention is unresolved despite completed one-time cleanups; automatic SQL shrinking
  remains off. A separate Sentinel broker workflow would require its own decision and controls.
  [Open questions](reviews/open-questions.md) retains the broader unresolved scope.

## History and maintenance

The complete former roadmap is preserved in [history through November 1](roadmap-history-20261101.md),
with the earlier [September 22 snapshot](roadmap-history-20260922.md). Historical priorities and old
operational claims are not current authority. Update this page when order or scope changes; put evidence
in status, detailed acceptance in checklists, and decisions in ADRs or open questions.
