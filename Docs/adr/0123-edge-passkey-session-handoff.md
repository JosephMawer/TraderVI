# ADR-0123: Existing Edge passkeys through a local session handoff

- **Status:** Accepted implementation scope November 2, 2026; installation and real sign-in verification pending.
- **Date:** 2026-11-02 (Toronto)
- **Domains:** architecture
- **Extends:** [ADR-0071](0071-native-wealthsimple-login.md)'s direct and embedded-browser sign-in.
- **Supersedes in part:** [ADR-0070](0070-wealthsimple-desktop-sign-in.md)'s exclusive embedded-browser fallback.

## Problem and decision

The immediate problem is that the operator's Microsoft Password Manager passkey is offered in regular
Edge but absent from TraderVI's WebView2 login on Windows 10 build 19045. The parent problem is broker
authentication without another credential or ID/selfie recovery. The root goal is reliable broker
connectivity under independent verification and execution controls. The phone workaround reported no
available passkeys; its storage/provider cause is unverified.

The operator selected an Edge companion instead of manual session import. TraderVI opens a dedicated
regular-Edge window with a random 256-bit pairing value in the URL fragment. The operator clicks the
extension's **Connect this tab** before logging in. It removes the fragment, observes the normal login,
then transfers only a supported successful bearer session. The passkey (a private signing credential)
stays in its password manager. Its private key and login assertions never cross into TraderVI.

Edge's `debugger` permission is powerful and can observe more than login traffic. The implementation
restricts attachment to the explicitly paired tab and observation to POST/200 user grants at the exact
production token endpoint. Only public request metadata and selected token fields are retained. There
are no content scripts, host permissions, cookie access, persistent browser storage, request replay or
injected login code. Detach on timeout, navigation outside the provider origin, debugger dismissal or
completion. Keep Edge's debugging indicator. A fixed public manifest key stabilizes the extension ID.

Native messaging is Edge's supported extension-to-desktop transport. Per-user registration allows only
the fixed extension origin. The host forwards one bounded message to a randomly named pipe restricted
to the current Windows user. Pairing lasts at most five minutes, messages at most 128 KiB, and the host
exchange at most one minute. Cancellation disposes the listener. No payloads or raw exceptions are
printed or written. Clear transient byte buffers; managed strings/browser buffers cannot be guaranteed
erased immediately. Same-user processes remain within the existing local trust boundary.

Detach and close the dedicated browser tab before native handoff. Reject wrong pairing, endpoint,
method/status, credential metadata, unsupported grants and DPoP tokens (bound to the browser's signing
key). Existing native token-info verification must succeed before Connected or optional protected
persistence. Authentication does not enable trading, alter permissions or restart the engine. Close
other Wealthsimple Edge tabs before starting to reduce concurrent renewal. Provider background renewal,
first native passkey handoff and live refresh compatibility still need operational verification.

## Alternatives and rollout

Do not copy or share Edge profile/passkey files. WebView2 has separate profile ownership. Windows 11
passkey-manager integration is not an OS-upgrade requirement for this fix. HAR (saved network traffic)
remains a discovery bridge, not the selected user flow. Removing passkeys or weakening security was not selected.

Offline tests cover pairing, strict exchange metadata, malformed/oversized/truncated framing,
cancellation, a real local pipe receipt, extension field filtering/tab scoping and detach/close ordering.
A WPF build checks the UI/helper integration. [Current status](../project-status.md) records validation;
[setup and rollback](../../Tools/Wealthsimple.EdgeBridge/README.md) describe installation. Installation
requires operator approval after review; do not replace a running desktop or engine for routine validation.

Sources: [Edge APIs](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/api-support),
[native messaging](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging),
[Password Manager passkeys](https://blogs.windows.com/msedgedev/2025/11/03/microsoft-edge-introduces-passkey-saving-and-syncing-with-microsoft-password-manager/),
[WebView2 data ownership](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder).

## Review questions

1. Why must a native verification succeed after Edge authentication?
2. What limits the extension's powerful debugging permission during a login?
3. Why must a DPoP session be rejected after successful passkey authentication?
