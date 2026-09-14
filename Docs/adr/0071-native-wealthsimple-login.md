---
cssclasses: [executive-brief]
---
# ADR-0071: Native Wealthsimple sign-in, browser verification only when required

- **Status:** Implemented; operator confirmed successful native sign-in on September 9, 2026 (Toronto).
- **Date:** 2026-09-09 (Toronto)
- **Domains:** architecture
- **Supersedes:** ADR-0070's browser-first sign-in interface; protected storage and verified status remain.

> [!summary] Decision and consequence
> TraderVI submits email/password directly and prompts for a verification code when required. The provider's browser login is a conditional fallback for an explicit browser security challenge, not the default interface or a background data transport.

## Context

The immediate problem is authenticating the native application and completing token handoff. The
parent problem is direct broker connectivity. The root goal is background account synchronization
and eventual explicitly authorized order execution. The operator clarified that signing into a
browser dashboard is not the desired product: TraderVI must own the authenticated HTTP client.

The first browser login succeeded at Wealthsimple but failed during native handoff. A real offline
WebView2 probe reproduced `NotImplementedException` when its COM request stream reached the JSON
parser. The original memory-stream tests could not catch this integration failure. This was a client
bug, not an operator mistake or evidence of a mandatory passkey enrollment.

## Decision

The connection window has native email/password inputs and calls the existing direct auth client.
Use the production public OAuth client identifier verified in the downloaded public application
bundle and the earlier capture. This identifier is not a secret. Generate fresh device/session IDs;
never replay captured passwords, OTP values, cookies or security-challenge claims.

An OTP-required response moves the same in-memory attempt to a verification-code prompt. Only an
operator-submitted code triggers the second request. Incorrect codes remain retryable explicitly.
Background connection checks cannot cancel or submit an in-progress OTP. Cancel, window close, or
ten minutes of inactivity expires the attempt and releases credential references. The password field
is cleared immediately after starting the request; email/password/code are never written to storage.
Optional remembered sessions continue using Windows CurrentUser encryption under ADR-0070.

Green requires a fresh native token-info success. A browser window is offered only when the auth
result explicitly requests browser interaction; it is never opened automatically. Complete the
provider's normal security check there if needed, then hand off the bearer session and close the
browser. Unsupported DPoP-bound sessions still fail explicitly; no challenge or signing-key check
is bypassed. The operator subsequently confirmed successful native sign-in; live TFSA reads are recorded in ADR-0072.

Fix browser handoff with a bounded stream adapter using byte-array reads with zero offset. WebView2
does not support all members expected by ordinary .NET stream consumers. Clear temporary buffers,
parse only the copied bytes, and schedule modal closure after the browser callback returns.
Errors identify the failing stage without including raw exceptions, request bodies or token values.

## Validation and boundary

Focused offline tests cover native password/OTP, invalid-code retry, timeout/cancel, preservation of
pending OTP during timer checks, explicit browser challenges, and the actual COM stream limitations.
An isolated WebView2 probe intercepts every application request and supplies synthetic responses;
no operational dashboard, SQL or Wealthsimple API is used by it. See project status for final counts.

TFSA account queries, broker-to-local reconciliation, and order submission/status/cancellation remain
separate unfinished integration work. A session is their prerequisite; it does not implement them.

References: [authentication protocol notes](../concepts/wealthsimple-authentication.md),
[WebView2 threading and callbacks](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/threading-model).
