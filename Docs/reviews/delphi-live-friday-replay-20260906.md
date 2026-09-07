# Delphi Live Friday replay — 2026-09-06

The operator authorized a September 4 replay with both signal/ranking changes and an independent
simulated account, using the saved System Shadow account balance. This implements
[ADR-0054](../adr/0054-delphi-live-preview-and-historical-replay.md), without changing frozen V1.

## Source and result

- Selected the valid OfficialPaper daily run available before Friday's 09:30 Toronto opening cutoff,
  with market-data date September 3. Each lens published 25 picks; overlap yields 28 displayed symbols,
  of which 23 are eligible under the existing daily filter. Published blocked rows remain visible.
- Requested historical five-minute and one-minute bars for those 28 symbols plus XIU, with a maximum
  of 58 sequential logical TMX requests, 30 seconds per request and ten minutes overall. The shared
  transport permits up to three attempts per request. Completed symbol inputs are cached locally.
- Produced 78 checkpoint frames. Coverage is 2,112/2,262 five-minute bars and 7,329/11,310 minute bars.
  Missing intervals remain missing; the report does not infer their prices or trading activity.
- Recorded six estimated fills: five buys and one sell, leaving four open holdings. Account amounts,
  symbol-level trades and raw market data remain in the ignored local artifacts.
- Report: `artifacts/delphi-live-replays/20260906-164031-7881053-report.json`.
  SHA-256: `5CBADCD0E326A981305902F8AF39072E6DBCE922DBB01933CF27C5E58128F253`.
  The matching `-baseline.json` records daily inputs and capital source; the run-specific `-inputs.json`
  cache records actual historical source retrieval times separately from assumed replay timing.

## Interpretation

Five-minute evidence is assumed available two minutes after completion. Protection uses completed
minute closes as bid proxies and fills use exact subsequent minute opens. Zero commissions, spread
and slippage are modeled. Closing holdings are marked, not forcibly sold. Missing original publication
latency and intraminute quote paths prevent a live-execution equivalence claim. This is an explanatory
historical simulation, with **zero clean engineering or promotion cohorts**.

Daily inputs are restricted to prior session dates and rows created by Friday's opening cutoff. The
daily table has no row-revision history: the captured baseline preserves the values read for this replay,
but cannot independently prove that a historical daily price has never subsequently been corrected.

## Validation and operational boundary

- .NET 10.0.400; all 592 Core tests passed, including seven replay regressions covering future-data
  exclusion, exact execution evidence, entry/cash limits, blocked picks and source/bar validation.
- Targeted Sandbox and WPF builds succeeded. Existing compiler warnings remain (Core nullable-context
  warnings and WPF's unused `ex`, among others). Dependency warnings are separate: the Core test build
  reports NU1902/NU1903/NU1904 for existing SqlClient, DirectoryServices.Protocols, Drawing.Common and
  Cryptography.Xml packages. These dependencies were not changed. The full SSDT solution was not rebuilt
  for this UI/replay change; its earlier full-build record remains separate.
- Rendered the actual WPF view and view model from local artifacts in an isolated rendering host, with
  no TraderVI App/dashboard startup, Refresh/Tick calls, timers, SQL or external requests. Inspected
  current picks, replay, account, compact layout and Advanced; checked timeline bounds, absence of future
  trades at the opening frame, and shared Advanced data context.
- The SQL/probe sandbox could not authenticate to local SQL Server. The explicitly approved run and
  read-only postflight used the normal host security context; encryption settings were not weakened.
- Postflight confirmed every `DelphiLive*` operational table remained empty, with only the single policy
  definition present, and the saved Shadow account balance unchanged. Source has no operational write path.
  No migrations, activation, training, broker operations, commits, pushes or PRs were performed.

## Next operational work

The outstanding open-session trial still needs real publication timing, durable SQL receipts and
full-watchlist capacity. This historical replay does not satisfy those checks. Published-but-ineligible
handling differs between the pure frozen-source selector and SQL selector; that pre-existing mismatch
is deferred in [open questions](open-questions.md), with live eligibility unchanged here.
