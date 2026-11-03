# Wealthsimple authentication and engine diagnosis — November 2, 2026

## Workstream continuity and next-session handoff

Continue this engine investigation as a fault encountered on the installed compact restart/current-state
path under [issue #19](https://github.com/JosephMawer/TraderVI/issues/19), not as a new competing restart design.
The parent Codex chat is **Redesign engine restart recovery**, host `local`, thread
`01a18def-9f8a-7c10-b18e-a05986e17746`. Its latest available discussion was reviewed for this handoff.
Read it alongside the [restart design/decision record](restart-recovery-design-20261030.md),
[ADR-0117](../adr/0117-compact-trading-current-storage.md), current status and the execution checklist.
Older design-stage installation labels do not override later dated installation evidence.

The parent chat's latest clarification says the restart is installed; removing routine full-history
broker reads remained a separate design question. Its proposed ordinary check starts at the last agreed
checkpoint, checks original unfinished orders and new activity, then compares cash and holdings. That
proposal is not permission to replace reconciliation policy. The grouped-history comparison was read-only
and not deployed to the engine. Do not describe performance cleanup as an agreed mandatory second phase.

The immediate task is to identify the precise guard/exception behind the observed completed-fill versus
managed-strategy divergence and the UI restore failure. Preserve the distinction between observed mismatch
and proven cause. Safe cause diagnostics belong to existing issue #23; the separate symbol-wiring work is
#24. Expanded exceptional/damaged-state recovery remains deferred to #25; the prior held-position bid
investigation remains on hold. Any repair proposal must preserve original order identities, applied fills,
current permissions, protection and remaining purchase allowance. This handoff grants no live restart,
deployment, SQL repair, recovery/hold clearance, financial reapproval or permission to resend the filled order.

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

At 14:47 the exact exception behind Real execution was unavailable: the installed `BrokerExecutionHost`
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
the precise failing guard required the follow-up below. Private payloads remain in ignored local artifacts.

## Follow-up through 15:12 Toronto — closed-position guard confirmed

**Live execution.** At 15:07:34 and 15:12:36 the engine threw the fixed closed-position error in
`TradingStorageValidation.ValidateTransition`. The attempted write changed an already closed position
cycle back to open. At 15:12, bounded .NET runtime observation showed compact recovery, a successful
account observation, strategy input/evaluation, managed append, compact publication and the failed
compact save. The final throw preceded the host's Blocked timestamp by about two milliseconds.
The source guard rejected the write. The retry kept managed revision 6843, `OrderTransmissionObserved`.
The trace identified the stage and guard. It did not capture the uncommitted managed event name.
[Recovery branch](../../Core/Db/CompactEngineRestart.cs),
[managed publication](../../Core/Trader/Storage/CompactManagedPublication.cs),
[transition guard](../../Core/Trader/Storage/TradingStorageValidation.cs).

**Original sell.** The SQL join matched the managed request to exactly one compact receipt, including
decision, position, security, side, symbol, quantity, reference price and permission. The compact
receipt was terminal with all cumulative quantity and value applied, zero reservation and a closed
position. The same request's protected local journal contained a quoted managed sale for the sealed
account and identity, with matching security and quantity. Both the journal and managed record had
acknowledgement. The journal had zero order observations, no status-check time, no inventory
verification and no conflict. The guarded broker read independently found no pending orders and
history agreed with holdings. It does not verify the local journal. Compact recovery reads the broker
by original ID and applies the sell delta to its own tables. The ordinary managed synchronization
is the path that checks that original sell through the journal and verifies remaining inventory.
[Compact fill application](../../Core/Trader/Storage/OfflineAccountRestart.cs),
[managed account sync](../../Core/Trader/Promotion/BrokerManagedAccountSource.cs),
[local sell verification](../../Core/WSTrade/Orders/ManualMarketSellWorkflow.cs).

**Why it repeats.** Compact checkpoints 17 and 18 were both `ObserveBrokerAccount`, with zero
unfinished compact orders and zero active compact restrictions. The two remaining open positions
had saved exit decisions and a protection owner different from the current strategy. That comparison
produces a scoped sale notice even when the compact unfinished-order list is empty. During market
hours the installed branch with notices evaluates protection using the pre-sale managed account.
The publisher can therefore see a changed protection row for a holding that exists only in the stale
managed view. Its active-position lookup omits the closed row. It proposes an open position and the
durable store rejects it. The exception interrupts the cycle before ordinary account synchronization
can advance the journal and managed record. The owner comparison establishes the notice condition;
it does not establish new authority to sell those remaining holdings.

**Dashboard.** The displayed account status identified the unresolved-local-order block and failed
history import. In this branch the synchronization result contains no saved import receipt.
`WealthsimpleAccountViewModel` calls `Invalidate(true)` and skips `RestoreAsync`, which explains the
placeholder despite the verified assignment. This observed refresh never requested the strategy
lease. Keep controls disabled while the linked-account check is incomplete.
[Account display](../../TraderVI.WPF/Viewmodels/WealthsimpleAccountViewModel.cs),
[import boundary](../../Core/WSTrade/Accounts/BrokerAccountSynchronizationWorkflow.cs).

**Offline reproduction.** A synthetic acknowledged sale is already reserved when compact recovery
reads its fill. A different holding retains an exit under its previous owner. Recovery closes the sold
cycle and returns the other holding's notice. Publishing a protection-time change against the
pre-sale managed account reproduces the installed exception and rolls back. Publishing the same
original order against the fixture's reconciled account advances the checkpoint without another
position change. Replay preserves the terminal receipt, other holding's protection, risk references
and purchase allowance. A separate UI test shows the blocked refresh and restoration of the same
assignment after a successful linked refresh. The aligned input in the first test is supplied by the
fixture. The implementation and retry tests for the handoff are recorded below.
[Sell handoff test](../../TraderVI.Core.Tests/BrokerManagedSessionTests.SettledSaleHandoff.cs),
[dashboard test](../../TraderVI.Core.Tests/WealthsimpleAccountTests.Polling.cs).

**Proposed repair under #19.** Before protection evaluation consumes a managed holding that compact
recovery has already closed, look up the outstanding managed request in the terminal receipt store
by binding and original request. Verify decision, position, permission, terms and cumulative fill.
Use the existing original-order and inventory checks to finish the local journal and publish the
reconciled managed account. Separate that step from ordinary evaluation and dispatch. Keep the
closed-position guard and the other holdings' scoped notices. The existing whole-account import can
refuse an unrelated unresolved order. Use original-ID reconciliation for this handoff and keep
independently verified exits eligible under their existing controls. If evidence conflicts, preserve
the hold on the affected action. Test interruption on either side of journal verification and managed
commit, repeat polls, conflicting evidence and unrelated unresolved orders before release. For the
dashboard, distinguish a blocked linked-account check from a verified empty assignment in its label.
For #23, surface a fixed stage and code such as `CompactManagedPublish / ClosedPositionReopen`;
the existing backoff and permission checks still apply.

**Evidence boundary.** Engine PID 22452 and collector PID 1452 continued running. The engine on disk
reports source `e507729c`; its settled handoff releases the lease and uses individual history reads.
The checkout's lease-reuse and grouped-reader work are outside this finding. At 15:12:52 the status
capture showed Running, Connected, execution Blocked, seven blocked workers and the same managed head.
The only open SQL integrity incident in the refreshed query was Paper-scoped. No repair was installed
or exercised live. The focused offline suite passed 65 cases. Compiler warnings were present and
dependency security was not reassessed. The full solution and native SQL rehearsal were outside this run.

Local evidence under ignored artifacts includes the broker v2 receipt `55985a3c-329b-4350-a005-3d903a58555f`,
status captures beginning `20261102T195600843Z` and `20261102T201250176Z`, and the allowlisted
`classified-codes.txt` and `stage-codes.txt` in `artifacts/engine-ui-20261102`. Diagnostics emitted
fixed source/error codes and method labels only. The SQL helper receipts for the original terms,
journal join, retry stages and exit-owner comparison were matched to their submitted query SHA-256
values (`1085128F…`, `4489BE61…`, `DD3AC6E9…`, `58C80DB9…`). The protected-file comparison
checked the selected slot version, managed revision and installed Core build as well as the original
journal envelope. These cross-store observations have distinct capture times.

## Offline implementation and retry results

The #19 change now runs after each compact recovery pass, including a repeated pass following a new
protective decision. Before managed protection or ordinary evaluation, it finds any original managed
sell whose holding is still present in the managed account and already closed in compact storage.
It checks the durable position and terminal receipt, the original broker order, the local sell
envelope and remaining inventory. A different security's pending order can retain its reservation.
Unknown pending scope or pending work on the sold security prevents verification.
[Engine handoff](../../Core/Db/CompactEngineRestart.cs),
[original sell reconciliation](../../Core/Trader/Storage/CompactManagedSaleHandoff.cs).

The journal saves the verified fill and then its inventory check. The managed account lease stays
held through the managed append, with the login and original assignment rechecked before commit.
The new revision replaces that request's cumulative fill, removes the sold position from protection
inputs and retains the previous whole-account import and risk evidence. The compact writer looks up
the completed receipt even though it has left the active orders. It advances the shared checkpoint
only if the proposed revision leaves the compact financial rows intact. The first subsequent full
import must agree with the direct fill. A different activity listing requires verified instrument
correspondence and identical financial facts. [Writer](../../Core/Db/CompactManagedWriter.cs),
[publication checks](../../Core/Trader/Storage/CompactManagedPublication.cs).

| Offline interruption or retry | Observed result |
|---|---|
| Before saving the journal fill | The acknowledged intent survived. Retry verified the same original request. |
| After saving the fill, before inventory verification | Reopening the encrypted test journal recovered one observation. Retry completed inventory verification. |
| After journal verification, during the managed transaction | Compact and managed state kept their prior committed revisions. Retry completed the handoff. |
| After sealing the alternate checkpoint, before database commit | The committed version still selected the previous matching seal. Retry safely replaced the unused seal. |
| After commit with the reply lost | Reload found the reconciled managed revision and its matching compact seal. Two more polls performed no further handoff writes. |
| Wrong original ID or decision, missing broker response, disagreeing proceeds or fee, same-security or unknown pending orders, nonzero inventory, or revocation before managed commit | Managed settlement stayed pending. Once the fixture restored matching evidence and permission, the original request reconciled. |

Each retry fixture included another holding with its own protection and an unrelated pending buy.
The tests observed a single cumulative sale fill on the original cycle, the same terminal compact
receipt, preserved allowance and risk references, and zero broker submissions. Managed protection
completed against the remaining holding. A subsequent ordinary import in a paused fixture accepted
the posted sell and retained a single fill. Tests used the file journal and protected alternating
checkpoint files with synthetic keys, fake broker reads and an in-memory transactional database.
[Regression cases](../../TraderVI.Core.Tests/BrokerManagedSessionTests.SettledSaleHandoff.cs).

The 18 focused checks passed. These included 15 original-sale handoff cases, the earlier guard
reproduction and two broker-context cases. The full Core suite passed 3,578 with 16 skipped and
zero failures. The isolated Release engine build succeeded with
255 existing compiler warnings (CS0162, CS0168 and CS8632). Validation reused restored dependencies
and did not refresh dependency-security advisories or build the full solution with SSDT.

**Release dependency.** The engine checks the exact approved build on each trading cycle. The
ordinary ongoing build review requires existing managed orders to have reconciled full fills. At the
last observed live head that prerequisite failed. Before installation, a separately reviewed
single-owner reconciliation procedure must establish the original sale's settled state without
submission authority. The current engine and collector continued running throughout implementation.
No live database or broker request, deployment, hold clearance or permission change was performed
for these tests. [Build review guard](../../Core/Trader/Promotion/BrokerImplementationReviewWorkflow.cs).

## Live reconciliation and build review

On November 2 the operator requested installation and selected a new immutable settings record
created only on dashboard approval. Release commit `79e66dd5` contains the single-request maintenance
entry point and the successor writer. The exact Release test package passed 3,578 with 16 opt-in
skips and zero failures. The matching dashboard/engine package built with 257 compiler warnings.
There was no fresh dependency-security audit or SSDT/full-solution build. In a separately marked
SQL Server LocalDB, rollback after sealing kept the original settings and selected checkpoint.
Commit inserted the successor, retained the old row and protection eligibility, and a subsequent
ordinary publication retained the successor pointer. The old and disabled grants were rejected.

At 15:58 Toronto the read-only SQL helper still showed revision 6843 and one unreconciled original.
The broker v2 helper found zero pending orders and agreement between history and holdings. A fresh
checksummed full backup passed SQL Server verification. Both copies matched by SHA-256 and Windows
reported the approved OneDrive copy in sync before cutover.

After close, the old dashboard closed and the original engine drained. Protected order, grant,
continuity and compact records were copied and hash-checked. The maintenance process verified the
old approved binary and assignment under exclusive ownership. It could read the original sale
and remaining inventory but had no submission transport. It returned `OriginalSellReconciled`.
At 17:50:21 the independent SQL readback verified managed revision 6844, `CompactSaleReconciled`,
compact checkpoint 29 and the original managed request marked reconciled. Pre/post financial,
position and order fingerprints and buying mode matched. One original settings row remained.

At 17:52:41 the standard runtime contained the verified 141-file release package, Core SHA-256
`9D2432B2ECE88DECA8B56E6F4E609B115E8C00DBCF78B4F847725128C54C5590`. The release retained
collector routing and individual history reads for ordinary trading. Engine PID 12964 reported
**Build review only**, Connected. Matching dashboard PID 7048 opened for operator approval. Collector
PID 1452 continued throughout. The prior standard runtime and private cutover receipts are preserved
under ignored `artifacts/issue19-release-20261102`. This checkpoint verifies live sell recovery and
installation. Activation and market-hours protection require their own evidence.

The dashboard showed **Execution stopped** at 17:59:51 and a generic engine error with no build
proposal. The 17:59:57 SQL read still showed revision 6844 and one immutable settings row. A later
retry in the same dialog failed its current-account check before review preparation. At 18:22:15
the engine's own connection receipt reported its last verification at 17:58:13, expiry at 18:00
and no current connection. The review service and collector were both running. This establishes
why the later dialog could not proceed. The first failed RPC did not retain a stage code, so its
cause is unconfirmed. Reopen setup after normal Edge sign-in and verify the operator's build
approval and separate Resume against the saved receipts before switching to normal startup.

**November 2, 20:26 Toronto.** A later review reached the approval action. Its dashboard reported
`InvalidDataException`. Read-only SQL verified revision 6844, compact version 29, one settings
record, the reconciled original sell and unchanged financial, position and order fingerprints.

An offline replay expanded the checksum-verified managed head, used the saved 20:25:41 account
import and verified the protected checkpoint against the compact rows. The installed successor
writer rejected its comparison of the prior and proposed open positions. Both remaining
positions matched the compact book exactly. The fresh broker projection omitted the original
strategy purchase target on both opening fills. This reproduced the approval failure independently
of the earlier expired connection.

The correction restores strategy attribution from matching original managed orders in the two
build-review paths that can save a fresh account. It uses the ordinary trading cycle's existing
mapping. With the saved import, both targets matched and the unchanged guard prepared one settings
record with zero position or order writes. The offline workflow regression includes an invested
compact account, a broker fill without its target, interrupted publication and repeated approval.
All 22 focused review, risk synchronization and sale recovery cases passed.

**21:17 Toronto installation.** Release `cc64462c` passed 3,578 Core tests, with 16 opt-in
skips. A separate marked SQL Server LocalDB passed eight approval cases including rollback and
retry with an attributed opening fill. The matching dashboard build had two compiler warnings
and zero errors. The exact staged Core also passed the captured-account comparison. The fresh
checksummed backup passed verification, its copy matched by SHA-256, and Windows reported it
in sync before cutover. At 21:08:59 SQL still showed revision 6844, compact version 29 and one
immutable settings record. The collector kept PID 1452 throughout the review-service switch.
The 141-file package installed at the standard path with Core SHA-256
`34C9C23F83341A202F75744DDD22F6CAC18DE44ED01B0451A42F0AF6C7069EBE`.
Engine PID 21012 reported Connected in build-review mode. Dashboard PID 33972 displayed
**Saved strategy and buying mode restored** after refreshing the linked account. Operator
approval, separate Resume and normal restart still require their own receipts.

### Normal engine activation

By 21:23 Toronto on November 2 the operator had approved the installed build and separately enabled
the host. SQL readback showed `ImplementationReviewed` at revision 6845, compact checkpoint 30
and one new immutable settings row alongside the original. The installed Core hash matched the
selected settings and the enabled, restartable Windows-protected grant. The review service drained
before normal engine PID 25708 started. The dashboard and collector kept their existing processes.

At 21:24:01 the normal engine completed its restart account check with zero consecutive failures.
It verified the linked broker account and holdings, zero pending orders, and the compact assignment
against the restored grant. Its service status was **Running**, broker status **Connected**, and
execution **Waiting** for regular trading hours. Another SQL read at 21:24:28 verified the original
sell still reconciled and fully applied, with the same financial, position/protection and order
fingerprints and buying mode as before the repair. The setup window's current status at 21:25:47
read **Waiting for market**. Its action summary still displayed the earlier review-mode confirmation.

The read-only integrity check at 21:24 found only the existing Paper recovery incident. At 21:25
five Paper/dependent-Research workers had reported blocked since startup. This after-hours check
verified saved protection and restart readiness. Regular-session protection evaluation and the
previously deferred bid investigation retain their separate verification boundaries.
