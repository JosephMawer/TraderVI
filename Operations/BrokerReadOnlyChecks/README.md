# Fixed read-only broker checks

The operator authorized this reusable Wealthsimple read path on October 18, 2026, to remove repeated
sandbox approvals for routine account checks. The frozen helper is installed outside sandbox-writable
roots. Its saved rule approves only this no-profile launcher:

```powershell
& 'C:\Users\joseph.mawer\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe' -NoProfile -File 'C:\Users\joseph.mawer\.codex\tools\tradervi-read-only-broker\v1\Invoke.ps1'
```

Run that command **alone**, with no arguments, output redirection or chained commands. If the tool
requires `sandbox_permissions=require_escalated`, use that setting with the fixed command; its saved
allow rule is intended to satisfy the request without a new manual prompt. Rules load at Codex
startup; restart Codex to refresh existing chats. Old ad hoc NativeRead/dotnet commands do not inherit
this approval. Do not allow-list a repository script, arbitrary dotnet executable or general PowerShell.

## What it reads

One invocation verifies the saved bearer through token-info, discovers eligible open self-directed
TFSAs, requires exactly one, and reads its CAD financials/balances, all holdings and all pending-order
pages. It then reads the fixed ONE_DAY/REGULAR daily-performance query. This normally uses seven
requests; pagination is bounded by a hard total of 20. The overall cancellation budget is 90 seconds,
each HTTP timeout is 30 seconds, and each response is limited to 128 KiB. There is no polling/retry loop.

The helper reuses the existing account, pending-order and daily-performance parsers/models as linked
source. It does **not** reference Core or compile its login, refresh, order submission/cancellation,
SQL, trading-engine or runtime-host implementations. Its separate transport checks exact HTTPS
destinations, methods, headers, operation names and query documents. Variables must match the verified
identity, the uniquely discovered account, CAD and the fixed read filters. Unknown headers, modified
queries, extra variables, foreign accounts, token endpoints and mutation requests are rejected before
network transmission. Cookies, automatic redirects and proxies are disabled; TLS validation is standard.

## Saved login and results

Only `%LOCALAPPDATA%\TraderVI\Wealthsimple\session.dat` can supply credentials. Windows DPAPI
(CurrentUser encryption) unlocks it in memory; the refresh-token field is never read/stored by this
client. It never refreshes, removes or saves credentials. A ciphertext hash comparison at the end of
the reads rejects a changed saved login. There is no authentication fallback or challenge bypass.
Expired, missing or rejected authentication requires the normal application's sign-in flow, not a
broader helper approval. Checks may overlap the desktop's normal activity; they do not take its
session-rotation lease or stop its services.

Detailed account/holding/order identities and financial values are saved only in a uniquely named,
DPAPI-encrypted `artifacts/read-only-broker/snapshot-<capture>.dat`. The encryption entropy is
`TraderVI.BrokerReadOnlyChecks.Snapshot.v1`; format version 1 includes CaptureId, CheckedUtc,
AccountId, Snapshot, HoldingIdentities, Pending, OrderIdentities, DailyStatus and Daily. This file
contains no access/refresh tokens or request headers. It is an observation, never an executable plan.
Only separately authorized consumers should use private snapshots; do not introduce arbitrary
decryption, broker-history, quote or order-status modes into this approval.

The console and matching `report-<capture>.json` expose only status, counts, validation flags,
retrieval/provider times and the encrypted snapshot path. They omit symbols, identifiers, amounts
and tokens. Read the latest command's receipt, not an unrelated older artifact. A failed read never
publishes a successful report. Daily-query failure can leave the account/pending check successful;
`DailyStatus` and `DailyPerformanceAvailable` distinguish that case without inventing a return.

This is a non-atomic series of broker responses, not an executable quote, complete cash-flow history,
saved risk baseline, reconciled order journal or permission to trade. Retrieval time does not qualify
upstream market freshness. `QualifiedForAutomation` remains false. The fixed query protocol can change;
unsupported responses stop or mark the affected daily result unavailable rather than silently adapting.

The helper writes only under the ignored diagnostic directory, rejects existing reparse paths, uses
an execution lock, and creates unique result files without overwriting existing ones. Concurrent chats
must serialize runs or handle a busy result. Stored private snapshots have no automatic retention policy;
review cleanup separately. DPAPI is protection at rest, not isolation from other software running as
the same Windows user. Transient plaintext byte buffers are cleared; .NET token strings live only
inside the short-lived client process and are never exported.

## Build, install and validate

Build/publish `BrokerReadOnlyChecks.csproj` directly. Only the ProtectedData package is needed in
addition to .NET 10; no full Core build, SQL connection or application launch is involved. Run the
published executable with `--self-test` inside the sandbox for synthetic offline checks. The installed
launcher accepts no such arguments; its recurring approval is for the fixed live read only.

`Install.ps1 -PublishDirectory <reviewed-output>` is a **one-time authorized installer**, never a
recurring approved command. It runs offline tests, copies the reviewed build and no-profile launcher
to the versioned user directory, hashes every installed file, executes one bounded native read and
checks four approval-rule cases before saving the rule. It changes no existing rules or broker login.
If authentication is unavailable, installation can still finish safely; the native receipt must then
remain explicitly unverified. New versions/queries/endpoints require reviewed installation and rules.

October 18 installation: **54 offline checks pass**, including exact-query/variable/header/endpoint
rejections, account ownership, partial-error rejection, expiry, request limit and a synthetic full
read. The native check succeeds with **seven requests**, including daily performance, without orders,
refresh or a saved-login change. Four rule-scope checks pass. The Release build has two nullable
warnings in the linked account reader; dependency restore used the local cache with audit disabled,
so no fresh dependency-security assessment or full-solution qualification is claimed. Evidence stays
in ignored `artifacts/read-only-broker/installation*.json` and the encrypted capture.

[ADR-0093](../../Docs/adr/0093-narrow-read-only-broker-checks.md) records the boundary.
[Codex rules](https://learn.chatgpt.com/docs/agent-configuration/rules) explain the approval mechanism.
