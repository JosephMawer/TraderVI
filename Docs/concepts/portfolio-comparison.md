# Reading the Portfolios comparison

Portfolios compares paper strategies; Trading manages tracked account activity. The three Portfolios
tabs share a selected account, period and scope. Compare is the landing view, Performance shows how
saved values changed, and Strategy review examines one challenger against the current daily assignment.
This is the implemented UI contract in [ADR-0061](../adr/0061-portfolio-comparison-workspace.md).

## What the numbers mean

**Period return** is ending account value divided by starting account value, minus one, using the same
saved closing dates for every paper account. For example, 10,000 to 10,600 is 6%; another account starting
at 500 and ending at 530 also returned 6%. Paper accounts have fixed capital; manually recorded Real
balances are not converted to comparable returns because deposits and withdrawals are not reconciled.

**Growth of 100** draws those account values relative to a common starting index of 100. An ending
index of 106 represents a 6% return over the displayed window. A missing shared starting value makes
normalization unavailable. Missing internal observations break a line rather than implying a known path.

**Maximum closing decline** is the largest fall from an earlier observed peak within the selected window.
Values of 100, 110, 104.5 give a 5% maximum closing decline even though the final return is positive.
It excludes movement between closing observations. The metric is unavailable when observed dates are
missing for that account, and is never represented as a full intraday risk statistic.

**Observed dates** means dates present in the loaded closing ledgers. It does not establish coverage of
every expected TSX session. At least two saved dates and matching endpoints are needed for return.
Different fill assumptions, history lengths and earlier assignments remain material limitations.

## Promotion boundary

The stored active daily strategy identifies current daily models and gates. Tracked-position exit rules
are separately assigned. Neither uniquely binds the complete recommendation strategy to a paper account.
Review therefore shows that primary performance is unavailable instead of using the Real account or
guessing a Shadow portfolio. It can inspect existing assignments in Settings, but cannot promote a paper
strategy through a common adoption route yet. That remains future ADR-0055 work.

## Implementation and validation (2026-09-07)

- `PortfoliosView.xaml` and its viewmodel comparison partial provide the three linked views.
- `PortfolioHistoryReader` reads saved daily Shadow closes; existing Live snapshots supply closing marks.
- `PortfolioComparison` calculates the descriptive display; `PortfolioPerformanceChart` plots it.
- .NET SDK 10.0.400; targeted WPF build passed. Core suite passed all 720 tests, including seven new
  regressions for aligned dates, gaps, duplicate/invalid observations, zero values and window selection.
- The offscreen `artifacts/portfolio-comparison-preview` harness renders all three pages, smaller windows,
  missing-history and inactive states. It verifies selection/period/scope behavior and zero binding errors.
  It strips operational controls and event handlers; it does not instantiate the operational dashboard.
- Existing compiler warnings remain. Dependency advisories reported by the test build remain separate
  from compilation/test results. No complete solution or SQL/SSDT build was run for this UI change.
- No database migration/read validation, market collection, model training, operational host launch,
  paper activation, actual assignment, trade, commit or publication was performed for this change.

The live installed-data experience and operational hosting have not been exercised by this validation.
Snapshots in the preview folder contain synthetic data only; the application's runtime contains no
fixture performance or pretend primary-account results.
