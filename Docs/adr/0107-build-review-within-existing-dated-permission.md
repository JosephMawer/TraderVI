# ADR-0107: Build review within existing dated permission

**Date:** October 23, 2026. **Status:** Accepted within Joseph's explicit request to fix, test and
install the market-input correction and verify live evaluation. **Domains:** architecture, risk-management.

The immediate problem is that installing the corrected source adapter changes Core's hash, while
the existing build-review path accepts only October21. The parent problem is restoring a deliberately
approved real strategy after a software correction. The root goal is working live evaluation with
the operator's exact financial permission and history preserved.

Extend ADR-0098's build-only review to an already approved ADR-0101/0105 dated continuation during
that permission's regular session. This supersedes only its October21 restriction. Require the same
cash-only book, zero orders/positions/protection, no risk/review holds, exact strategy except build,
fresh account/source/storage checks, two-minute review, host off and explicit operator approval.
The original close expiry and unused allowance remain unchanged. No financial renewal is created.

Add an optional exact permission-request identity to the build review. The effective-review projection
applies that build only to its matching dated permission; legacy reviews remain omitted-field compatible
and do not override later continuations. Full append validation preserves the prior approvals, scope,
risk state, memory, modes, order identities and source. Exact lost-acknowledgement recovery remains.
The reviewed build still requires a separate operator host enable. No approval or enable by the agent.

The companion market fix projects an already validated progressive successor into transient source
admission without modifying the saved comparison. Diagnostic input counts and retained worker failure
time/type establish observation, not permission or signal qualification. No trading gate is changed.

## Review questions

1. Why does a build approval bind the existing permission request instead of creating a new dated grant?
2. Why must a previous build review be ignored after another session continuation?
3. Why do fresh shared inputs alone not establish a confirmed signal or broker fill?
