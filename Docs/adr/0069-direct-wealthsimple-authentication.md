---
cssclasses: [executive-brief]
---
# ADR-0069: Direct Wealthsimple authentication

- **Status:** Accepted for an isolated auth prototype; desktop integration and execution remain future work.
- **Date:** 2026-09-09 (Toronto; observations extend into September 10 UTC)
- **Domains:** architecture

Desktop login and optional protected persistence are now implemented under
[ADR-0070](0070-wealthsimple-desktop-sign-in.md); the console prototype remains memory-only.

> [!summary] Decision and consequence
> Build direct Wealthsimple authentication, as requested by the operator. A local browser-session handoff has been verified by our .NET client; this does not activate broker execution or connect the running WPF dashboard.

## Context

The immediate problem is authenticating our own Wealthsimple client. The parent problem is reliable
broker connectivity and reconciliation. The root goal is eventual explicitly authorized execution
of an approved TraderVI strategy against verified account state.

The operator rejected SnapTrade in favor of direct control. The old `wstrade.WSTrade` implementation
has obsolete protocol assumptions and credential storage. Its order code remains inactive.

## Decision

Build an isolated boundary in `Core/WSTrade/Authentication`. The first verified route is a
user-completed browser login followed by a local HAR handoff. HAR is a browser network-capture format.
Parse only a successful user-token response from the exact production endpoint, then send a fresh
token-info request. Never replay captured traffic. Token verification establishes a session, not
reconciled account state or permission to place strategy orders.

The observed login uses password, OTP (one-time verification code), and a 1,800-second bearer token.
The public code also supports DPoP, a cryptographic proof tying a token to its originating signing
key. Reject captured DPoP sessions because our new client does not hold the browser's private key.

Keep session material in memory and out of result objects, exception output and repository files.
Disable redirects and cookie persistence in the production HTTP transport. Password/OTP methods and
optional DPoP support are tested offline. Turnstile, passkeys and identity challenges must be completed
through the normal browser flow; the prototype does not bypass them.

Serialize refresh calls and adopt rotated tokens only after verification. Discard the local session
after an ambiguous refresh failure because the server may already have rotated its credentials.
Do not retry automatically. DPoP's explicit nonce challenge allows one newly signed retry, unrelated
to order retries. Expired access is never sent, but explicit refresh may still renew the session.

## Alternatives and consequences

- SnapTrade was rejected by the operator in favor of direct integration.
- Reactivating the legacy client would retain outdated protocol and session behavior.
- HAR handoff is a discovery bridge, not the selected permanent login interface. Integrated browser
  login and protected storage remain separate work.

We own protocol compatibility and session recovery. No claim about unattended login or order APIs
follows from the auth check. Existing strategy approval and execution boundaries remain intact.

## Evidence and review

The .NET client received HTTP 200 when verifying the operator-supplied bearer session directly with
Wealthsimple. No password replay, live refresh, account reconciliation or trade was performed.
See [authentication notes](../concepts/wealthsimple-authentication.md).

1. Why is login different from permission to route strategy orders?
2. Why does handing off a DPoP session require its original signing key?
3. Why must an uncertain refresh response not trigger an automatic retry?
