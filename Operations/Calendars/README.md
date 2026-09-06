# Reviewed TSX calendar installation

Installed on 2026-09-06 for the current Windows user:

- Calendar: `tsx-2026-through-20261223-v1.json`.
- Provenance and checksum: `tsx-2026-through-20261223-v1.review.json`.
- User environment variable: `TRADERVI_TSX_CALENDAR_PATH`.
- Installed absolute path: `C:\src\TraderVI\Operations\Calendars\tsx-2026-through-20261223-v1.json`.
- Coverage: 2026-01-01 through 2026-12-23 inclusive, containing 247 full sessions.

The explicit dates use TMX's [2026 calendar](https://www.tsx.com/en/trading/calendars-and-trading-hours/calendar)
and [regular trading hours](https://www.tsx.com/en/trading/calendars-and-trading-hours/trading-hours).
The [settlement schedule](https://www.tsx.com/en/resource/3407/) distinguishes banking and U.S. settlement
holidays from exchange closures. TMX's [Labour Day notice](https://www.tsx.com/en/news?id=1185) separately
confirms the next session is Tuesday, September 8, following Friday, September 4.

The review used indexed text of these official publications because direct fetches returned HTTP 403.
No market-data API or database was used. Eight focused tests passed using the actual Core calendar
loader, including holiday distinctions, Toronto daylight saving, prior-session history and coverage bounds.

Start WPF from a process that has received the new user environment. If Visual Studio, a terminal or
another launcher was already open when this was installed, restart that launcher first. No application
was launched by installation, and no collection or simulation was activated. If the repository is moved,
update the user variable to the new absolute calendar path.

## Coverage boundary

December 24 has a 13:00 TSX close. The frozen V1 host assumes a full 09:30–16:00 session, so this snapshot
ends on December 23. December 24 is outside coverage, not a holiday to skip. Do not extend the date list
across it without reviewing short-session collection, protective exits, marks, daily history and outcomes.
There are 76 listed sessions from September 8 through December 23. December 16 is the latest anchor
with five subsequent sessions fully inside this snapshot; later outcomes need extended reviewed coverage.

Out-of-coverage requests fail. Resolve the short-session limitation before December 24, including
protection for positions carried into that date. New official closure notices also require a reviewed,
versioned replacement. Runtime does not fetch or infer calendar updates.

Validation emitted existing compiler warnings and dependency advisories, including the critical
`System.Drawing.Common` 5.0.0 advisory; no package version was changed or dependency audit rerun here.
