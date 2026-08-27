# ADR-0032: Live paper-trading WPF dashboard

- **Status:** Accepted
- **Date:** 2026-08-26
- **Domains:** architecture, data-pipeline, risk-management
- **Related:** ADR-0015, ADR-0028, ADR-0030, ADR-0031
- **Extended by:** ADR-0033, which introduces the tabbed shell and changes the
  SQL display-refresh default to thirty seconds.

## Context

The immediate problem is that a console table is useful for diagnostics but
does not provide the live, visually scannable history needed to supervise a
paper portfolio. The parent problem is keeping market evidence, policy state,
positions, alerts, and ghost exits in one application-owned operational loop.
The root goal is to tune Delphi from trustworthy paper evidence without relying
on conversational memory.

The repository already contains `TraderVI.WPF`, but its legacy startup window
contains old Wealthsimple login and order code. Reusing that screen would mix a
paper-monitoring feature with broker-facing behavior and hard-coded local token
paths. The console already provides a safe headless surface for diagnostics.

## Decision

Use `TraderVI.WPF` as the local live paper-trading dashboard, with a new
paper-only startup window. Put monitoring and exit behavior in a shared Core
service used by both WPF and the TraderVI console; never implement policy logic
inside XAML code-behind.

### Confirmed direction

- The application runs the accepted fifteen-minute monitor and updates the
  display as new SQL/source state arrives.
- The dashboard shows active and closed ghost trades, entry/latest/exit prices,
  P/L, high-water/trailing levels, policy directives, poll health, and trade
  history.
- SQL is the durable source for history. The UI can be closed and reopened
  without losing already persisted evidence or trades.
- The dashboard is paper-only and does not read Wealthsimple tokens or expose
  broker buy/sell controls.

### Accepted implementation defaults

- Keep the existing TraderVI console commands for scripting, diagnostics, and
  recovery. ConsoleTables is not required for the primary visual experience.
- Start the shared monitor automatically only during the regular Toronto
  session. Outside market hours, load stored history without calling TMX.
- Align policy polls to the existing quarter-hour-plus-two-minute schedule and
  refresh the visible SQL-backed dashboard every five seconds.
- Add a manual `Poll now` control with an explicit external-call/database-write
  label for supervised diagnostics.
- Make the new paper dashboard the WPF startup window while leaving the legacy
  window in source but unreachable from the default paper workflow.
- Keep process hosting local for version 1. Closing the WPF process, sleeping
  the computer, or losing connectivity stops future polls; the dashboard must
  display that limitation and the last successful receipt time.

## Alternatives considered

- **Continuously redraw a console table.** Retained as a diagnostic option but
  rejected as the primary experience because history, timelines, status cards,
  responsive details, and failure visibility are substantially clearer in a GUI.
- **Create a local ASP.NET web dashboard.** Viable, but rejected for version 1
  because the existing Windows-only WPF project already builds and avoids a
  second local server, browser-launch, and hosting/security surface.
- **Put TMX polling directly in the WPF window.** Rejected because the console
  and GUI could drift into different policy implementations and UI closure
  would make the logic difficult to test.
- **Keep using the legacy WPF main window.** Rejected because it initializes old
  broker/token paths that are unrelated and unsafe for a paper-only monitor.

## Consequences

- The user gets one live application for the visual supervision they requested.
- Policy, persistence, and execution remain testable independently of WPF.
- The first version is not an unattended Windows service. Missed polls caused
  by app or machine downtime remain visible and may be replayed, but receipt
  history cannot be reconstructed for a request that never occurred.
- A later always-on host can reuse the same Core service without rebuilding the
  dashboard or policy.

## Review questions

1. Why does the shared monitor belong outside the WPF window?
2. Why is the legacy Wealthsimple window not the new startup surface?
3. What stops when the WPF process or computer stops in version 1?
