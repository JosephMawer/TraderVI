# Wealthsimple authentication: first direct connection

**Observed:** September 9, 2026 Toronto / September 10 UTC.
**Decisions:** [ADR-0069](../adr/0069-direct-wealthsimple-authentication.md), [ADR-0070](../adr/0070-wealthsimple-desktop-sign-in.md), [ADR-0071](../adr/0071-native-wealthsimple-login.md).

The isolated .NET client verified a user-completed browser session directly with Wealthsimple
(HTTP 200). WPF now defaults to native email/password sign-in and a verification-code prompt.
The first direct native login still needs operator verification. Authentication establishes no order authority.

## Desktop sign-in

1. Start the rebuilt WPF app and click **Wealthsimple** at the bottom of the left sidebar. The dot
   remains visible when the sidebar is collapsed.
2. Optionally select **Keep me signed in on this Windows account**. The default is memory only.
3. Enter your email/password in TraderVI and select **Sign in**. If Wealthsimple requests 2FA,
   enter the code in the native **Verification code** field and select **Verify code**. There is no
   browser in this default flow. Credentials stay in memory only; closing/cancelling or a ten-minute
   timeout forgets an unfinished attempt. Background checks leave that attempt alone.
4. TraderVI verifies its own session directly. Green means verified; red means disconnected; amber
   means signing in, awaiting verification, checking, reconnecting or unable to verify.
5. Only an explicit provider browser-security challenge reveals **Continue with browser verification**.
   If required, complete that check normally. The temporary browser closes after session handoff.
   Account data and future orders use the native HTTP client, not an open browser dashboard.

Saved sessions live in `%LOCALAPPDATA%\TraderVI\Wealthsimple\session.dat`, encrypted by Windows for
the current user. Do not put credentials in appsettings, SQL, source files or the repository. Browser
runtime files use the adjacent `Browser` directory; the login profile is InPrivate with password
saving/autofill disabled when the fallback is used. **Disconnect** removes TraderVI's saved session and memory references.
Other browser sessions are unaffected. Unchecking the remember option removes the saved copy.

The app verifies every five minutes and refreshes within 90 seconds of expiry. A last-verification
timestamp explains the evidence behind green; connectivity cannot be inferred continuously between
checks. A ten-second UI timer retires stale state after workstation sleep. Only one desktop process
owns saved-session renewal. Network/rotation uncertainty ends local connectivity and asks for a new
sign-in, without repeated refresh attempts. Password/passkey challenges and DPoP handoff compatibility
still need live checks. No trade or account API is wired to this screen.

The operator's first browser login exposed a handoff bug: WebView2's COM stream throws on stream
operations assumed by the JSON parser and on nonzero byte-array read offsets. A bounded adapter now
reads zero-offset chunks into temporary memory before parsing; an actual offline browser probe
reproduces the old failure and verifies the adapter. This does not require changing login behavior
at Wealthsimple. The passkey enrollment page shown after login was not the handoff error.

## Observed protocol

The user supplied a local HAR with preserved login requests. No password, email, verification code,
token, device/session identifier, account identifier or private response body was copied into
repository notes or fixtures. All test data is synthetic; `*.har` is ignored by Git.

| Step | Endpoint | Observed result |
| --- | --- | --- |
| App initialization | `POST https://api.production.wealthsimple.com/v1/oauth/v2/token` | App-only bearer token, not a user login |
| Password grant | Same token endpoint | HTTP 401 with OTP-required header |
| Password plus verification code | Same token endpoint | HTTP 200; bearer access token, refresh token, `created_at`, `expires_in: 1800` |
| Session verification | `GET https://api.production.wealthsimple.com/v1/oauth/v2/token/info` | HTTP 200 with available profiles |

Password JSON uses `grant_type`, `username`, `password`, `skip_provision`, `scope`, `client_id`.
The observed scope is `read write`; `skip_provision` is true. It is the web client's provisioning
parameter and does not skip verification. `OPTIONS` entries are browser CORS preflights.

The code travels in `x-wealthsimple-otp`; the next request can carry the server's
`x-wealthsimple-otp-authenticated-claim`. Captured password requests also contain Turnstile,
device/session headers and an existing remembered-OTP claim. Their presence does not prove which
are mandatory on a new device. The importer copies no password, OTP, Turnstile, remembered-login
claim or cookie. Its follow-up sends the bearer token and recognized device/session/client headers.

The captured token-info request omitted Authorization, consistent with browser HAR sanitization.
The token response still contained a usable bearer session. A fresh .NET token-info request
independently verified it. No DPoP header or token was observed in this login.

## Public-code evidence

- [Application bundle](https://assets.wealthsimple.com/app-e812a6e0c352b1a0.js): production origin,
  API version `v1`, token/info paths, refresh grant, challenge headers and optional DPoP handling.
- [Login chunk](https://assets.wealthsimple.com/62-f67fb686842cda65.js): password/OTP mapping,
  Turnstile and optional DPoP upgrade.
- [RFC 9449](https://www.rfc-editor.org/rfc/rfc9449.html): DPoP proof format and nonce behavior.

These are observations of a private API, not a published broker integration contract. Downloaded
public scripts remain under ignored `artifacts/wealthsimple-auth-discovery/` and must not be committed.

## Code and usage

`Core/WSTrade/Authentication` contains the auth client, safe result types, browser-capture parser,
opaque challenge context and optional in-memory P-256 proof key. `Tools/Wealthsimple.Auth` is an
isolated console host: no SQL, market-data collection, application monitor or order routing.

```powershell
dotnet restore Tools/Wealthsimple.Auth/Wealthsimple.Auth.csproj --ignore-failed-sources --verbosity minimal -p:NuGetAudit=false
dotnet build Tools/Wealthsimple.Auth/Wealthsimple.Auth.csproj --no-restore
dotnet Tools/Wealthsimple.Auth/bin/Debug/net10.0/Wealthsimple.Auth.dll --har "C:\local-private-folder\login.har"
```

The last command makes one verification call, reports only safe status, and disposes the in-memory
session. It never saves tokens, copies the capture or changes the source file. `--interactive` keeps
the session in the console process for `verify`, `refresh`, `forget`, `exit`. `forget` drops local
references; it does not revoke the broker session or delete the capture.

`--refresh` explicitly sends a refresh grant and verifies the returned token. This may rotate the
browser's shared refresh token. This discovery run verified the bearer token only; live refresh
and fresh password/OTP login in .NET remain unverified. The low-level login methods can accept
normal browser challenge results; they do not solve or bypass Turnstile.

The importer selects the newest successful user-token response, ignores app-only grants and other
hosts, and rejects missing/malformed or DPoP session material. It does not fall back to an older
bearer session after a newer DPoP response. Expired access is never sent; explicit refresh can still
be requested when a refresh token exists. No imported token is treated as verified until token-info.

## Validation and next steps

Focused tests cover the observed OTP flow, verification, malformed success, safe error output,
rate limits, challenge handling, refresh serialization/rotation, uncertain refresh failure,
capture isolation/expiry and independently verified DPoP signatures. Exact final counts are in
project status. The tool builds under SDK 10.0.401; one live .NET token-info call returned HTTP 200.

Existing compiler warnings and cached package-security advisories are separate, including
`System.Drawing.Common 5.0.0` critical severity. No fresh dependency audit, complete solution/SSDT
validation, live password replay, refresh, account reconciliation, holdings request or trade ran.

Next: operator verification of native password/2FA and refresh/reconnection, then read-only account
reconciliation. Browser DPoP handoff requires key ownership design. Execution needs its own lifecycle design.

## TFSA account display — September 10, 2026 update

Native sign-in is now confirmed by the operator. Live authenticated account reads succeeded after
closing the app with an encrypted remembered session. The WPF main TFSA page loads a snapshot after
sign-in and offers explicit Refresh. Green remains an authentication indicator; data has its own
retrieval/failure status. The previous sections describe earlier discovery milestones.

`Core/WSTrade/Accounts` defines the fixed query documents and financial DTOs (data-only objects).
The auth client's account partial sends them to `https://my.wealthsimple.com/graphql` using its own
authorization header, verified `identity_canonical_id`, profile `trade` and API version `12`.
Minimal selections derived from the capture work live without copied operation hashes or cookies:

- Account catalog: identity → accounts, with cursor pagination and explicit self-directed TFSA checks.
- Account financials: selected account → currentCombined(CAD), value, net deposits and simple returns.
- Trading balance: selected account → tradingBalanceViewV2, CAD cash and buying power.
- Positions: identity financials filtered by selected account → current(CAD) → positions, all pages.

Display currency always comes from the returned amount; no FX conversion or missing zeros are inferred.
Retrieval time is not the broker's valuation time or an executable quote. The provider's return reference
date supplies its period label. Net deposits are not tax contribution room. An isolated live probe
prints only success/presence checks, never account IDs, holdings, balances, response bodies or tokens.

See [ADR-0072](../adr/0072-wealthsimple-tfsa-account-display.md). Broker-to-local reconciliation, pending
orders and order lifecycle remain separate work. No SQL state or strategy authority changes on refresh.
