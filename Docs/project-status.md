# TraderVI Project Status

Last reconciled **November 5, 2026** from existing records. This is dated evidence, not a live health
check or authority to trade. [Roadmap](roadmap.md) owns priority; the
[execution checklist](strategy-alignment-implementation-checklist.md) and
[Live checklist](delphi-live-implementation-checklist.md) own acceptance and dependencies.
[Design rules](design-rules.md) and accepted ADRs govern behavior.

## Operating state and immediate risks

- **Hosts:** On November 5 at 21:13 Toronto all three applications were stopped. A Codex service update
  coincided with the last collector receipt; shared launch ancestry is a plausible cause, but individual
  process termination causes were not recorded. The operator authorized restart at 21:17. At 21:18 the
  engine's 15 workers reported zero consecutive failures, execution was waiting for regular hours, the
  collector reported OutsideSession and the dashboard showed broker connected. Startup does not prove
  the next session or survival of another service shutdown. Private evidence: `artifacts/startup-20261105-2116/`.
  [ADR-0128](adr/0128-windows-owned-application-launch.md) accepts Windows-owned on-demand launch;
  its [procedure](running.md#launch-independently-of-codex) distinguishes installation from starting hosts.
- **Real execution and protection:** November 4's installed [issue #24 repair](reviews/verified-symbol-mapping-20261104.md#live-transition)
  reconciled the original filled buy, preserved saved protection and passed operator build review and
  resume. Fresh bids and matching protection were observed for all four holdings; one ordinary protective
  sale filled and reconciled. Prospective new-holding and sustained protection acceptance remain open.
  The October 30 bid failure was placed on hold; these later observations qualify that historical finding
  but do not establish uninterrupted future protection. [Earlier evidence](project-status-history-20261101.md#october-30--held-position-bid-protection-gap-verified-1003-toronto).
  November 3 approved [reinvestment terms](adr/0125-reusable-strategy-sale-proceeds.md) permit reuse of
  reconciled net sale proceeds; a live buy under those terms had not occurred at the 11:59 check.
- **Paper, shared inputs and recovery:** The November 3 Paper recovery completed and its scoped hold was
  cleared; older failed receipts remain immutable. On November 4 the Paper checkpoint package and
  baseline were installed; normal saved-state restart and an empty incremental verification passed.
  Nonempty overnight delta and market-session acceptance remain open under
  [ADR-0127](adr/0127-paper-checkpoint-restart-and-incremental-verification.md).
  At 15:29 November 4 shared publication remained at 10:20 despite fresh collector observations, blocking
  new-entry readiness; on November 5 at 09:11 the latest saved publication had reached November 4's close.
  That pre-open read does not prove fresh November 5 market-hour Paper/Real evaluation.
  [Paper installation evidence](reviews/paper-restart-copy-rehearsal-20261104.md#live-installation-and-rehearsal-cleanup),
  [mapping transition](reviews/verified-symbol-mapping-20261104.md#live-transition).
- **Collector and minute data:** The independent collector and latest-minute policy are installed; the
  November 4 unexplained collector exit was followed by an explicitly authorized restart. The November 5
  evening restart is the later host checkpoint above. Source coverage, capacity, venue and native recovery
  remain to be qualified. The approved Real strategy remains five-minute; the one-minute publisher was
  staged, and adoption requires separate review. [Acceptance](strategy-alignment-implementation-checklist.md#independent-collector-and-recovered-confirmation--october-27-overnight).
- **Nightly:** The November 5 scheduled 06:30–06:37 run completed all four stages with overall Attention:
  isolated Hermes source warnings, one DataAudit error and ten warnings, and two invalid Athena prediction
  outcomes. SQL verified one valid November 5 Delphi preparation using November 4 data. At 09:10 the task
  was Ready with zero missed runs. Selected existing output directories are runtime dependencies; protect
  them during cleanup. [ADR-0122](adr/0122-existing-output-nightly-execution.md), private evidence in
  `artifacts/morning-readiness/2026-11-05/manual-readiness/`.

## Evidence and development

- **Dashboard:** The November 3 holdings-refresh repair was installed; the original trade-synchronization
  failure remains undiagnosed. Later dashboard-only updates added the holdings grid, strategy link and
  Engine health view. Native accessibility checks passed in their stated scopes; some clicks, clipboard
  behavior and pixel layout remain unverified. [Health contract](adr/0126-engine-health-and-diagnostic-snapshot.md).
  November 5 updates installed a separate Trade history tab and market-context panels. Sector classifications
  and 11 indices were visible; the after-close feed checks do not prove intraday data delay, and automatic
  intraday refresh was off at that checkpoint. [Market-context evidence](reviews/trading-market-context-20261105.md)
  and [remaining acceptance](strategy-alignment-implementation-checklist.md#main-account-market-context--november-5).
- **Edge passkey sign-in:** The operator confirmed the November 2 initial Edge handoff; the engine reported
  Connected. Subsequent live refresh was unverified at that checkpoint. [Evidence](reviews/wealthsimple-auth-and-engine-diagnosis-20261102.md),
  [ADR-0123](adr/0123-edge-passkey-session-handoff.md).
- **Prediction scorecards:** The November 3 desktop showed Ready for four models and 99.97% usable coverage
  among predictions due for ten-session grading. Outcomes still inside the window are shown separately;
  delayed-intraday outcomes have a separate evidence gap. November 5's nightly report recorded two invalid
  outcomes. [ADR-0124](adr/0124-due-prediction-scorecard.md); the
  [November 1 baseline](project-status-history-20261101.md#november-1--athena-prediction-scorecards-and-the-ten-session-window)
  used the earlier denominator rule.
- **Paper profit protection:** Progressive protection was installed and enabled October 18 for all 15
  then-active paper portfolios, preserving existing holding state. Off-session checks did not establish
  regular-session touch behavior or upstream bid freshness. [ADR-0090](adr/0090-progressive-individual-profit-trailing.md).
- **Build and storage:** DataAudit, Hermes and Athena build independently of Core under ADRs
  [0119](adr/0119-independent-data-audit-build.md), [0120](adr/0120-independent-hermes-build.md) and
  [0121](adr/0121-independent-athena-build.md). Source/build tests are not installation evidence.
  Receipt compression migration 056 was installed November 1 after a verified synchronized backup;
  a separate test-database restore drill remains outstanding. [Conversion evidence](reviews/sql-cleanup-inventory-20261031.md#production-conversion--november-1).

## Remaining qualification

Preserve the distinction between source implementation, tests, installation and operational evidence.
The checklists retain remaining protection, Paper/Real market-hour, source/valuation, order/recovery and
comparable-strategy acceptance. AI Momentum is not an activated operating portfolio. Review short-session
calendar and carried-holding protection before **December 24, 2026**. Dependency advisories, schema drift,
sustained memory capacity and the restore drill remain separate reliability work.

## History and maintenance

The complete immediately preceding record, including detailed incident sequences and private evidence
pointers, is in [status history through November 5](project-status-history-20261105.md). Earlier records
remain in the [November 1](project-status-history-20261101.md) and
[September 22](project-status-history-20260922.md) archives. Historical statements do not renew
operational verification. Update affected current entries in place; link detailed evidence and retain its
date, scope and limitations.
