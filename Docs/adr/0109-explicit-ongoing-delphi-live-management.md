# ADR-0109: Explicit ongoing Delphi Live management

**Date:** October 26, 2026 Toronto. **Status:** Accepted bounded no-transfer agreement; implementation in progress, not installed or activated.
**Domains:** architecture, risk-management, user-interface, market-microstructure.
**Related:** ADRs 0079, 0081, 0083, 0085, 0105, 0107.

The immediate problem is repeated dated approval and enable downtime. Its parent problem is durable
execution authority with independently checked daily readiness. The root goal is one selected strategy
managing the actual TFSA across trading days until the operator pauses it.

## Accepted ongoing scope

Joseph explicitly accepts an ongoing no-transfer agreement after reviewing its limitation: the broker
adapter does not establish complete deposit/withdrawal coverage. Initial approval confirms no outside
changes since the original cash-only anchor and a commitment to pause before an outside deposit,
withdrawal or trade. Exact reconciliation checks cash, holdings and every original/owned order.
Matching balances cannot independently detect offsetting transfers. This is operator-scoped evidence,
never native proof of complete cash-flow coverage. Unexpected income or other unexplained cash
movement also blocks buying; no invented cash adjustment or hold clearance follows.

A new approval links previous session/risk hashes, pins the strategy/build and retains the original
cumulative purchase ceiling. Old permissions remain immutable history; their internal Ongoing flag
alone cannot grant this authority. Risk authorization preserves references, provenance, Through,
completed-session state and holds. Obsolete permission payloads leave only the current projection;
they remain in historical receipts. Initial conversion is limited to the unchanged cash-only trial
with no automatic orders or unresolved holds. General replacement/invested conversion is not implied.

Daily continuation reconstructs boundary cash/holdings from the anchor and exactly owned full fills.
An order spanning a boundary is ambiguous and blocks continuation. Valuation retains first five-minute
Open and exact final five-minute Close, independently of confirmation cadence. Missing/conflicting bars
cannot become restart-time values. Closes/opens advance in calendar order; only verified next-session
opening clears the previous daily hold. Overall holds and cumulative spending persist. Recovered
evidence retains its real observation time. Paper progress, balances and confirmation confer no Real authority.

Full-fill-only safeguards remain: partial, cancelled, uncertain or conflicting orders require original-ID
recovery and operator intervention. Stop new buys retains normal exits/protection; full execution pause
remains distinct. Shutdown drains admitted work and preserves explicitly approved ongoing grants.
Restart rechecks build, assignment, account, orders, calendar, source, integrity and risk. Stopped buying
stays stopped. Material changes require review.

This supersedes ADR-0105's dated expiry/separate enable only for the newly approved agreement and its
combined app action. Legacy trials/grants retain their limits. Activation remains Joseph's app action.
Additive omitted-when-absent JSON fields and append-only risk events require no schema migration.

## Accepted execution-price policy; separate implementation increment

Joseph approves a fresh broker ask as the fixed reference, at most 0.25% upward movement before
submission, and at most 0.50% bid/ask spread. Size/reserve against the bounded price; retain allocation,
cash, cumulative allowance and duplicate checks. Never ratchet the reference upward to chase rejection.
These are chosen limits, not empirical findings from the old failures. Existing regular-session market
orders remain; preflight bounds do not guarantee fill prices. Disclose the policy in the explicit review.

The October26 hash-verified read found six AskAboveDecisionReference failures (October23 revisions1247–1267),
none retaining prices or quote timestamps. It cannot distinguish stale quotes, spread, feed definition
or excessive movement. The old reference uses TMX ask with last-price fallback; preflight uses broker ask.
Retrieval time is not upstream quote freshness. Private evidence: artifacts/ongoing-live-20261026/rejections.sql,
query hash1A652107105F1F7579F23ECBFCBDFD821354B650E320265C25DEA49F8303D041;
result hashC718D31706363E37427B91FE2560E3BE98DA289BF14A2A94EC1DA62148CC956E.

## Faster confirmation: qualification pending

Requested behavior is two genuinely fresh consecutive minute observations, retaining longer indicator
horizons and requiring updated-strategy review. The October23 raw TMX sample returned22 minute rows;
at13:24 its newest event was13:01. It does not qualify minute operation. Current shared collection uses
exact five-minute bars with a two-minute offset; Real owns its confirmation. Repeated reads supply no
new bars. Changing only a timer or BarInterval is insufficient: evaluator, measurement, collection,
storage and publication need consistent successor contracts and market-hours source/latency evidence.

## Source checkpoint

Core agreement, linked risk authorization, boundary reader and automatic risk evidence adapter are
implemented.457 focused broker-session tests pass, including consent, legacy preservation, stopped
mode/restart, weekend continuation, invested exact boundary valuation, missing bars, stale/unexplained
cash, history changes and uncertain orders. Existing protection/ceiling tests remain. UI, final full
validation, installation and native multi-day/first-fill qualification remain pending. Tests grant no
operational authority.

The app review/combined approve-and-start action and persistent pause-new-buys control are now implemented. WPF Debug build passes (260 existing compiler warnings, zero errors). Financial activation and installed UI verification remain pending.
