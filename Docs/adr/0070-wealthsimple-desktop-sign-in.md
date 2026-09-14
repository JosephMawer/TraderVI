---
cssclasses: [executive-brief]
---
# ADR-0070: Wealthsimple desktop sign-in and session status

- **Status:** Implemented; first operator login through the embedded browser and live refresh remain to be verified.
- **Date:** 2026-09-09 (Toronto)
- **Domains:** architecture

The browser-first interface below is superseded by [ADR-0071](0071-native-wealthsimple-login.md).
Native email/password and 2FA are now the default; browser verification is conditional. Storage and
connection-status decisions in this ADR remain applicable.

> [!summary] Decision and consequence
> The WPF sidebar opens a Wealthsimple connection window. The operator signs in on the provider's own page. TraderVI optionally remembers an encrypted session, never an email/password pair.

## Context

The immediate problem is entering credentials and knowing whether the desktop is connected.
The parent problem is reliable broker-session ownership. The root goal is eventual explicitly
authorized execution against reconciled account state. The operator requested a WPF sign-in screen
and an always-visible green/red sidebar indicator.

The observed login includes browser challenges. A native password form cannot yet reproduce that
flow reliably. ADR-0069 established a working bearer-session handoff, initially through a local HAR.

## Decision

Use WebView2, Microsoft's embedded Edge browser, to display only `https://my.wealthsimple.com` as
the top-level sign-in page. Use a dedicated InPrivate profile with password saving and autofill
disabled. Observe only successful user-token responses at the exact production token endpoint.
Copy public protocol metadata and opaque bearer tokens; never retain password, email, OTP, challenge
claims or cookies in TraderVI's session record. Close the browser before the native client takes
ownership and verifies the session. No scripts are injected and normal challenges remain mandatory.

Remembering a session is opt-in. Store `session.dat` in `%LOCALAPPDATA%\TraderVI\Wealthsimple`,
encrypted with Windows Data Protection API (DPAPI) under `CurrentUser`. This ties decryption to the
Windows account. It is not isolation from other software running as that same account. The record
contains access/refresh tokens, expiry and required public/device/session metadata only. Export
requires a protector and transient plaintext byte buffers are cleared. DPoP sessions cannot be
persisted or handed off without their original signing key.

One file lease prevents two TraderVI instances from rotating the same saved session. Remove the
durable copy before verification/refresh, and atomically save the verified replacement. A crash or
ambiguous refresh therefore requires sign-in again rather than reviving obsolete credentials.
This deliberately favors a fresh login over keeping a potentially consumed refresh token.

The sidebar always shows a colored dot and, when expanded, a text status. Its tooltip and accessible
name expose the same state. Green means verified, unexpired authentication; red means disconnected;
amber means checking, reconnecting or unable to verify. Recheck every five minutes, renew within
90 seconds of expiry, and retire green if verification is more than six minutes old. A ten-second
UI timer updates status independently of trading/SQL work. These checks cannot prove instantaneous
remote revocation; display the last verification time. No automatic retry follows ambiguous failure.

Disconnect removes local saved and in-memory session state. It does not remotely revoke other
Wealthsimple sessions. WPF shutdown disposes the native client and releases the lease after active
operations finish. Authentication never enables execution or alters strategy/account authority.

## Validation and remaining work

Synthetic tests cover the session lifecycle, refresh serialization/failure, stale indicators,
protection and handoff validation. Offline WPF rendering checks expanded/collapsed navigation and
the connection window. A Windows fixture checks real DPAPI encryption, corruption rejection,
file-lease exclusion and clearing storage. See project status for final results.

Wealthsimple may restrict embedded-browser challenges, require another top-level identity origin,
or issue browser-key-bound DPoP tokens. Report these as an unsupported handoff; do not weaken the
challenge or claim success. The next operator login and refresh are the live compatibility checks.

Sources: [WebView2 response observation](https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/webresourcerequested),
[password-saving setting](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2settings.ispasswordautosaveenabled),
[Windows ProtectedData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata).
