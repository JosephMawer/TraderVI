# ADR-0093 — A narrowly approved broker-read helper

**Date:** October 18, 2026. **Status:** Accepted by the operator's setup request; installed/native read verified.
**Domains:** architecture, data-sources.

## Problem and goal

The immediate problem is repeated sandbox prompts when other chats unlock the saved broker login
and read the current account. The parent problem is dependable inspection without repeatedly
approving new mutable diagnostic binaries. The root goal is safe, efficient TraderVI work while
broker execution, credential changes and strategy authority remain separately controlled.

## Decision

Install a reviewed standalone Windows helper outside sandbox-writable roots and approve only its
constant no-profile `-File` launcher. Reuse the existing read parsers/models through explicit linked
source; compile no Core assembly, general login/refresh client or order mutation implementation.

The helper unlocks the existing CurrentUser DPAPI session in memory. DPAPI is Windows' per-user
encryption mechanism. It reads only the existing access token and public session metadata; it does
not load the refresh token, refresh authentication or save/delete credentials. A changed saved-session
ciphertext at the end of the reads invalidates the result. Expiry requires normal application sign-in.

Its independent HTTP guard permits only token-info GET and six exact GraphQL read documents:
account discovery, financials, balances, positions, pending orders and daily performance. GraphQL
is the provider's field-selection interface; a POST to it is not inherently read-only. The guard
therefore verifies the exact query, variables, identity/account binding, destination, method and
headers before forwarding. No caller arguments, alternate endpoint, arbitrary query or raw token
export is available. Redirects, cookies and proxies are disabled. Reads have explicit time/size/count limits.

Save detailed observations with CurrentUser encryption; print only counts, flags and times. Keep
`QualifiedForAutomation=false`. The operator's read-helper authorization includes its bounded native
qualification and recurring task-relevant use, not order placement, cancellation, broker-history
imports, SQL writes, credential rotation or live-strategy activation. Query expansion is a reviewed
upgrade. Existing mutable NativeRead/dotnet commands do not inherit the installed helper's approval.

## Consequences and limits

The broker bearer retains its provider-granted permissions; it is not a new provider-issued read-only
token. The frozen executable and fail-closed transport enforce this local read-only path. Hash checks
detect installation drift; they do not isolate against a Windows administrator. Private snapshots
remain encrypted at rest and retain their original observation times.

Separate broker calls are not atomic and cannot establish market freshness, executable prices,
cash-flow completeness, reconciled risk or order/execution readiness. A failed optional daily read
is separately labeled rather than replacing the latest return with an older or Overall value.
There is no automatic retry/refresh or recurring schedule. Existing Codex chats must reload rules
and use the fixed command; the general sandbox and unrelated approvals remain in place.

[Procedure and validation evidence](../../Operations/BrokerReadOnlyChecks/README.md).
Related: ADRs [0070](0070-wealthsimple-desktop-sign-in.md), [0072](0072-wealthsimple-tfsa-account-display.md)
and [0091](0091-local-read-only-database-checks.md).

## Review questions

1. Why does approving all GraphQL POSTs fail to enforce read-only access?
2. What happens when the saved broker login expires or changes during a read?
3. Why does a successful snapshot still leave trading qualification unchanged?
