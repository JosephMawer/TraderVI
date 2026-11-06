# ADR-0128: Windows-owned application launch from ordinary build outputs

- **Status:** Accepted
- **Date:** 2026-11-05
- **Domains:** architecture
- **Related:** [ADR-0103](0103-real-execution-independent-of-paper-research.md), [ADR-0111](0111-independent-collector-and-recovered-confirmation.md)

## Context

The three applications stopped around a Codex sandbox-service update. The immediate problem is
application lifetime depending on the development tool that launched it; the parent problem is
operational hosting. The root goal is continuous collection and approved trading with an independently
usable dashboard. The operator requests independence from Codex and ordinary source build outputs,
without accumulating copied source/runtime workspaces. A build folder and a process sandbox are
separate concerns: launching an ordinary executable through a developer tool still gives it that
tool's process ancestry.

## Decision

Register separate on-demand Windows tasks for Engine, Collector and Dashboard, using the signed-in
operator's interactive Windows token with limited privileges. Use Windows' installed PowerShell,
not a runtime bundled with Codex. Keep each task active for its application's lifetime. Configure no
execution time limit, no battery stop, no schedule/sign-in trigger and no overlapping task instances.
The collector retains its bounded three-restart supervisor and complete package hash validation.
The engine and dashboard do not restart automatically. Existing host ownership checks remain.

Launch the existing engine/dashboard from `TraderVI.WPF/bin/Release/net10.0-windows` and the collector
from `TraderVI.Collector/bin/Release/net10.0`. Launch performs no compilation or copying. Explicit
collector rebuilds require stopped-host manifest renewal. Keep launcher configuration and a single
overwritten lifecycle receipt per application in `%LOCALAPPDATA%/TraderVI/Launchers`. Keep existing
collector configuration/control paths until a separately verified move, preserving deliberate stops.

This changes process hosting only. Preserve the installed engine build identity, existing financial
permissions, protected state, recovery checks and provider contracts. Installation evidence belongs
in [project status](../project-status.md); commands belong in [the running guide](../running.md#launch-independently-of-codex).

## Alternatives and limits

Direct launch from the operator's own terminal also avoids Codex ancestry, but Windows tasks provide
consistent commands and visible task ownership. Continued launch with Codex `Start-Process` does not
provide the requested independence. Windows services would introduce different desktop/credential
requirements and are unnecessary for this signed-in-user setup. This does not provide operation while
Windows is shut down, asleep or signed out, nor automatic recovery after reboot. Do not terminate
Codex's service as a verification experiment; inspect task settings, process ancestry and host health,
and distinguish that verification from observing a later real Codex update.

## Review questions

1. Why does running from `bin/Release` alone not establish independence from Codex?
2. Which checks and deliberate-stop behavior must survive a collector rebuild?
