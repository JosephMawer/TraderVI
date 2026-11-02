# Wealthsimple authentication and engine diagnosis — November 2, 2026

## Authentication outcome

The operator confirmed successful sign-in through the revised Edge flow. At 14:47 Toronto the engine's
read-only connection status independently reported Connected, a remembered session and no pending
OTP/browser challenge. This establishes initial handoff and native verification, not future refresh
rotation or healthy trading execution.

The direct password/SMS attempt received an additional browser challenge. TraderVI deliberately ended
that unsupported direct attempt, so phone approval did not resume it. The embedded WebView2 fallback
then reached a passkey chooser that did not offer the existing Microsoft Password Manager key available
in regular Edge on this Windows 10 PC. The attempted phone workaround reported no available passkeys;
its provider/storage cause was not established, and no security settings or recovery requirements were bypassed.

The operator selected an Edge companion. Its implementation adds:

- **Sign in with Edge passkey** in the connection window and a regular-Edge login using the existing key.
- A narrowly used extension debugger connection to the explicitly paired tab, accepting only successful
  supported user-token exchanges at the exact production endpoint. Selected session/public metadata
  crosses the boundary; passwords, OTP claims, cookies, passkey assertions/private keys do not.
- A native-messaging helper restricted to the fixed extension origin, then a Windows-user-only named
  pipe with random pairing, bounded frames and expiry. Unsupported browser-key-bound DPoP sessions fail.
- Closure of the dedicated login tab before handing ownership to TraderVI's existing native verification
  and optional Windows-protected storage. No trading permission or strategy is enabled by this feature.
- A connection page that attaches observation **before** opening Wealthsimple. The initial toolbar-first
  instructions were too easy to miss, and the observed provider URL lacked the expected pairing marker.
  **Continue to Wealthsimple** removes that ordering/redirect dependency without new extension permissions.

The helper was registered per user. The operator loaded the unpacked extension, and the approved
desktop was staged separately with collector routing preserved. The refined desktop is
`artifacts/edge-passkey-20261102/desktop-runtime-v2` (PID 9432 at diagnosis). Engine PID 22452 and collector
PID 1452 remained unchanged. The original runtime and earlier desktop copy remain available.
Restored connectivity allows the already-enabled engine to use its existing authority; no new financial
approval, host enable, account recovery or policy change was issued.

Validation: 70 focused authentication/handoff tests, synthetic extension protocol/event/connection-page
tests, actual helper framing/private-pipe/receipt checks, Release WPF build, and documentation links passed.
Existing compiler warnings remain; NuGet security auditing was disabled, and the SQL-containing full
solution was not built. Implementation: `2ab06f8f`; helper record: `7c6089aa`; installation record:
`5a2b1598`; usability refinement: `35b77ba3`. [ADR-0123](../adr/0123-edge-passkey-session-handoff.md)
and [setup/rollback](../../Tools/Wealthsimple.EdgeBridge/README.md) own the protocol and operation details.

## Engine findings at 14:41–14:47 Toronto

1. **Authentication is working; Real execution is separately blocked.** The 14:47:24 execution status
   still reports the generic check-failure message with next retry at 14:52:24. The engine is Running,
   not verification-only. A waiting/successful scheduler lane does not mean its inner execution check
   succeeded: the execution host catches failures and publishes Blocked itself.
2. **A completed sell has not propagated through the full strategy record.** The guarded broker helper
   succeeds, finds no pending orders and reports history/holdings agreement. Compact SQL has a terminal,
   fully applied sell receipt with no remaining work, and its current positions agree in aggregate with
   the broker. The managed strategy archive remains at revision 6843, event `OrderTransmissionObserved`,
   saved 14:39:17, with that original order still unreconciled and the pre-sale position set. This is a
   specific divergence between compact/broker settlement and managed-strategy reconciliation, not proof
   that a trade needs retransmission. Preserve its original identities; do not resend or edit records.
3. **Several checks rule out simpler explanations.** The managed head checksum is valid under its actual
   UTF-16 hash contract; current engine projection checksums also pass. Compact checkpoint version 13 and
   its managed-revision pointer match SQL, and its approved build matches the running engine. The compact
   account is Active, saved execution/buy/sell permission remains enabled, buying is not operator-paused,
   and no active compact restriction or account loss/capital-review hold was reported. Metadata matches
   are not a complete replay/digest or protection qualification.
4. **The remaining integrity incident is Paper-scoped and old.** `PaperRecoveryRequired`, discovered
   October 30 at 18:41 UTC, remains protection-untrusted. No open Broker/global incident was returned.
   Seven blocked Paper/Research workers align with the source's explicit Paper and dependent-Research
   integrity guards. This preserves the existing Paper-recovery problem; authentication does not clear it.
5. **The three incomplete audit entries are historical failures.** The status reader retains latest
   records per kind, status and recovery scope, so a completed newer audit can coexist with an older
   Failed row. The retained failures are NewHistory (October 27), all-scope Recovery (October 22), and
   Real Recovery (October 26). More recent Real/new-history completions exist. They are not three
   currently running recovery jobs. The current Paper hold still needs its own reviewed recovery proof.
6. **The strategy picker is not proof the assignment vanished.** The account view shows a fallback label
   whenever its managed-strategy view model has no loaded selection. Restore errors retain the saved
   account mode and are caught by that view model. Verified engine diagnostics still read the approved
   managed record. Whether the UI's failed restore was caused by lease contention or another read error
   remains unproven. Do not approve a replacement strategy to cure that placeholder.

The exact exception behind Real execution remains unavailable: the installed `BrokerExecutionHost`
catch discards its cause, and the RPC boundary also returns a generic failed command. Safe cause logging
is already planned as issue #23. The narrowed reconciliation divergence warrants a focused reproduction
and safe stage/error-code diagnostics, preserving original orders, guards and retries. This investigation
does not authorize an engine restart, recovery/hold clearance, financial reapproval or database repair.
Connected and process responsiveness must not be represented as verified ongoing automatic protection.

## Read-only evidence and limitations

- Existing capture procedure: `artifacts/market-watch-20261029/Read-Status.ps1`.
  Captures `20261102T194155479Z-752bca37eff742ef9b7d7a7070117865` and
  `20261102T194742671Z-b61d57499e8f463f91f28c5e87261377` under that directory returned no component failures.
  Each associated ExecutionDiagnostics reply had Succeeded=true and a verified decoded payload.
- Installed broker v2 receipt capture `dc815c02-30a9-4c07-9b28-d1ac19990d6d`, 14:42 Toronto:
  successful bounded history/account read, no pending orders, history/holdings agreement, no orders sent,
  token refresh or credential changes. It does not qualify the local journal, cash timing or automation.
- Guarded SQL result `result-20261102T194353996Z-4e3da241653548cd9e529b495467d730.json` under
  `artifacts/read-only-db`, query SHA-256 `36380B680AB7BD8539EF22FBCC2DB2AB8EA9F62C16771E18E6A45ABB4ED7ADE9`.
  Its initial strategy hash comparison used UTF-8 and is invalid for that record's UTF-16 contract;
  that zero is not evidence of corruption. Other projection checks use their own documented hash contract.
- Corrected guarded result `result-20261102T194453384Z-6bd712e7a0d74c908bb7d0f97c16a841.json`, query hash
  `8AA2B2A6277689D7D1AD2D9760E0B3F79309AD9BD08A115CE257D63F970236F9`, confirms the UTF-16 head hash.
  Receipt hashes were matched to their submitted query files before interpretation.
- Read-only compact metadata comparison: `artifacts/engine-ui-20261102/Inspect-CompactReceipt.ps1`.
  Windows protection rejected the sandboxed read; the explicitly approved outside-sandbox run read only
  the protected compact receipt, checked it remained unchanged, and emitted metadata-match booleans.
  It did not export session credentials or write a checkpoint.

No diagnostic command sent orders, refreshed tokens, changed strategy/settings/grants, cleared a hold,
started recovery, wrote SQL or restarted a host. The existing engine continued its own approved work.
These are non-atomic observations; broker and compact data were captured at different instants. Stable
strategy head/checkpoint identities and the later failed retry support the divergence, but identifying
the precise failing guard still requires further evidence. Private payloads remain in ignored local artifacts.
