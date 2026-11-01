# ADR-0118: Daily Delphi results in the desktop

- **Status:** Accepted; source implementation, not installed
- **Date:** 2026-11-01
- **Domains:** architecture, user-interface
- **Supersedes:** ADR-0034 desktop execution controls and ADR-0060 immediate daily reevaluation only

## Decision

The desktop displays saved daily Delphi results. Remove both the official-run button and the
automatic Delphi run after daily strategy assignment. The Delphi CLI remains the nightly
execution host. Opening or refreshing the desktop result view reads persisted recommendations
and their matching presentation evidence.

The immediate problem is two desktop paths into daily evaluation. The parent problem is separating
nightly application dependencies from the shared Core assembly. The root goal is reliable nightly
processing with bounded build storage and an understandable development/release workflow.

Daily strategy Save and Assign remain available with their existing verification, selection lock,
paused/empty-family guards and immutable version identity. Assign changes the active selection;
the next nightly Delphi run produces new results. The confirmation and completion text explain
this wait and keep buying paused. Existing saved results retain their actual dates and strategy
identity; assigning a version does not relabel them as freshly evaluated.

Live, Shadow and Trading settings reevaluation and the separate recorded-fill/Ghost-position
actions retain their existing behavior. They are not daily Delphi generation. The Core assembly
identity recorded by daily settings remains the same module identity, now obtained from
DelphiSettingsChange rather than DelphiWorkflow.

## Consequences and limits

- WPF has no direct DelphiWorkflow execution or code-identity reference. The saved-result reader
  and presentation types remain shared with the CLI.
- Closing the desktop no longer needs a daily-run guard; an active settings operation still
  blocks closure.
- Removing these callers does not itself split Core or reduce a nightly package. Dependency
  extraction is the next investigation in the [release design](../reviews/nightly-precompiled-releases-20261101.md).
- The existing scheduled pipeline and authorized CLI workflow remain available. A future
  desktop action for the entire pipeline is a separate unresolved design choice.
- Compilation and local unit tests do not install a release or exercise SQL, models or the
  nightly workflow. Existing collector, engine and desktop outputs are preserved.

## Alternatives considered

Keeping only the manual button would retain a desktop execution dependency the operator does
not need. Removing only that button would leave the settings path running Delphi implicitly.
Launching the CLI automatically from settings would preserve the unwanted behavior and is rejected.

## Review questions

1. When does a newly assigned daily strategy first produce new results?
2. Which shared code does the desktop still need to display saved Delphi results?
3. Why does removing these execution paths not yet isolate the nightly builds from Core?
