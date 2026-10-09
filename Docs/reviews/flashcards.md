# Review Flashcards

### ADR-0088 — Source symbol aliases

- **Q:** Does translating a broker symbol change a saved trade? **A:** No. Only the monitoring reference changes; the owning account's records and history stay intact.
- **Q:** Why use a verified alias instead of removing every exchange suffix? **A:** Names can collide across listings. A source-scoped mapping identifies one existing stock explicitly and rejects uncertainty.

### ADR-0069 — Direct Wealthsimple authentication

- **Q:** Does a verified broker login permit strategy orders? **A:** No. Account reconciliation and explicit execution authority are separate boundaries.
- **Q:** Why can a captured DPoP token not be treated as bearer? **A:** It requires proofs from the original private signing key; our new client's key cannot substitute for it.
- **Q:** Why stop after an uncertain refresh failure? **A:** The server may already have rotated the refresh token. An automatic retry can reuse invalid credentials and lose session state.

Question/answer pairs distilled from ADRs and concepts. Used in review
sessions ("quiz me" / "review").

## Conventions

- One card per `### Q:` heading.
- Tag each card with the same domain tags as ADRs/concepts.
- Cite the source (`Source: ADR-0001` or `Source: concepts/<file>.md`).
- Keep answers short — 1–4 sentences. The point is recall, not exposition.

## Cards

### Q: Does Wealthsimple's quote timestamp prove when the exchange price last changed?

A: No. Its precise meaning is undocumented in the reviewed public sources. That uncertainty is
accepted for a possible limited AI fallback, with broker and receipt times separate. TMX remains
the normal source; the fallback is deferred and does not qualify a live risk feed.
Source: ADR-0087. Tags: architecture, risk-management.

### Q: Can two portfolios sharing a Delphi Live policy share buy confirmation?

A: No. They may share market judgments, but each account keeps its own holding, pending-action and
confirmation history. The AI may review another account's state without copying its permission to buy.
Source: ADR-0087 and ADR-0053. Tags: architecture, risk-management.

### Q: Does rereading a saved engine decision make it fresh?

A: No. Its original decision time still limits its age. Later performance records cannot extend that
expiry, and a paper decision does not prove that the managed account can buy or re-enter now.
Source: ADR-0087. Tags: architecture, risk-management.

### Q: Does choosing another paper portfolio give AI Momentum that portfolio's money?

A: No. The choice supplies strategy rules and their exact version. The AI's cash, holdings and loss
history still come from its own managed account. A review packet keeps those two sources separate.
Source: ADR-0087. Tags: architecture, risk-management.

### Q: Why read a paper portfolio's cash and holdings together?

A: A trade changes both. Reading them separately could combine cash from before the trade with
holdings from after it. The saved-record reader takes a consistent set and keeps the saved price
times, so rereading old records cannot make their prices fresh.
Source: ADR-0087. Tags: data-sources, risk-management.

### Q: Why can't the paper dashboard's freshest price qualify the whole portfolio?

A: One fresh holding can conceal stale prices elsewhere. The risk adapters require every holding to
match a dated checkpoint or identified source bar and reconcile current cash/history. Shadow's saved
timestamp may mean a bar start or a fill time, so it needs its source evidence before interpretation.
Source: ADR-0087. Tags: data-sources, risk-management.

### Q: Does the broker-book adapter prove that live portfolio values are qualified?

A: No. It checks scoped reconciled books, exact-time prices and no-flow evidence, saves provenance,
and blocks new allowance on invalid input. A qualified producer must still establish complete actual
holdings and cash-event coverage. A repeated read cannot extend the original evidence's freshness.
Source: ADR-0087. Tags: data-sources, risk-management, validation.

### Q: Which portfolios may AI Momentum choose?

A: All current strategy portfolios, including position-count and daily-gate variants. The catalogue
expands as new portfolios are built and integrated. Each choice retains its exact rules/version and
needs qualified adapters and current checks before executable use; switching preserves risk history.
Source: ADR-0087. Tags: architecture, risk-management.

### Q: Does the tested AI round core mean the AI portfolio can trade yet?

A: No. It calculates round transitions from qualified synthetic inputs and produces risk assessments.
Feed/cash-event qualification, durable history, full account/position controls and execution wiring
remain pending. A risk assessment alone is not order permission.
Source: ADR-0087. Tags: architecture, risk-management, validation.

### Q: Does shared risk enforcement mean every portfolio must use identical rules?

A: No. Each portfolio has an explicit approved risk configuration and its own history. AI uses
continuous rounds while ordinary portfolios retain daily rules; shared controls enforce the selected
profile and independent boundaries through one execution path. Switching engines cannot reset risk.
Source: ADR-0087. Tags: architecture, risk-management.

### Q: Does adding money erase an AI portfolio's measured trading loss?

A: No. Confirmed deposits and withdrawals adjust effective references to preserve the measured
loss percentage: 2% below the peak stays 2% below. Round identity, attempts, pauses and stock stops
remain unchanged; original capital/history and the adjustment are recorded.
Source: ADR-0083, ADR-0087. Tags: risk-management, accounting.

### Q: Does a fifteen-minute AI review interval delay portfolio loss checks?

A: No. The app checks every fresh verified portfolio valuation independently, with no AI call.
That includes qualified one-minute updates; repeated cached prices or failed polls provide no new
valuation. Current five-minute bars alone cannot establish fresh one-minute protection.
Source: ADR-0087. Tags: risk-management, architecture, reliability.

### Q: Does taking profit or selling everything start a fresh AI risk round?

A: No. Keep the same round, saved peak, limit/trigger and attempt count, even entirely in cash.
Buying again uses that round's allowance. Only hitting its hard loss limit consumes an attempt;
selling/rebuying cannot reset protection. Source: ADR-0087. Tags: risk-management, decision-engine.

### Q: Does an end-of-session learning review replace intraday learning consultation?

A: No. The post-session review evaluates completed outcomes and leaves inconclusive cases pending.
The supervisor and execution coordinator still consult saved evaluated lessons throughout the day.
All paid learning shares the monthly budget and cannot rewrite hard boundaries. Source: ADR-0087. Tags: learning, architecture, llm.

### Q: Does switching AI portfolios create fresh risk allowance?

A: No. Keep the round reference, peak, limit, attempts and review holds, even if the switch passes
through cash. Retain mutually permitted holdings and stops; confirm/reconcile required exits before
replacements and log the switch reason. Source: ADR-0087. Tags: risk-management, architecture.

### Q: Can the AI learn from a stop change it is forbidden to execute?

A: Yes. Log the desired alternative, evidence and rejection, then compare actual outcomes with a
clearly labelled simulation under defined assumptions. TraderVI code still forbids loosening or
removing the established stop; learning can inform permitted choices but cannot change that boundary.
Source: ADR-0087. Tags: risk-management, llm, learning.

### Q: What raises an AI round's exit trigger as it gains value?

A: Its highest verified portfolio value within that round. At a 5% limit, CAD 10,000 gives a
CAD 9,500 trigger; a rise to CAD 11,000 raises it to CAD 10,450, which a price decline cannot lower.
The same round drawdown drives buying allowance; the overall 10% closing-peak guard keeps its
separate history. Source: ADR-0087. Tags: risk-management, decision-engine.

### Q: Can an upfront forecast set a later AI round's enforced loss threshold?

A: No. Preserve original allocated capital, but calculate each round's initial trigger from its
actual verified opening capital. Later rounds use reconciled remaining capital; upfront forecasts
are estimates only. Verified gains then raise the active trailing trigger.
Source: ADR-0087. Tags: risk-management, decision-engine.

### Q: Does AI Momentum ration its monthly budget across trading days?

A: No. Useful reviews and learning share the monthly allowance without daily allocations or future
consult reserves; per-call reservations and token limits still apply. At exhaustion the agreed pause
applies unless the operator explicitly extends the allowance, with a fresh review still required
before resuming decisions. Source: ADR-0087. Tags: architecture, llm, budget.

### Q: Which initial events can bring an AI review forward?

A: A new qualifying entry signal, a confirmed holding closure, or a risk-limit change to permitted
actions. Combine events arriving together and retain the one-minute minimum and shared budget;
protection does not wait for the review. Later hooks use the same path. Source: ADR-0087. Tags: architecture, llm, reliability.

### Q: What happens when the AI cannot review because its provider or budget is unavailable?

A: Pause new buys, additions and portfolio switches; monitoring, stops and existing engine exits
continue. Resume only after a successful fresh AI review with all other limits satisfied. Restored
API access or budget alone does not clear the pause or other holds. Source: ADR-0087. Tags: architecture, llm, reliability.

### Q: Do the three AI consults replace Delphi Live intraday monitoring?

A: No. Intraday monitoring continues and feeds the AI; scheduled consults are additional reviews.
Adaptive AI attention cannot weaken protective checks or exceed the shared budget, and faster
AI reviews do not create fresh bars between source updates. The accepted scheduled review interval
is one to fifteen minutes during market hours, subject to budget. Source: ADR-0087. Tags: architecture, llm, reliability.

### Q: Does a new trading day start a fresh AI round automatically?

A: No. The entire sequence and attempt count carry across sessions. Round 2 stays round 2 at 3%,
with the same reference, peak and protection; its failure advances to round 3 at 1% after required checks.
After three failed, reconciled attempts, a fresh sequence is eligible next trading session with a
fresh signal and AI review, subject to other limits. The UI must distinguish this eligibility from
a carried round or approval hold. Source: ADR-0087. Tags: risk-management, architecture, user-interface.

### Q: Can a remaining AI attempt bypass the overall 10% loss review?

A: No. A 10% decline from the highest recorded completed-session closing value pauses new buys
until explicit operator approval; protective exits continue. The UI must show proximity to that
limit and the review hold even if value recovers. Source: ADR-0087. Tags: risk-management, user-interface.

### Q: When can another AI round begin after a loss threshold is reached?

A: Only after the failed round's AI-managed holdings are closed, fills and cash are reconciled,
outstanding commitments are resolved, and a fresh post-closure signal from the selected engine
qualifies for a permitted attempt. There is no extra fixed delay; the AI can take it or wait.
An acknowledgement or partial sale is insufficient. After the sequence's third failure, no new
attempt is allowed that session, even if earlier failures occurred on other days.
Source: ADR-0087. Tags: risk-management, decision-engine.

### Q: What does the AI Momentum gradual allocation scale constrain?

A: Each stock's combined buying allowance reduces as losses deepen. Initial buys and additions share that allowance; splitting orders
cannot bypass it. Shrinking the allowance alone does not force a sale. Allowance follows continuous
losses and recovery across days. The round loss fraction now measures drawdown from its highest
verified value, initialized at its opening value. The accepted 5% / 3% / 1% cycle schedule replaces
the earlier fixed 7% pause. Allowance is proportional to the unused fraction of that round's loss
limit: half the limit lost leaves half the normal allowance. Recovery can restore allowance before
the limit, but reaching it commits to closure despite a later rebound. Source: ADR-0087.
Tags: risk-management, decision-engine.

### Q: Does an existing trailing sell rule automatically restore AI buying allowance while in cash?

A: No. Position trailing floors and portfolio buying limits are separate controls. Watched stocks
can recover while an all-cash portfolio's trading value stays unchanged; automatic re-entry needs
an explicit qualifying signal and restart allowance. Source: ADR-0087. Tags: risk-management, decision-engine.

### Q: Does the new 7% daily buying-pause default change an existing portfolio automatically?

A: No. New engine templates use 7%; saved versions and historical V1 policies retain their settings.
Adoption requires an explicit saved version and assignment, preserving existing holds. AI Momentum's
later cycle schedule is separate and not yet implemented. Source: ADR-0087. Tags: risk-management, decision-engine.

### Q: How should learning reach both the AI supervisor and execution coordinator?

A: Both consult versioned evaluated lessons periodically and record their use. The coordinator reads
saved lessons without requiring another model call; protective exits never wait for learning.
Source: ADR-0087. Tags: architecture, llm, risk-management.

### Q: Can an AI Momentum rule override bypass the hard protection boundary?

A: No. The AI may reject discretionary trading rules with a recorded reason, but TraderVI enforces
the approved cash, exposure, protection and validity limits. Source: ADR-0087. Tags: llm, risk-management.

### Q: Does using the operator's Codex subscription establish a working trading client?

A: No. Authentication, exact model access, quotas, latency and recovery still need qualification.
Direct API use has separate usage-based access. Source: ADR-0087. Tags: architecture, llm.

### Q: Why does a lost AI response keep its spending reservation after restart?

A: A timeout does not prove the provider did no work. Keeping the bounded cost reserved prevents
uncertain calls and retries from silently exceeding the shared allowance. Source: ADR-0087. Tags: llm, reliability.

### Q: Why does a broker order need a synchronization receipt?

A: The same cumulative order appears in many refreshes. A stable order identity and atomic receipt
ensure its financial effects are recorded once, including after an interrupted response or restart.
Source: ADR-0084. Tags: architecture, risk-management.

### Q: Does a confirmed external sale establish new account risk references?

A: No. It updates holdings and trade history, while prior reports become stale. Existing protection and
loss holds remain; opening values and cash-flow adjustments need their own qualified evidence.
Source: ADR-0084. Tags: risk-management.

### Q: Does a deposit make a previously measured account loss smaller?

No. A confirmed cash movement adjusts both risk references proportionally, so a 2% loss remains 2%.
Existing holds stay in place. Source: ADR-0083.

### Q: Why does a completed cash activity still need matching account-value evidence?

Its occurrence time may differ from when cash affected the balance. Adjustments need that effective
boundary and matching books; otherwise market movement could be mistaken for the deposit's effect.
Source: ADR-0083.

### Q: Can a noon holdings read and a 09:30 price reconstruct the opening account value?

Only if complete account evidence establishes which shares and cash were actually present at 09:30.
The price timestamp alone cannot undo intervening trades or cash movements. Source: ADR-0083.
Tags: architecture, risk-management.

### Q: Does an unchanged cumulative net-deposit total prove there were no cash movements?

No. A deposit and withdrawal can offset. Continuing a no-flow interval requires complete reconciled
event evidence; pending or unexplained movements cannot be treated as zero. Source: ADR-0083.
Tags: risk-management, data-sources.

### Q: Does a late engine start require resetting the TFSA opening value or adding a morning pause?

No. Reuse the saved correct reference or recover a precisely dated, validated value. A later value or
previous close is not automatically the opening. Keep this logic in Core so the future scheduled engine
uses the same path; existing strategy risk checks still apply. Source: ADR-0083. Tags: architecture, data-sources.
### Q: Must a separate baseline review screen precede ongoing risk-tracking development?

No. Develop and test continuation against the existing baseline contracts. A concise summary within
account setup can support explicit saving later; an established baseline and verified storage are still
required before operational use. Source: ADR-0081. Tags: architecture, risk-management.

### Q: Does a first broker risk baseline require waiting for missing lifetime history?

No. Setup can use verified current account value where no earlier applicable baseline exists, recording
when measurement starts. It must preserve known references and holds, distinguish a failed history read
from absent records, and never label a setup value as a historical opening or lifetime high.
Source: ADR-0081. Tags: architecture, risk-management.

### Q: Who protects retained holdings during a strategy transition?

Their current owner remains responsible until required exits reconcile and activation transfers ownership.
Existing protective levels carry forward. Missing protection is initialized from actual cost and fresh
observations, not the paper holding's history; none of this independently grants order authority.
Source: ADR-0081. Tags: architecture, risk-management.

### Q: Can expected sale proceeds fund a promotion's new buys?

No. Required exits must be confirmed and reconciled first. Use the lower of fresh broker CAD cash and
buying power, then apply strategy sizing and reservations without deducting a broker-reflected reserve twice.
Source: ADR-0081. Tags: risk-management.

### Q: Why must cumulative broker totals not be summed across imports?

Each import is a complete observation of the same orders plus any new orders. Select the latest consistent
observation and use each external order ID once; summing snapshots would invent repeated purchases or sales.
Source: ADR-0080. Tags: architecture, risk-management.

### Q: Does saving broker history switch the strategy engine to the broker account?

No. The initial SQL import preserves observations and reviewed legacy references. Strategy-input ownership,
cash and risk baselines, inherited position protection and execution authority remain separate work.
Source: ADR-0080. Tags: architecture, risk-management.

### Q: How does an uncertain broker-history import response recover?

The database retains the request ID and content hash with the committed observation. The same request
can recover its receipt; a different request or changed facts must pass a fresh plan and predecessor check.
Source: ADR-0080. Tags: architecture.

### Q: What does future primary promotion change for the TFSA?

**A:** It assigns the reviewed strategy version authority to trade the permanent real account once
activation requirements hold. Paper cash, positions and fills remain separate from the TFSA's history.
Source: ADR-0073. Tags: architecture.

### Q: What happens to overlapping TFSA holdings during primary promotion?

**A:** Retain the actual position and transfer management to the incoming strategy. Do not sell just
to buy it back, copy the paper quantity, or rewrite the real purchase history. Record handover separately;
transition timing and initialization of the new strategy's protection rules still require design.
Source: ADR-0073. Tags: architecture.

### Q: Do multiples of ten guarantee whole-order execution?

**A:** No. Ten is a sizing preference, not the TSX standard board lot. A partial fill can leave another
whole-share quantity, and exits must be able to close that remainder without increasing exposure.
Source: ADR-0073. Tags: market-microstructure.

### Q: Why must the TFSA display exclude a managed TFSA?

**A:** Wealthsimple groups accounts by tax wrapper. TraderVI selects the open self-directed account
explicitly so managed assets do not change its displayed trading balance or holdings.
Source: ADR-0072. Tags: architecture.

### Q: Does loading broker holdings reconcile TraderVI's trading records?

**A:** No. A broker snapshot displays current balances and positions. Reconciliation must separately
map holdings, fractional quantities, fills, cash movements and pending orders before strategy inputs change.
Source: ADR-0072. Tags: architecture.

### Q: Does TraderVI normally need an open Wealthsimple browser after signing in?

**A:** No. Native email/password and 2FA establish the HTTP client's session. Browser verification is
offered only for an explicit provider challenge and closes after a supported token handoff.
Source: ADR-0071. Tags: architecture.

### Q: Why did ordinary parser tests miss the first browser handoff failure?

**A:** MemoryStream supports operations that WebView2's COM stream does not. The regression now uses
restricted streams and a real offline WebView2 probe, with bounded zero-offset reads before parsing.
Source: ADR-0071. Tags: architecture.

### Q: Where does TraderVI remember a Wealthsimple login?

**A:** It optionally stores access/refresh session tokens encrypted for the current Windows user in
LocalAppData. Email, password and verification codes are not saved.
Source: ADR-0070. Tags: architecture.

### Q: What does the green Wealthsimple sidebar dot prove?

**A:** The session was recently verified and has not expired. It does not prove instantaneous remote
state, account reconciliation or permission to trade; the connection window shows verification time.
Source: ADR-0070. Tags: architecture.

### Q: Why delete a saved session before refreshing it?

**A:** Refresh may rotate the credential. Removing the old copy prevents a crash or ambiguous response
from causing the next app launch to reuse a consumed refresh token.
Source: ADR-0070. Tags: architecture.

### Q: Why do gate comparisons start with two fresh accounts for each strategy?
- **Tags:** decision-engine, risk-management
- **Source:** ADR-0067
- **A:** Equal starting cash, source and timing make the daily gate profile the intended difference.
  Existing accounts have earlier holdings and interventions. Each new account independently receives
  CAD 1,000; the ten accounts are alternative outcomes, not one combined investment portfolio.

### Q: Does Portfolio rules only require a purchase when daily gates would block it?
- **Tags:** decision-engine, risk-management
- **Source:** ADR-0067
- **A:** No. It admits the accepted published ranks without daily admission gates, but preserves data
  checks, portfolio entry conditions, cash/position limits, risk holds and protective exits. It cannot
  expand Top 3/Top 5 to lower ranks, backdate a fill or turn the official Hold evidence into a pass.

### Q: Can a comparison account switch to another gate profile after it starts?
- **Tags:** architecture, decision-engine
- **Source:** ADR-0067
- **A:** No. The account's gate profile is fixed for its lifetime. A different gate experiment uses
  a fresh matched pair so historical results retain their original rules and interpretation.

### Q: Does forcing a Shadow candidate for an engineering test also force its purchase?
- **Tags:** decision-engine, risk-management
- **Source:** ADR-0064
- **A:** No. The September 8 exception admits published Top 3/5 candidates despite the late run and
  daily eligibility failure. Fresh post-adoption prices must still pass intraday entry checks, cash/slot
  limits and risk holds; a signal needs a later observed fill bar. Skips remain valid test outcomes.

### Q: Can returns from an operator-forced paper session validate the ordinary strategy?
- **Tags:** decision-engine, risk-management
- **Source:** ADR-0064
- **A:** No. The override and carried effects are engineering evidence. Existing portfolio charts still
  include those results, so retain visible dated test labels and the audit record; official daily
  recommendation evidence is not rewritten to make the candidates appear eligible.

### Q: What changes when an exploratory trial uses today's stock list and sector classifications?
- **Tags:** data-pipeline, machine-learning
- **Source:** ADR-0063
- **A:** Membership becomes an explicit present-day assumption with its real capture date, not a claimed
  historical observation. Prices and indicators still obey each historical cutoff. Survivorship and
  classification uncertainty remain alongside the newer-model limitation.

### Q: Does the first practice runner test SQL recovery or prove historical provider delivery timing?
- **Tags:** architecture, data-pipeline
- **Source:** concepts/historical-practice-implementation.md
- **A:** No. It runs the shared daily/Shadow workflows with isolated storage and assumed bar availability.
  Acquisition timestamps remain separate. SQL leases, crash resume, transport latency and load require
  their own tests; a failed run keeps traces and is explicitly replayed into a fresh directory.

### Q: Should missing historical market data prevent the normal practice trial?
- **Tags:** architecture, data-pipeline
- **Source:** ADR-0063
- **A:** No. Replay includes gaps and keeps simulated time moving so the production workflows must
  handle unavailable inputs and recover when data returns. Record effects and unexpected failures;
  do not manufacture prices, skip affected sessions or present unknown valuations as known.

### Q: How much capital belongs to each account in the first practice trial?
- **Tags:** architecture, user-interface
- **Source:** ADR-0063
- **A:** Each of the four independent Shadow portfolios starts with CAD 1,000 cash and no holdings or
  pending orders. No deposits or withdrawals occur during the trial. These are alternative strategy
  accounts; their starting balances are not pooled or divided among strategies.

### Q: What does the first practice version's exploratory model mode allow?

**A:** It allows today's exact preserved models to run on older market sessions, with a visible exploratory
label and model provenance. Market inputs must still arrive causally. Results describe today's system on
past prices and cannot establish a forecast made with only then-available training information.

Tags: machine-learning, architecture. Source: ADR-0063.

### Q: Does historical TMX access prove that a strategy had a trading edge?

**A:** No. It establishes source availability for the sampled dates and securities. Model/input eligibility,
execution assumptions, costs and comparison evidence still determine whether the historical result is useful.
The accepted 20-session starter trial bounds the first test; it is not a statistical promotion threshold.

Tags: data-pipeline, machine-learning. Source: ADR-0063.

### Q: Why separate practice data preparation from playback?

**A:** A checksummed saved dataset gives reruns the same evidence and lets playback control simulated time
without depending on provider availability. The Practice label helps users; isolated storage protects accounts.
The first milestone applies this to historical daily Delphi and four independent Shadow accounts; Live follows.

Tags: architecture, user-interface. Source: ADR-0063.

### Q: Why did a weekday 00:30 schedule miss Friday's market data?
- **Tags:** data-pipeline
- **Source:** ADR-0058
- **A:** Friday at 00:30 occurs before Friday's trading session. Collecting its completed daily bars
  requires a later invocation, including Saturday. The repaired schedule runs every calendar day.

### Q: Does absent SPY data mean positive cross-market confirmation?
- **Tags:** decision-engine, data-pipeline
- **Source:** ADR-0058
- **A:** No. The corrected strategy requires observed XIU and SPY histories at the expected completed
  session. Missing, insufficient, malformed or stale shared inputs stop the new recommendation set with
  a reason. Valid-data formulas and the four ML models stay unchanged.

### Q: What happens when Hercules finishes training a replacement model set?
- **Tags:** machine-learning, decision-engine
- **Source:** ADR-0057
- **A:** New files and training evidence are saved separately as candidates. The current strategy keeps
  its selected models until a later reviewed selection explicitly changes them.

### Q: What makes switching back to an earlier model set possible?
- **Tags:** architecture, machine-learning
- **Source:** ADR-0057
- **A:** Preserve its exact files, hashes, metadata, input behavior and strategy identity. Rollback
  selects that recorded set and adds a history event; it does not delete the newer results.

### Q: What does training/prediction feature parity fix in daily Delphi?
- **Tags:** machine-learning, data-pipeline
- **Source:** ADR-0056
- **A:** The same dated stock and XIU observations must produce the same model inputs in training and
  prediction. This restores existing relative-return inputs; it does not train models to imitate the
  deterministic trading rules or add those rules to the feature vector.

### Q: Why does a missing stock date exclude one stock, while bad required XIU data stops the corrected set?
- **Tags:** machine-learning, data-pipeline, decision-engine
- **Source:** ADR-0056
- **A:** Complete stocks still have usable individual inputs. XIU is shared, so an unusable required
  benchmark input undermines the common comparison. Neither case should silently produce substitute zeros.

### Q: Does matching model vector length prove that an old artifact is compatible with corrected inputs?
- **Tags:** machine-learning, architecture
- **Source:** ADR-0056
- **A:** No. A separately authorized review must establish the training contract for the exact bytes.
  The corrected strategy binds those model hashes and metadata; otherwise new candidate training may be
  needed. Source tests and a filled-in binding do not constitute that compatibility review.

### Q: What do we promote when a paper strategy proves successful?
- **Tags:** architecture, decision-engine, risk-management
- **Source:** ADR-0055
- **A:** The identified complete strategy: selection, entry, exit, sizing, risk and model/policy inputs.
  Its paper account stays independent. Training success or today's highest account balance is not approval.

### Q: Does selecting the recommendation champion permit real broker orders?
- **Tags:** risk-management, market-microstructure
- **Source:** ADR-0055
- **A:** No. Selection determines the approved source of recommendations. Broker execution needs separate
  permission and account/risk checks; the adapter reports actual orders and fills rather than copying paper fills.

### Q: Why can't we simply choose the highest balance in the Portfolios tab?
- **Tags:** architecture, decision-engine
- **Source:** ADR-0055
- **A:** Accounts can have different starting capital, dates, costs, coverage and drawdowns. Compare
  identified strategies on an explicit fair basis with untouched evidence; preserve each account's history.

### Q: Why may TraderVI monitor an unlinked Real holding while excluding an unlinked Ghost row?
- **Domains:** architecture, market-microstructure, risk-management
- **Source:** ADR-0044

**A:** A Real row represents a broker holding the operator says already exists, so hiding it would defeat actual-capital supervision. A Ghost row is experimental evidence and still requires a saved Delphi pick so its provenance cannot be invented or lost.

### Q: Why can an unlinked Real holding not receive the fresh-Delphi loss exception?
- **Domains:** data-pipeline, market-microstructure, risk-management
- **Source:** ADR-0044

**A:** The exception requires original-pick provenance plus a newer qualifying official Breakout publication. An unlinked holding cannot prove that chain, so it receives the ordinary conservative loss policy and remains manual-exit only.

### Q: Why are Granville indicators implemented as per-category classes rather than one big class?
- **Domains:** architecture, technical-indicators
- **Source:** ADR-0001

**A:** Categories share derived state internally (e.g., Plurality #1–#4 all
reuse the advancers/decliners comparison) but have different data
dependencies between each other. Per-category grouping matches the natural
cohesion, allows incremental implementation, and isolates failures.

### Q: Why XIU instead of the raw S&P/TSX Composite?
- **Domains:** finance-fundamentals, decision-engine
- **Source:** ADR-0002

**A:** XIU is a tradable ETF with reliable TMX data, captures the high-volume
core of the TSX where the momentum strategy actually operates, and gives us
a single benchmark across all subsystems for consistency.

### Q: Why doesn't Granville's literal #15/#16 work on XIU?
- **Domains:** technical-indicators, finance-fundamentals
- **Source:** ADR-0003

**A:** Granville's #15/#16 exploit the DJIA's *price-weighted* construction,
where a few high-priced names can carry the index. XIU is cap-weighted,
which already neutralizes that distortion. We reformulated the *idea*
("narrow leadership predicts a stalled move") as ScoreB (top-3
concentration) + ScoreC (narrowness) instead of porting the rule directly.

### Q: What do ScoreB and ScoreC measure, and why do we need both?
- **Domains:** technical-indicators, math-statistics
- **Source:** ADR-0003, concepts/price-weighted-contribution.md

**A:** ScoreB measures how top-heavy the same-direction push was (top-3
share of |contribution|). ScoreC measures how many constituents disagreed
with XIU's direction. They aren't redundant: a move can be top-heavy
without being narrow (and vice versa). The AND-gate captures Granville's
"narrow *and* top-heavy" thesis, which is what predicts a stall.

### Q: Why is the Weighting indicator one-sided (fires only on up-days)?
- **Domains:** technical-indicators, decision-engine
- **Source:** ADR-0003

**A:** Backtest showed v1-rule down-day triggers had a positive 1d forward
return — i.e., a mean-reversion bounce, not a narrow-decline warning. The
indicator is interpreted as a long-side warning gate, so firing on down-days
would change its meaning. A down-side analogue, if ever justified, becomes
its own indicator — not an expansion of this one.

### Q: Why is the price-weighted contribution called a "proxy" rather than an index?
- **Domains:** finance-fundamentals, math-statistics
- **Source:** concepts/price-weighted-contribution.md

**A:** The sum of price-weighted `contribution_i` values does *not* equal
XIU's actual cap-weighted return. We use the proxy only to derive ScoreB
and ScoreC — structural descriptors of the day's move — not to predict
returns. The proxy preserves the Granville-style narrowness signal that
cap weights would smooth away.

### Q: What is Oracle and what can/can't it do?
- **Domains:** architecture, oracle
- **Source:** oracle-rules.md, oracle-phases.md

**A:** Oracle is a strictly *downstream* LLM narration/critique/Q&A layer
over `TradeDecisionEngine`. It reads persisted `DecisionDossier` rows and
writes `LlmNarrative` rows. It must never influence scoring, ranking,
sizing, or gates (Rule R1) and must never compute numbers — only cite
dossier fields by name (Rule R2).

### Q: Why is the `DecisionDossier` the audit unit, not the live runtime state?
- **Domains:** oracle, architecture
- **Source:** oracle-rules.md (R3, R9)

**A:** Reproducibility. "The LLM said X yesterday" is only falsifiable if
the exact inputs (dossier JSON), exact prompt text, model, provider, and
temperature are all persisted. The dossier is the contract; if a fact
matters to the reasoning it belongs in the dossier, never smuggled in via
globals or live reads.

### Q: Why does Oracle cache narratives by SHA-256 of the prompt?
- **Domains:** oracle, infrastructure
- **Source:** concepts/oracle-prompt-tightening.md

**A:** Two reasons. (1) Cost — re-running Oracle for the same date with an
unchanged prompt should not hit the API. (2) Iteration — when we tweak the
prompt template, *only* dossiers whose prompt actually changed regenerate;
everything else replays from the cache. The hash is stored in
`[LlmNarrative].PromptHash`.

### Q: What is `MarketSharedContext` and what problem does it solve?
- **Domains:** oracle
- **Source:** concepts/oracle-prompt-tightening.md

**A:** A per-batch struct listing every signal that fired on ≥ 70% of
today's picks (Granville warnings, Granville confirmations, ML/rule
confirmations). Each per-pick prompt is told to *not* restate those — only
deviations or outliers. Without this, every pick's narrative says the same
thing about Trend10/Trend30/MACrossover firing.

### Q: Why strip `Confidence` and `ExpectedReturn=0` from the dossier JSON sent to the model?
- **Domains:** oracle
- **Source:** concepts/oracle-prompt-tightening.md

**A:** Two distinct failure modes. `Confidence` often equals
`CompositeScore`, so the model double-cites the same number under two
names. `ExpectedReturn=0` is read literally as *"no anticipated profit"*
when it really means *"unset"*. `DossierPromptBuilder.ProjectPerPickView`
removes both before serialization.

### Q: What GPT-5-family API differences did Oracle have to handle?
- **Domains:** oracle, integration
- **Source:** concepts/oracle-prompt-tightening.md

**A:** Two: `temperature` is locked to 1 (must be omitted), and `max_tokens`
was renamed to `max_completion_tokens`. `OpenAiLlmClient` branches on
`model.StartsWith("gpt-5")`. Also: the full `gpt-5` requires OpenAI org
verification; `gpt-5-mini` does not, and is the practical default for
Oracle's structured-data workload.

### Q: What is the FK delete order when rerunning Delphi for a date?
- **Domains:** infrastructure, database
- **Source:** concepts/oracle-prompt-tightening.md

**A:** Child-first: `LlmNarrative` → `DecisionDossier` → `DailyPick`.
Reverse order trips `FK_DecisionDossier_DailyPick`. `Delphi/Program.cs`
encodes this; anything else that deletes by date must mirror it.

### Q: Why is news/fundamentals deferred to Phase 4 rather than added now?
- **Domains:** oracle, roadmap
- **Source:** oracle-rules.md (R5), oracle-phases.md

**A:** Each Oracle phase is intentionally standalone and valuable on its
own — Phase 1 is a useful audit log even without an LLM; Phase 2 narration
works on existing structured signals. News brings new problems (dedupe,
provenance, rate limits, schema-version bump) that would delay the
highest-value lowest-risk slice — so we ship the narration loop first and
add context later.

### Q: Why use US indices (^GSPC, ^NYA) to confirm XIU's daily move?
- **Domains:** technical-indicators, finance-fundamentals
- **Source:** ADR-0004

**A:** XIU and the TSX Composite are dominated by the same large-cap basket, so they're too autocorrelated to confirm each other. TSX large-caps are macro-correlated to US large-caps on a daily horizon, so US broad indices are an independent surface: when XIU moves without the US, the move is locally driven and less likely to persist.

### Q: Why does Genuity short-circuit when US bars trail XIU by more than one day?
- **Domains:** technical-indicators, data-sources
- **Source:** ADR-0004

**A:** Cross-border confirmation only means something when the bars compared belong to the same trading session. A 1-day tolerance covers the common 'Canadian close published before US bar appears' race; anything wider would falsely report confirmation or divergence on stale data, so Genuity emits a single Neutral 'Stale US data' result instead.

### Q: Why Yahoo's chart endpoint and not TMX or Stooq for US index data?
- **Domains:** data-sources, architecture
- **Source:** ADR-0004

**A:** TMX recognized the US symbols but returned empty OHLC; Stooq's free CSV download is gated by a captcha/API-key page. Yahoo's chart endpoint returns daily OHLC with no key/cookie/crumb (browser UA required). It's wrapped behind IUsIndexDataSource so it can be swapped without touching Genuity logic if Yahoo ever changes.

### Q: Why were Granville Dullness indicators #21 and #22 deferred instead of shipped?
- **Domains:** technical-indicators, decision-engine, math-statistics
- **Source:** ADR-0005

**A:** Calibrating Option C (volume + range + close-change all subdued) on XIU 2020-2026 fired ~2.72% of days but #21's h=5 hit rate was 32-34% — anti-predictive. A sensitivity sweep over D1/D2/D3 kept #21 stuck below 50% at every horizon, and tightening the prior-trend classifier from 5-day return sign to 20-day extreme-proximity didn't help. #22's bucket was n=3-12, too small to claim an edge. The most likely explanation is regime: 2020-2026 XIU is a bull run with one short correction, so quiet days after advances are pauses-before-continuation, not exhaustion. Revisit after backfilling 2001-2019 or moving to a different universe.

### Q: Why didn't tightening Dullness's prior-trend classifier (Path A → Path B) rescue Granville #21?
- **Domains:** technical-indicators, math-statistics
- **Source:** ADR-0005

**A:** Path A used the 5-day return sign; Path B used proximity to the 20-day high/low. Both produced #21 hit rates in the 32-34% band at h=5 — well below 50%. That ruled out hypothesis #2 ("the classifier was too crude") and elevated hypothesis #1 ("the rule is regime-dependent and 2020-2026 is the wrong regime"). No threshold combination over D1/D2/D3 flipped #21 above random, which is the signature of a sample/regime problem rather than a calibration problem.

### Q: Why do Genuity #17/#18/#19 short-circuit on flat XIU days?
- **Domains:** technical-indicators, decision-engine
- **Source:** ADR-0004 (Magnitude floor)

**A:** Below ~10 bps, `sign(return)` is dominated by noise (one-cent tick on a $50 bar is ~2 bps), so 'confirming' a near-zero XIU move tells us nothing about whether the day's tape is genuine. Same-day Genuity indicators require both `|XIU return|` and `|US return|` to clear the 10 bps floor; #20 is unaffected because it operates on the 5-day return.

### Q: After ADR-0011, what is the primary ranking key Delphi uses to pick the leader?
- **Domains:** decision-engine, technical-indicators
- **Source:** ADR-0011

**A:** `DirectionEdge + RScomp` — the raw relative-strength composite is added at equal weight to the model-implied `P(up10) − P(down10)`. `DirectionEdge` alone is the secondary tiebreaker, and the engine `CompositeScore` is the final fallback. Missing RS values default to 0.

### Q: Why does ADR-0011 use raw `RScomp` instead of the volatility-normalized `CompZ` for the additive ranking?
- **Domains:** decision-engine, math-statistics
- **Source:** ADR-0011

**A:** `RScomp` and `DirectionEdge` already share a comparable scale (~±0.2 typical), so a plain sum approximates equal influence without re-scaling. `CompZ` is in units of σ (~±2), so adding it directly would silently over-weight RS by ~10× and break the "equal weight" intent.

### Q: Why did the first sector-index backfill attempt skip every symbol despite finding no historical bars?
- **Domains:** data-pipeline, decision-engine
- **Source:** ADR-0012

**A:** It used `MAX([Date]) WHERE Symbol = …` as the resume cursor, but Hermes's daily snapshot path had already seeded yesterday's date for each `^TT*` symbol. The cursor reported "up-to-date" and the multi-year gap before the earliest stored bar stayed invisible. The fix is coverage-shape gating: trigger `[FULL]` when `count < 100` or `earliest > start + 30d`.

### Q: What latent SQL bug in `SectorIndexRepository.UpsertAsync` did the backfill work expose?
- **Domains:** data-pipeline
- **Source:** ADR-0012

**A:** The `SqlDbType.Decimal` parameters for `Price`, `PriceChange`, and `PercentChange` were created without `Precision`/`Scale`, which defaults to `(18, 0)`. Values were being truncated to integers despite the column being `DECIMAL(18, 4)`. Fixed by explicitly setting `Precision = 18, Scale = 4` on all three parameters.

### Q: In TraderVI, what is a "lens" and what three things define it?
- **Domains:** architecture, decision-engine
- **Source:** ADR-0013

**A:** A lens is a self-contained way of viewing the universe, expressed as a `(thesis → gate stack → ranking key)` triple. Two lenses can share market-level inputs and per-symbol scoring yet produce different shortlists because they gate and rank differently.

### Q: In the multi-lens architecture, what is computed once and shared, versus what may differ per lens?
- **Domains:** architecture, decision-engine
- **Source:** ADR-0013

**A:** Per-symbol scoring (composite, probabilities, `DirectionEdge`) and all market-level inputs (regime, breadth, Granville, RS) are computed once and shared. Only the gate stack (`TradePipeline`) and the ranking key (`PrimaryKey`) differ per lens.

### Q: Which lens drives the executed pick, which is journaled, and how are they distinguished in the DB?
- **Domains:** decision-engine, architecture
- **Source:** ADR-0013

**A:** Continuations is executed (B1) and emits dossiers/sizing; Breakouts is journaled only (B3) and writes picks without dossiers. They are distinguished by the `[Lens]` column on `dbo.DailyPick` (`'Continuation'` vs `'Breakout'`); read APIs default to `'Continuation'`.

### Q: Why was "one shared pipeline with a mode flag" rejected for serving breakout vs continuation theses?
- **Domains:** architecture, decision-engine
- **Source:** ADR-0013

**A:** It re-creates the "tune one gate to do two jobs" anti-pattern, branches scoring/ranking with conditionals, and loses per-thesis attribution. A future third view would need another branch instead of just a new `LensDefinition`.

### Q: Which two patterns must both fire for `TrendConfirmationGate` to pass, and why is `Trend10` excluded?
- **Domains:** decision-engine, technical-indicators
- **Source:** ADR-0014

**A:** `Trend30` (multi-week uptrend) and `MaCrossover` (10/30 MA cross) must both be present. `Trend10` is excluded because it flips on routine pullbacks even while a name is still leading, so requiring it would reject healthy continuation candidates.

### Q: How does the Continuations ranking key differ from the Breakouts (ADR-0011) key, and why?
- **Domains:** decision-engine, technical-indicators
- **Source:** ADR-0014

**A:** Breakouts ranks by `DirectionEdge + RScomp` (equal-weight sum). Continuations ranks RS-first (`primaryKey = RScomp`) with `DirectionEdge` as confirmation, because the continuation thesis is *about* realized leadership, so RS should lead rather than be averaged 1:1 with forward probability.

### Q: In the Continuations lens, what role does breakout probability still play?
- **Domains:** decision-engine
- **Source:** ADR-0014

**A:** It is demoted to a soft composite input only — it no longer gates. The setup gate for this lens is `TrendConfirmationGate`, not the breakout `SetupGate`.



### Q: What two database rows does a single `buy` in TradeManager create, and what does each capture?
- **Domains:** architecture, risk-management
- **Source:** ADR-0015

**A:** A `TradeLog` row (the fill: symbol, BUY, shares, price, amount, commission 0) and an `ActivePosition` row (open risk: entry price, cost basis, `StopLossPrice` = entry x 0.90, `WarningPrice` = entry x 0.92). The sell side later realizes P&L on the position and closes it.

### Q: Why is live Wealthsimple routing not wired, and what runs instead in ghost mode?
- **Domains:** market-microstructure, architecture
- **Source:** ADR-0015

**A:** `WSTrade.PlaceOrder` needs a Wealthsimple `security_id`, which cannot yet be resolved from a ticker. Ghost mode (the default) prints a `[GHOST] Simulated Wealthsimple ...` line and only writes the database; non-ghost mode warns and still logs so the book stays accurate.

### Q: Which Delphi-linkage fields does ADR-0015 leave null, and what does that postpone?
- **Domains:** decision-engine, architecture
- **Source:** ADR-0015

**A:** `EntryComposite`, `StrategyVersionId`, and `OriginalPickId` are left null (manual entry only). That postpones model-vs-discretion attribution - comparing actual fills against Delphi's hypothetical pick for the same day.

### Q: Why is On-Balance Volume (OBV) kept out of the Granville #1–#56 plug-in framework?
- **Domains:** technical-indicators, decision-engine
- **Source:** ADR-0016

**A:** The Granville #1–#56 indicators are market-wide and read once per day. OBV is
*per-symbol* and *cumulative* (its value is anchor-relative, only the breakout shape matters).
Forcing it into that framework would break the framework's cohesion, so OBV lives as a
separate per-symbol indicator with its own table (`dbo.SymbolObv`) and classifier.

### Q: How does OBV influence the daily picks, and why isn't it in the engine composite?
- **Domains:** decision-engine, technical-indicators
- **Source:** ADR-0016

**A:** OBV is a **soft additive tilt** on each lens's ranking key (mirroring the RS pattern,
ADR-0011): `+ObvSignalWeight` when the field trend is Rising, `−ObvSignalWeight` when Falling,
0 otherwise. It is injected via `TradeDecisionEngine.ObvTilts`. It stays out of the engine
composite because that composite is built from **ML model roles** with registry weights; OBV
is a rule-based indicator, so the ranking-key seam (the same one RS uses) is the right place.

### Q: Why is pruning `dbo.SymbolObv`'s tail safe, and how does Hermes continue OBV across a gap?
- **Domains:** data-pipeline, time-series
- **Source:** ADR-0016

**A:** The running cumulative is already baked into each retained row, so deleting old rows
never changes the newer values — the newest retained row stays the anchor. Hermes
`UpdateObvAsync` reads that last stored `(date, obv)` via `GetLatestAsync`, seeds
`CalculateOBV` with it plus the prior close, and extends over every newer session, filling
multi-day gaps in one pass.

### Q: Why are active model names, data freshness, and current priorities kept out of root `AGENTS.md`?
- **Domains:** architecture
- **Source:** ADR-0017

**A:** They change more frequently than durable working rules. Root `AGENTS.md` controls how agents
work safely; `Docs/project-status.md` records what exists now, and `Docs/roadmap.md` records what
comes next. This avoids turning the instruction prompt into a stale second architecture document.

### Q: Why does Sandbox have its own nested `AGENTS.md`?
- **Domains:** architecture
- **Source:** ADR-0017

**A:** Codex layers instructions from the repository root toward the working directory. A nested
file applies the probe contract and side-effect rules only to Sandbox work while preserving the
root safety and validation rules, avoiding irrelevant probe detail in every other task.

### Q: Why is TraderDB.sqlproj built but never published under ADR-0018?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0018

**A:** The build validates that canonical schema definitions compile against SQL Server 2019. Publishing would broadly reconcile unresolved project/database drift and could rebuild or drop unrelated objects, so each live change is instead an individually reviewed manual migration.

### Q: What recovery window does TraderDB's SIMPLE model provide?
- **Domains:** data-pipeline, risk-management
- **Source:** ADR-0018

**A:** Recovery reaches the end of the newest successful full backup. Changes after that backup—such as later Delphi, Hercules, TraderVI, or Oracle writes—are exposed until the next full backup because SIMPLE recovery has no transaction-log backups.

### Q: Why does SQL Server write locally before a backup is copied to OneDrive?
- **Domains:** architecture, risk-management
- **Source:** ADR-0018

**A:** SQL Server writes as its service account while OneDrive syncs as the interactive user. Writing and checksum-verifying locally first prevents permission coupling and keeps OneDrive from treating an incomplete database-backup stream as a finished off-machine copy.

### Q: How does Hermes distinguish a successful data update with a failed backup from a fully protected run?
- **Domains:** data-pipeline, risk-management
- **Source:** ADR-0018

**A:** The completed database update remains in place, but Hermes prints a prominent backup failure and exits with code `2`. A OneDrive filename is published only after a temporary copy matches the verified staging backup's SHA-256; if copying fails, the staging backup is retained for manual recovery.

### Q: Why does Delphi reject stale history before model evaluation instead of using an `ITradeGate`?
- **Domains:** decision-engine, risk-management
- **Source:** ADR-0019

**A:** Freshness is an input-integrity invariant shared by every lens. If checked in a lens gate, relative strength, OBV, and model features would already have consumed stale bars. Pre-scoring exclusion ensures every downstream comparison uses the same completed TSX session.

### Q: How do DataAudit and Delphi's history-freshness eligibility rule differ?
- **Domains:** data-pipeline, decision-engine
- **Source:** ADR-0019

**A:** DataAudit is an independent read-only diagnostic for the whole local universe, including classification, mapping, bar integrity, and freshness. Delphi's rule is a narrow runtime defense that prevents a session-mismatched symbol from entering recommendation scoring even when DataAudit was not run first.

### Q: What exact event creates an official paper-calibration run?
- **Domains:** architecture, data-pipeline, decision-engine
- **Source:** ADR-0020

**A:** The successful append-only transaction that writes the complete `OfficialPaper` run, every model-evaluated candidate, and both lens evaluations after point-in-time inputs are known. The mutable `DailyPick` refresh is a separate operational write and neither creates nor rewrites that evidence identity.

### Q: Why does Athena use two different starting prices for prediction and tradeable outcomes?
- **Domains:** math-statistics, market-microstructure
- **Source:** ADR-0021

**A:** Model labels were trained from the observation-session close, so calibration must reproduce that contract. A real recommendation cannot fill at the same close that produced it, so paper execution starts at the first eligible session open after both the observation and run time.

### Q: What evidence is required before removing a safety gate?
- **Domains:** decision-engine, math-statistics, risk-management
- **Source:** ADR-0022

**A:** At least 120 matured official cohorts, a forty-cohort untouched forward window, adequate regime coverage, cohort-aware confidence bounds for improvement, and bounds showing no unacceptable lower-tail or drawdown deterioration—followed by explicit human approval and immutable version/ADR/code records.

### Q: Why can an opening move not automatically veto Delphi's completed-session recommendation?
- **Domains:** decision-engine, market-microstructure, risk-management
- **Source:** ADR-0023

**A:** The move may be new information, price discovery, or temporary execution noise. Opening direction must first prove incremental value as a versioned challenger; only input-integrity or execution-safety failures may be immediate hard exclusions.

### Q: Why are intraday wave trading and multi-day swing trading scored separately?
- **Domains:** architecture, market-microstructure, risk-management
- **Source:** ADR-0023

**A:** They use different holding periods, entry and exit rules, costs, risks, and opportunities. Combining them would conceal which policy produced the result and make Delphi impossible to tune honestly.

### Q: Why does Athena count prediction cohorts by `MarketDataAsOf` rather than Delphi run date?
- **Domains:** architecture, data-pipeline, decision-engine, math-statistics
- **Source:** ADR-0024

**A:** Weekend runs and deliberate reruns can use the same completed market session. They remain separate audit records, but counting them as independent cohorts would exaggerate the evidence available for tuning.

### Q: What is the difference between Athena's completion and usable coverage?
- **Domains:** data-pipeline, math-statistics
- **Source:** ADR-0024

**A:** Completion counts every terminal outcome, including invalid ones. Usable coverage counts only valid and degraded terminal outcomes; invalid and pending rows cannot support the reported performance score.

### Q: Why is `SwingMarkToMarket3` not the final Delphi swing exit policy?
- **Domains:** architecture, math-statistics, risk-management
- **Source:** ADR-0025

**A:** It records the value of a published pick at the first three session closes without applying a stop, target, trailing rule, or forced sale. It measures selector potential while the actual trade-management policy remains an open, separately versioned decision.

### Q: How does Athena treat a published pick that has no symbol bar in its first three eligible XIU sessions?
- **Domains:** market-microstructure, math-statistics
- **Source:** ADR-0025

**A:** It records terminal `NoEntry`, which counts as a valid execution observation but not as zero return. Return reports exclude it from averages and show the no-entry rate separately.

### Q: What do MFE and MAE tell us about a long Delphi recommendation?
- **Domains:** math-statistics, risk-management
- **Source:** ADR-0026

**A:** MFE is the largest unrealized gain reached from the raw entry open; MAE is the largest unrealized loss and is stored as zero or a negative return. Together they show available upside and endured downside that the closing return can hide.

### Q: Why does Athena mark some excursion ordering as `SameSessionUnknown`?
- **Domains:** market-microstructure, risk-management
- **Source:** ADR-0026

**A:** A daily OHLC bar shows the session high and low but not which occurred first. Guessing the order could make a future target-and-stop policy look executable when the data cannot prove it.

### Q: How does Athena stop repeated official Delphi runs from overweighting one market session?
- **Domains:** architecture, math-statistics
- **Source:** ADR-0027

**A:** It first averages recommendations within each run, then averages runs within the shared `MarketDataAsOf` cohort, and finally gives each cohort equal weight. Reruns remain auditable but do not create extra independent cohorts.

### Q: Why are Continuation and Breakout tradeability results not combined?
- **Domains:** decision-engine, risk-management
- **Source:** ADR-0027

**A:** The lenses express different theses with different gates and rankings. Separate reports show which selection process produced the return, path risk, or no-entry result; combining them would hide the behavior Delphi needs to tune.

### Q: Why can ADR-0028's 10% and 20% loss levels not guarantee those sale prices?
- **Domains:** data-sources, market-microstructure, risk-management
- **Source:** ADR-0028

**A:** TMX evidence is delayed and the monitor is advisory, so the market may move further before the crossing is observed and a manual sale is possible. Paper evaluation must use a price available after detection rather than awarding the earlier threshold price.

### Q: What prevents the original Delphi recommendation from bypassing ADR-0028's 10% loss alert?
- **Domains:** decision-engine, risk-management
- **Source:** ADR-0028

**A:** The exception requires the newest valid OfficialPaper run that started after entry and was durably created before the decision bar. That exact run must publish the same symbol through Breakout with probability at least 60%, direction edge at least 10%, and down probability below 35%. A newer run that omits the symbol prevents fallback to older evidence; missing or original-entry evidence cannot qualify, and no signal bypasses the 20% alert.

### Q: Why must TMX intraday requests leave `freq` unset?
- **Domains:** data-sources, market-microstructure
- **Source:** ADR-0028

**A:** TMX's current chart code uses `interval` plus Unix `startDateTime` and `endDateTime` for intraday data. Sending the obsolete `freq = "minute"` combination produced daily fallback bars; omitting it returned valid 15-minute sessions.

### Q: Why does a TMX intraday batch record receipt time separately from the bar timestamp?
- **Domains:** data-sources, market-microstructure, risk-management
- **Source:** ADR-0028

**A:** The bar timestamp describes when market activity occurred, while receipt time describes when TraderVI could first act on that evidence. Without both, a delayed paper policy could be credited with information it did not yet have.

### Q: Why must a TMX consumer distinguish the newest returned bar from the newest completed bar?
- **Domains:** data-sources, market-microstructure, risk-management
- **Source:** ADR-0028

**A:** TMX includes the currently forming interval, and the market-hours probe observed that snapshot changing before completion. A mutable forming bar may be displayed for diagnostics but cannot be treated as final policy evidence.

### Q: Does TMX offering one-minute bars mean TraderVI should poll every minute?
- **Domains:** architecture, data-sources, risk-management
- **Source:** ADR-0028

**A:** No. Bar resolution controls how precisely the observed path is represented; collection cadence controls how often TraderVI asks for updates. ADR-0045 collects every five minutes while the policy still consumes completed 15-minute bars. Completed five-minute storage is accepted because it remained gap-free and reproduced all nine comparable fifteen-minute bars exactly; one-minute evidence showed gaps.

### Q: Why is an intraday ghost-entry pilot not an official Athena tradeable outcome?
- **Domains:** architecture, market-microstructure, risk-management
- **Source:** ADR-0029

**A:** ADR-0021's official outcome uses the first eligible session open. A user-timed intraday entry has a different opportunity and fill convention, so mixing it into the official scorecard would make the evidence incomparable. The pilot tests the workflow but earns no promotion evidence.

### Q: Why may ADR-0029 observe a forming bar for entry but never use one for an exit-policy decision?
- **Domains:** market-microstructure, risk-management
- **Source:** ADR-0029

**A:** The user chooses the pilot entry at the current delayed observation, whose exact event and receipt state are recorded. An exit rule must be reproducible, so it waits for a completed direct fifteen-minute bar. Completed five-minute bars are retained later for finer evidence and exact aggregation checks, not substituted when a source slot is absent.

### Q: Why does ADR-0030 store both a poll observation and completed market bars?
- **Domains:** architecture, data-pipeline, market-microstructure
- **Source:** ADR-0030

**A:** Bars record what happened in the market; the poll observation records when and how TraderVI asked for and received that evidence, including empty or failed polls. Both are required to reconstruct a delayed decision without look-ahead.

### Q: What happens when TMX returns different OHLCV for an already stored completed intraday bar?
- **Domains:** data-pipeline, data-sources, risk-management
- **Source:** ADR-0030

**A:** TraderVI keeps the first completed evidence unchanged, marks the later poll invalid with a bounded conflict code, and surfaces the disagreement. It never overwrites the audit trail or selects the more favourable version.

### Q: Why does an automatic ghost exit use a new five-minute observation after the policy alert?
- **Domains:** market-microstructure, risk-management
- **Source:** ADR-0031

**A:** The trail or stop may have crossed before delayed evidence reached TraderVI. A later TMX receipt provides a price observed after detection; using the earlier threshold would award the paper trade a fill the system could not prove was available.

### Q: Why is the live WPF dashboard not allowed to implement its own exit rules?
- **Domains:** architecture, risk-management
- **Source:** ADR-0032

**A:** The console and GUI must use one shared, tested monitor. Keeping policy and persistence in Core prevents display code from creating a second trading behavior and lets either host reproduce the same result.

### Q: Does applying migration 012 make intraday monitoring durable by itself?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0030, ADR-0032

**A:** No. The migration creates the ledger. The shared collector must still fetch, validate, and append each poll before the dashboard can display durable receipt and completed-bar history.

### Q: Where should behavior live when both a CLI and a WPF tab expose the same capability?
- **Domains:** architecture
- **Source:** ADR-0033

**A:** In one host-neutral shared workflow returning structured results. The CLI owns text and exit codes; WPF owns presentation and interaction.

### Q: Does opening TraderVI automatically run Data Audit, Delphi, Hermes, or Athena?
- **Domains:** architecture, risk-management
- **Source:** ADR-0033

**A:** No. Tabs expose status and deliberate controls; opening the shell is not authorization to launch consequential workflows. The first Data Audit tab runs only when its read-only button is pressed.

### Q: What does opening or refreshing the Delphi tab do?
- **Domains:** architecture, risk-management
- **Source:** ADR-0034

**A:** It only reads and displays the latest persisted Continuation and Breakout recommendations. It does not evaluate symbols or write SQL.

### Q: Which component turns Delphi recommendations into paper buy, hold, and sell actions?
- **Domains:** architecture, decision-engine
- **Source:** ADR-0034

**A:** The paper controller or Trade Manager. Delphi publishes the daily thesis; the intraday paper workflow manages positions under the accepted entry and exit policy.

### Q: Why does the Delphi workspace use a typed presentation snapshot instead of parsing its console report?
- **Domains:** architecture, decision-engine
- **Source:** ADR-0035

**A:** The snapshot is a stable contract made from the same evaluated facts as the report. Parsing spacing and prose would make ordinary wording changes capable of breaking the GUI.

### Q: What is the difference between Delphi Overview and Full Report?
- **Domains:** architecture
- **Source:** ADR-0035

**A:** Overview answers what Delphi recommends and why at a glance. Full Report preserves the structured summary and detailed diagnostic text for copying and investigation.

### Q: Why does Project Docs resolve a local link against both the repository boundary and the current catalog?
- **Domains:** architecture
- **Source:** ADR-0036

**A:** Canonical root checking blocks path traversal, while catalog membership limits in-tab navigation to deliberately discovered Markdown and applies the same directory exclusions used during browsing.

### Q: Can Project Docs open a web page merely by loading or refreshing Markdown?
- **Domains:** architecture
- **Source:** ADR-0036

**A:** No. Only an explicit click on an HTTP(S) hyperlink may open the system browser; discovery, filtering, selection, rendering, and refresh are local and passive.

### Q: Why does an operator-confirmed paper entry require the actual fill price?
- **Domains:** architecture, market-microstructure
- **Source:** ADR-0037

**A:** A later TMX quote is not the operator's execution. Requiring the actual fill preserves faithful book cost, P/L, and exit evidence while avoiding an invented price.

### Q: What happens when a Breakout pick is manually added to Paper Trading?
- **Domains:** risk-management, decision-engine
- **Source:** ADR-0037

**A:** It is linked to its exact saved pick and monitored, but it is durably labelled exploratory. Continuation remains the production recommendation lens, and no selection automatically changes Delphi.

### Q: How can a model rank useful candidates while still being badly calibrated?
- **Domains:** machine-learning, math-statistics
- **Source:** ADR-0038

**A:** Its higher probabilities may correctly order events above non-events, producing useful AUC, while the probability values are systematically too high or low. Reliability buckets, Brier score, and expected calibration error test confidence honesty separately from ordering.

### Q: Why does the official prediction scorecard average candidates, then reruns, then market-session cohorts?
- **Domains:** architecture, math-statistics
- **Source:** ADR-0038

**A:** Candidates from one run share market exposure, and deliberate reruns over the same completed session are not independent evidence. Nested weighting keeps every run visible while giving each distinct `MarketDataAsOf` cohort equal final weight.

### Q: Why can a favourable diagnostic slice not automatically increase a Delphi signal's weight?
- **Domains:** decision-engine, machine-learning, math-statistics
- **Source:** ADR-0038

**A:** A slice is an association that may reflect correlated signals, regime concentration, repeated testing, or a small sample. It can justify a versioned challenger hypothesis, but only forward evidence and human approval may change the champion.

### Q: What does a TraderVI `REAL` row establish?
- **Domains:** architecture, market-microstructure, risk-management
- **Source:** ADR-0039

**A:** It records a broker fill reported by the operator, with an account label. It does not establish that TraderVI sent or verified the order, and it gives TraderVI no broker authority.

### Q: Why can an exit policy automatically close a Ghost row but not a Real row?
- **Domains:** architecture, risk-management
- **Source:** ADR-0039

**A:** A Ghost fill is simulated under TraderVI's accepted paper policy. A Real sale is true only after it occurs at the broker, so the dashboard must keep the signal open until the operator records the actual fill.

### Q: Why are Trading-tab P/L and Scorecards-tab calibration kept separate?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0039

**A:** Trading P/L depends on operator timing, execution mode, and fills. Official scorecards test immutable Delphi predictions against defined outcomes; mixing the ledgers would confound model calibration.

### Q: Which price does the delayed-intraday outcome receive after an exit alert?
- **Domains:** market-microstructure, math-statistics
- **Source:** ADR-0040

**A:** The raw fill is the open of the first five-minute bar beginning at or after the recorded detection time. The earlier policy threshold is never awarded as a fill.

### Q: Is the delayed-intraday outcome's 25-basis-point adjustment a Wealthsimple commission?
- **Domains:** market-microstructure, math-statistics
- **Source:** ADR-0040

**A:** No. The raw result is explicitly zero-commission. The separate 25-basis-point-per-side result is a conservative spread/slippage sensitivity used to test whether an apparent edge survives execution friction.

### Q: When does a delayed-intraday evidence gap become invalid rather than pending?
- **Domains:** architecture, data-pipeline, math-statistics
- **Source:** ADR-0040

**A:** It becomes invalid only when later evidence proves a required policy or exact fill bar was skipped, or when receipt order conflicts with event order. With no later proof, an apparent gap at the end of the available evidence remains pending.

### Q: Why can Athena not use the next later five-minute bar when the exact expected fill bar is missing?
- **Domains:** data-pipeline, market-microstructure
- **Source:** ADR-0040

**A:** That substitution would hide a data gap and choose a different price after seeing what evidence survived. Athena records the proven gap as invalid so every valid fill follows one reconstructible convention.

### Q: Why can relative-strength histories not be aligned by their minimum list length?
- **Domains:** decision-engine, data-pipeline, time-series
- **Source:** ADR-0041

**A:** Length contains no session identity. A short recent sector series and a long stock/XIU series can have the same selected count while representing different dates, and an interior gap can shift every later position. ADR-0041 aligns exact endpoints to canonical XIU sessions instead.

### Q: What does TraderVI do when an exact relative-strength endpoint is missing?
- **Domains:** decision-engine, technical-indicators, time-series
- **Source:** ADR-0041

**A:** The affected metric remains null and coverage reports the missing or stale-tail session. TraderVI does not clip, compress, forward-fill, or substitute another date, and pre-/post-correction official evidence keeps a visible code and strategy boundary.

### Q: Why are pre-ADR-0041 official runs excluded from the active strategy scorecard rather than marked invalid?
- **Domains:** decision-engine, math-statistics
- **Source:** ADR-0042

**A:** They faithfully record the earlier implementation, so they are valid historical evidence. They are excluded because combining them with the corrected strategy would describe two implementations as one unchanged system.

### Q: What changes when `v3.1-rs-date-aligned` is activated?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0042

**A:** The strategy and code identity changes, and active comparative cohorts restart under that identity. Thresholds, models, gates, ranking formulas, and execution policy are cloned unchanged.

### Q: How does TraderVI distinguish genuine zero active breadth from unavailable movers data?
- **Domains:** data-pipeline, data-sources, technical-indicators
- **Source:** ADR-0043

**A:** A genuine zero has a positive reported basket with equal advancing and declining counts. Unavailable data stores all three mover fields as null; `0/0/0` is never treated as a market reading.

### Q: Why can leadership not filter out missing mover rows and calculate over the remaining observations?
- **Domains:** decision-engine, technical-indicators
- **Source:** ADR-0043

**A:** Filtering would compress time across gaps and make stale observations appear consecutive. Leadership requires the newest 12 sessions to have contiguous mover coverage, otherwise the affected indicators remain neutral/no-data.

### Q: Why does the leadership-missingness correction start a new strategy identity?
- **Domains:** decision-engine, data-pipeline
- **Source:** ADR-0043

**A:** Preventing fabricated zero and falling votes can change Granville scores or gates. A new identity keeps pre- and post-correction evidence attributable even though thresholds, weights, models, and execution policy are unchanged.

### Q: Why must the strategy-identity check explicitly require both fields to be non-null in its populated branch?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0042, ADR-0043

**A:** SQL `CHECK` constraints accept `UNKNOWN`. A length expression over one null field is unknown, so explicit non-null predicates are required to make a partial `InitialCodeCommit`/`DecisionRef` pair fail at the database boundary.

### Q: Why can TraderVI collect every five minutes while retaining a 15-minute exit policy?
- **Domains:** architecture, market-microstructure, risk-management
- **Source:** ADR-0045

**A:** The scheduler controls when source evidence and snapshots are refreshed; the policy consumes only completed 15-minute bars. Intermediate ticks can add five-minute evidence but cannot invent a new policy event or change the accepted thresholds.

### Q: Why do wide TMX chunk requests end one minute before the next chunk begins?
- **Domains:** data-pipeline, data-sources, market-microstructure
- **Source:** ADR-0045

**A:** TMX treats both bounds as inclusive and can revise a boundary bar between adjacent requests. Minute-contiguous, non-overlapping windows retrieve each aligned bar once while the merge validator still rejects any unexpected conflict.

### Q: Why does the guarded nightly pipeline run shortly after midnight instead of immediately after the TSX close?
- **Domains:** architecture, data-pipeline, decision-engine
- **Source:** ADR-0046

**A:** Delphi records the recommendation run date. Starting after midnight lets Hermes ingest the prior completed session and lets Delphi publish for the new calendar day rather than dating advice after that day's market has already closed.

### Q: How does the nightly runner safely use the latest source on every scheduled run?
- **Domains:** architecture, risk-management
- **Source:** ADR-0046

**A:** It fingerprints the current Git working tree, performs non-incremental Release builds without restoring dependencies, and fingerprints the source again before any operational stage starts. A concurrent edit or build failure stops the pipeline, while per-stage artifact hashes ensure each program runs the output produced by that preflight.

### Q: What is Codex allowed to do when the nightly pipeline fails?
- **Domains:** architecture, risk-management
- **Source:** ADR-0046

**A:** It may read the durable status and referenced log, explain the evidence, and recommend a safe next step. It may not retry a program, change SQL, call a market service, repair data, or expand the authorized workflow.

### Q: How is a confirmed Real fill from a Delphi Hold row attributed?
- **Domains:** architecture, decision-engine, risk-management
- **Source:** ADR-0047

**A:** It is stored as an unlinked discretionary Real holding: `OriginalPickId` and `EntryComposite` remain null, while the selected row's ID, date, lens, rank, and Hold direction are retained in notes as context rather than recommendation provenance.

### Q: Why can a Real entry override a Hold row while a Ghost entry cannot?
- **Domains:** market-microstructure, risk-management
- **Source:** ADR-0047

**A:** The Real entry reconciles a broker fill the operator says already happened, so omitting it would make the operational ledger incomplete. A Ghost fill has no independent broker fact and remains restricted to saved Buy recommendations so experiments retain valid Delphi provenance.

### Q: What must a successful zero-result Delphi rerun do to same-date operational recommendations?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0048

**A:** Atomically replace the projection with an empty one, deleting stale narratives, dossiers, picks, and Granville logs for that date. It must not leave an earlier same-date result visible.

### Q: Why is immutable calibration evidence outside Delphi's operational-publication transaction?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0048

**A:** Calibration evidence is an append-only record of the evaluation, while picks, dossiers, narratives, and Granville logs are a replaceable same-date operational projection. Combining their retry semantics would blur those distinct contracts.

### Q: What does TraderVI do when a Real exit is partial or carries commission?
- **Domains:** architecture, risk-management
- **Source:** ADR-0048

**A:** It refuses the version-1 workflow. The operator may confirm only one all-shares, zero-commission fill; partial-fill lifecycle and commission accounting remain an explicit open decision.

### Q: Why must `DailyBars` enforce `(Symbol, Date)` when the repository already uses that pair in MERGE?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0049

**A:** A MERGE predicate describes how one writer searches; it does not prevent another or concurrent writer from creating duplicate natural sessions. The database uniqueness rule makes the repository's identity assumption true for every writer.

### Q: Why does migration 018 validate Quotes before trusting its existing foreign key?
- **Domains:** data-pipeline, risk-management
- **Source:** ADR-0049

**A:** An enabled but untrusted foreign key protects new writes without certifying older rows. The migration first proves there are no orphan Quotes, then asks SQL Server to check existing rows and mark the relationship trusted.

### Q: Why is TraderDB built in a separate CI job from the .NET applications?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0050

**A:** The original SQL project requires a pinned Visual Studio/SSDT toolchain. A separate job makes that dependency and its failures explicit without coupling them to Core tests or focused application builds.

### Q: Why can the SSDT CI job not deploy TraderDB?
- **Domains:** architecture, risk-management
- **Source:** ADR-0050

**A:** It receives no database credentials, invokes only the Build target with deployment disabled, uploads no DACPAC, and is backed by the project's target that rejects deploy operations. Live changes still require an authorized manual migration.

### Q: Why does Shadow V1 run four independent portfolios with the full starting capital in each?
- **Domains:** architecture, decision-engine, risk-management
- **Source:** ADR-0051

**A:** Each is an alternative answer to “what if this one policy controlled the available TFSA capital?” Sharing or summing their cash would mix Continuation/Breakout and Top-3/Top-5 policies and make the comparison meaningless.

### Q: Why is a Shadow signal persisted before its virtual fill?
- **Domains:** market-microstructure, risk-management
- **Source:** ADR-0051

**A:** The signal becomes knowable only after a completed bar is received. Filling at that bar's close would use an already-past price; Shadow waits for the first later five-minute bar and applies friction to its open.

### Q: What happens when a Shadow pending buy misses its exact next-bar fill window?
- **Domains:** market-microstructure, risk-management
- **Source:** ADR-0051

**A:** The order is cancelled rather than filled at a later convenient open. The frozen candidate must pass the full entry test again using current five-minute evidence before a new pending order can be created.

### Q: Why does a Shadow position persist the last consumed fifteen-minute bar start?
- **Domains:** architecture, market-microstructure, risk-management
- **Source:** ADR-0051

**A:** It makes trailing-state processing idempotent across polls and restarts. A bar may arm or raise a stop once, but its earlier low can never be retested against the stop that same bar established.

### Q: Can Athena silently use Shadow portfolio returns to calibrate Delphi?
- **Domains:** architecture, decision-engine
- **Source:** ADR-0051

**A:** No. Athena measures immutable candidate outcomes at fixed checkpoints, while Shadow measures one capital-constrained selection and execution policy. They remain separate evidence streams unless a future reviewed design explicitly joins them.

### Q: Why does a dirty working tree no longer make an otherwise complete Delphi run degraded?
- **Domains:** architecture, data-pipeline, decision-engine
- **Source:** ADR-0052

**A:** Working-tree state answers where the run came from, while audit state answers whether its captured trading evidence is usable. TraderVI preserves `Dirty` visibly but does not treat source-control hygiene as an evidence defect.

### Q: What still makes Delphi provenance invalid after ADR-0052?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0052

**A:** An unavailable code commit or incomplete loaded-model identity/hash still makes the official run invalid. A dirty run also cannot be reconstructed from the commit alone, so its working-tree marker must remain visible.

### Q: Why must Delphi Live keep observing a candidate after dismissing it from active ranking?
- **Domains:** data-pipeline, decision-engine, math-statistics
- **Source:** ADR-0053

**A:** The later evidence reveals whether dismissal avoided a loser or rejected a delayed winner. Stopping collection at dismissal would preserve more winner histories than loser histories and bias both calibration and future training.

### Q: What role may DotLLM play when a user asks why Delphi Live changed a recommendation or Shadow exited?
- **Domains:** architecture, decision-engine, llm, risk-management
- **Source:** ADR-0053; `Docs/oracle-rules.md`

**A:** It may translate persisted deterministic facts, rule results, veto codes, and action history into plain language. It may not invent causality, calculate the decision, change a score, trigger an action, or claim a reason absent from the durable record.

### Q: Why can one clean Delphi Live observation restore Data Confidence without restoring entry eligibility?
- **Domains:** data-pipeline, decision-engine, risk-management
- **Source:** ADR-0053

**A:** The clean exact stock/XIU pair proves the current collection cycle is healthy, so the consecutive-miss ladder resets. It cannot recreate a missing bar or repair a rolling window; each family must independently remature from the required contiguous evidence before a Buy is possible.

### Q: Why does an inactive Delphi Live preview show more rows than the eligible live source?
- **Domains:** architecture, data-pipeline
- **Source:** ADR-0054

**A:** The preview explains all published daily picks, including rows blocked by daily gates. Displaying a row does not grant entry eligibility or make an older daily run fresh for another session.

### Q: Why is the Friday replay's profit/loss an estimate rather than clean live evidence?
- **Domains:** data-pipeline, market-microstructure
- **Source:** ADR-0054

**A:** Historical bars do not recover original receipt times or executable quotes. The replay assumes publication timing, uses minute-price proxies, leaves missing bars missing and excludes costs; it cannot count toward operational shakedown or promotion.

## Delphi model-set and threshold settings (ADR-0059)

**Q:** Why does editing a daily gate create a new strategy version?
**A:** Past forecasts and outcomes must keep the exact settings that produced them. A new version starts separately identified evidence while preserving the original for review or rollback.

**Q:** Does selecting a trained model set replace recommendations already on screen?
**A:** Browsing a set changes nothing. Save Version also leaves assignments alone. Explicit Assign updates the shared selection and starts an official daily reevaluation; successful publication replaces current recommendations while preserving prior run evidence (ADR-0060).

**Q:** How do model signal thresholds differ from strategy gates?
**A:** Signal thresholds label individual model outputs. Strategy gates determine whether the combined candidate qualifies. The settings page preserves signal thresholds and edits only the eight persisted daily gates.

## Central settings and ownership (ADR-0060)

**Q:** Does one global Settings page mean all portfolios share the same trading rules?
**A:** No. It is one navigation destination. Trading rules and limits belong to the assigned strategy; each account retains its own capital, holdings and history. The initial design adds no duplicate account-level trading caps. Each change's scope must be shown before application.

**Q:** What changes when a revised strategy is saved under the accepted central-settings design?
**A:** A new version becomes available; existing systems and portfolios keep their assignments. Assigning that version is a separate explicit action, subject to system eligibility and a defined effective boundary.

**Q:** What happens to existing positions when a new strategy version is explicitly assigned?
**A:** Assignment is blocked while holdings or pending orders remain. Pause new buys, close holdings separately, then save/assign while paused and empty. Resume explicitly. Completed fills and earlier decisions remain immutable history.

**Q:** Does splitting account returns at an assignment boundary prove the new strategy's performance?
**A:** No. It shows what happened while that version governed the account, but inherited holdings and cash reflect earlier decisions. Clean strategy comparison needs a prospectively defined study with comparable conditions; mixed-origin trades remain descriptive account evidence. See `concepts/settings-system-map.md`.

**Q:** Under the accepted revision to ADR-0060, does Assign close holdings so a new version can start?
**A:** No. It must remain blocked until holdings and pending orders are gone; selling and order resolution are separate actions. This supersedes the earlier with-holdings design. Trading-rule editing and saving also require the affected family to be paused and empty; saving remains separate from assignment.

**Q:** Does Pause new buys disable protective exits or clear loss-review holds?
**A:** No. It blocks new entries and cancels pending internal buys. Exits continue. Resume retains risk holds and uses the explicitly assigned version.

**Q:** Do edits on different rule tabs require separate strategy versions?
**A:** No. The tabs share one retained draft and one combined review/save. The saved version is assigned separately; there is no cross-system Save All.

**Q:** Why do Portfolios returns require matching saved start and end dates?
**A:** Different dates expose accounts to different market conditions. Missing endpoints remain unavailable; the comparison does not shift one account's window to manufacture a result.

**Q:** Does maximum closing decline measure the worst intraday loss?
**A:** No. It measures the largest observed fall from a prior closing-value peak in the displayed period. Intraday moves and missing observations cannot be inferred from those closes.

**Q:** Does Review promotion assign the selected paper strategy to Trading?
**A:** No. It prepares the complete review. You must explicitly approve with a reason to change Trading's source. Performance gaps are disclosed and advisory in manual mode; the existing paused/empty account rules remain. See ADR-0062.

**Q:** What must a complete strategy identity add to the daily model assignment?
**A:** Its selection, entry, exit, sizing, risk and execution/data contracts, with exact policy and code
versions. Daily models are shared inputs. A Live champion is a role in its paper family, not Trading
approval. The complete route is implemented in source under ADR-0062, awaiting separate migration/activation.

**Q:** Why can missing account cash block a new recommendation without stopping protection?
**A:** New risk requires current account capacity and risk baselines. A known holding can still be
evaluated for protection using available evidence. Neither calculation makes a simulated fill real.


**Q:** When do performance-evidence restrictions become enforced for common promotion?
**A:** They are deferred to the Wealthsimple live trading/automation design. Manual recommendation
approval may accept disclosed evidence gaps. This never makes paper fills real or waives internal Live
research rules, and it grants no broker authority.

**Q:** Does the September 8 Delphi Live engineering admission make the daily Hold candidates eligible?
**A:** No. ADR-0065 records a distinct admission and preserves all original eligibility and gate facts.
Live still needs fresh confirmation and its ordinary safety and execution evidence.

**Q:** Can a late-start Live test become a clean session after several successful polls?
**A:** No. Fresh observations can rebuild rolling confirmation, but cannot repair missing opening
coverage or session VWAP. The host gap remains and excludes the session from clean research evidence.

### Q: Why can a successful morning recovery still be unsuitable for today's new paper entries?

**A:** Daily inputs must stop at the prior completed session; the publication must also meet the strategy's
pre-open cutoff. Late recovery cannot erase missed host coverage or convert a Hold into an eligible Buy.

Source: ADR-0058; ADR-0065. Tags: operations, data-quality, execution.

### Q: Does bypassing the first failed daily gate establish that a candidate can be bought?

**A:** No. Delphi's normal pipeline stops at its first failure. A selected-gate experiment must evaluate
the remaining gates; portfolio entry and execution checks still apply afterward. On September 9,
bypassing Breadth exposed a Granville block for all 207 candidates in each lens.

Source: concepts/paper-source-refresh-and-gate-profiles-draft.md (design in progress).
Tags: decision-engine, risk-management.

### ADR-0066 — Daily source adoption

- **Q:** Why does adopting a late Delphi publication not repair opening coverage? **A:** Adoption changes the current source prospectively; original freezes, missing observations and actual receipt times remain evidence.
- **Q:** What survives a source change? **A:** Cash, holdings, completed fills, protective sells, risk guards and exit history. Pending buys requalify from fresh evidence.
- **Q:** Does source refresh bypass daily gates? **A:** No. Separate, explicitly labelled paper profiles are needed to compare portfolio rules without those gates.

### ADR-0068 — Main-account Trading overview

- **Q:** Does stopping portfolio-screen refreshes after hours stop its trading engine? **A:** No. Display refreshes and engine wakeups are separate. The grid can retain saved results while scheduling, session finalization and safety checks continue.
- **Q:** Can a new portfolio show a daily gain when its shared-period return is unavailable? **A:** Yes. Daily gain compares the selected session's saved value with its own opening value. One session is enough; an intraday checkpoint is labelled Partial, and missing valuations remain unavailable.
- **Q:** Do fixed gate-comparison accounts need their own destination? **A:** No. All paper portfolios appear in one grid, with explicit gate-profile labels. Sharing a list does not remove the need for matching performance periods or change promotion eligibility.
- **Q:** What leads the Trading overview? **A:** The main real account's earnings, value, holdings and active strategy. Attention items support that overview; independent paper portfolios have their own workspace.
- **Q:** Does promoting a paper strategy transfer its profits or holdings to Trading? **A:** No. The approved rules use the real account's own state. Its financial history continues across strategy changes, while paper accounts and results remain separate.

### ADR-0073 — TFSA transition preview

- **Q:** Does a verified empty pending-order feed prove the account is reconciled? **A:** No. Completed fills, cash movements and TraderVI's durable records still need reconciliation. Submitted quantity also differs from remaining quantity after a partial fill.
- **Q:** If risk permits 37 shares but cash permits 27 at the limit, how many does the ten-share preference propose? **A:** 20. Rounding is downward within both limits. If only seven fit, seven is allowed; a whole-share exit need not be a multiple of ten.

### ADR-0074 — Market-buy acknowledgement and recovery

- **Q:** Does `soOrdersCreateOrder.order.orderId` mean the shares filled? **A:** No. It acknowledges creation. A separate, scoped order-details response reports cumulative fill quantity, price, value and fees.
- **Q:** What should happen after an uncertain submission? **A:** Keep the original request ID and read/reconcile its broker state. A local attempt marker prevents automatic replay; a missing or delayed lookup is not proof that the original order failed.

### ADR-0075 — Manual market-buy ticket

- **Q:** Why does the current Real tracking form require a fill price, while a market-buy ticket should not? **A:** Tracking records an already completed broker trade. Select a stock and use Manual buy (or Buy more for a holding) to review a quantity order; the broker later supplies actual fill quantities and prices. Selection and an estimated cost are not a completed fill.
- **Q:** Does buying manually from a Delphi Hold row make it a strategy trade? **A:** No. The ticket preserves the saved source and Hold direction as context, labels the order Manual, and leaves strategy attribution and automatic management unchanged. The operator chooses the exact whole-share quantity; portfolio sizing is separate.
- **Q:** Does an affordable estimated market order guarantee the final cost? **A:** No. Current ask, cash and buying power are preflight checks. The submitted quantity order has no limit price; actual cumulative fills supply the final price and costs.

Source: ADR-0075; concepts/trading-workspace-redesign-draft.md#manual-market-buys--september-10-design-draft.

### ADR-0076 — Market-sell protocol

- **Q:** Can an instrument be sellable when it is not buyable? **A:** Yes. Sell preflight checks the broker's `sellable` flag independently; a restriction on new purchases must not be mistaken for a sell restriction.
- **Q:** Can changing order side bypass an uncertain attempt? **A:** No. Buy and sell share an identity/request-ID fence. Preserve the original intent and reconcile its broker state before making another trading decision.
- **Q:** Does a successful sell eligibility check prove enough shares are available? **A:** No. It verifies the instrument. A sell workflow must also verify available holdings, pending orders and durable reviewed authority before submitting.

### ADR-0077 — Manual holdings sell-all

- **Q:** Does Market sell all silently adopt a changed broker quantity? **A:** No. The click authorizes the entire displayed whole-share quantity. A mismatch stops submission and requires refresh plus a new click.
- **Q:** Why can Filled still block another manual order? **A:** The holdings snapshot may lag the fill. The saved sell remains reserved until fresh owned-account holdings show exactly the expected remaining shares and no pending orders.
- **Q:** Can the current Limit sell ticket place an order? **A:** No. It is an editable preview with disabled submission; the HAR verified market orders only.

### Q: What does latest source mean for an isolated nightly run?

**A:** The verified source captured at attempt start, including uncommitted edits and deletions. Later edits belong to the next run; the captured build stays unchanged.

Tags: architecture, data-pipeline. Source: ADR-0078.

### ADR-0079 — Reconciliation before portfolio execution

- **Q:** Why must repeated order-status reads not create new fills? **A:** Each response is a cumulative total. Rebuild or apply only a verified change; applying the total repeatedly would invent shares and costs.
- **Q:** What does a matching reconciliation establish? **A:** Reconstructed shares and book values match observed holdings within the stated scope. It does not establish complete cash history, account risk baselines, strategy qualification or trading authority.
- **Q:** What is the accepted first portfolio handover sequence? **A:** Retain overlapping holdings, market-sell required non-overlaps during regular hours, reconcile confirmed exits, then consider new buys under the approved strategy and real-account risk rules.

- **Q:** Why can completed activity say FILLED while historical order details say POSTED? **A:** The captured broker views use different status representations. Preserve the source status and require complete fill facts; a status-only posting advance must not create a second fill.
- **Q:** When can the history reader retain an expired or cancelled order with a blank quantity? **A:** Only for EXPIRED, CANCELLED or CANCELED when all selected execution fields are explicitly null, activity reports no fill and prior confirmed fills are preserved. Save the blank-source flag; never omit the order or treat arbitrary missing quantities as zero.
- **Q:** Would starting from today's verified account remove the need for synchronization? **A:** No. A baseline can avoid reconstructing every old order, but future fills and account changes still need synchronization, and current holdings, costs and risk/protection state must carry forward.
- **Q:** Does accepting historical ETF fills allow the strategy to buy ETFs? **A:** No. Accounting for completed history and eligibility for new trades are separate checks; current unsupported holdings still block handover.
- **Q:** Why can a ticker suffix match while a proceeds difference cannot? **A:** A unique Canadian listing tied to the same broker security can verify the spelling difference. Conflicting financial values remain separate evidence and require explicit resolution; similarity cannot overwrite either source.
- **Q:** Can a high-precision SQL timestamp column still receive a rounded value? **A:** Yes. A lower-precision parameter can round the value before storage. Bind `DateTime2` explicitly; recover existing receipts only through the exact known old encoding while preserving their original JSON and strict financial checks.

### Q: Why can nightly preparation retry without retrying Delphi?

**A:** Source capture and dependency download precede operational stages. Repeating preparation cannot duplicate official recommendations; repeating Delphi can require a separate reviewed publication decision.

Tags: architecture, data-pipeline. Source: ADR-0078.

### Q: What happens when a dependency changes but its lock file does not?

**A:** Locked restore fails before Hermes. Regenerate and review the lock file with the dependency change; the night must not silently select another package graph.

Tags: architecture, data-pipeline. Source: ADR-0078.

- **Q:** How can compact storage preserve historical portfolio state? **A:** Select immutable item versions at or before the original revision, use its saved array counts and verify the fully reconstructed fingerprint. Never join a historical root to current holdings. (ADR-0082)
- **Q:** When may backup retention delete older generations? **A:** Only after the new SQL backup passes checksum verification and its destination copy matches SHA-256; keep the latest two completed backups in each location. (ADR-0082)
- **Q:** Does releasing SQL pages immediately free Windows disk space? **A:** No. SQL can reuse those pages internally; physical file shrinking is a separate reviewed operation. (ADR-0082)

- **Q:** Can an uncertain order be retried with a new request ID after restart? **A:** No. Recover the original ID; only confirmed terminal state and reconciled holdings can release its reservation. (ADR-0086)
- **Q:** Why must continued references replace cached engine references? **A:** Keeping a pre-withdrawal high in memory would turn withdrawn capital into a false loss despite correct database continuation. Holds still persist. (ADR-0086)
- **Q:** Does a successful day graph qualify the real opening? **A:** No. It does not prove first publication timing, the strategy's valuation method, effective cash events or matching boundary books. (ADRs 0083, 0086)

- **Q:** Does promoting a portfolio copy its paper cash and positions into the TFSA? **A:** No. It selects the reviewed rules; real cash, holdings, costs and fills remain the account facts. (ADR-0085)
- **Q:** Does ongoing strategy selection guarantee that it can trade tomorrow? **A:** No. Selection persists, but that day's account, source and risk checks still determine whether orders are allowed. (ADR-0085)

- **Q:** Does handing a manual holding to the strategy create a new purchase? **A:** No. Actual shares, cost and entry history remain; management and protection responsibility transfer at an explicit boundary. (ADR-0085)
- **Q:** Can a five-minute background refresh replace the account checks immediately before an order? **A:** No. Account changes can happen between polls; current cash, holdings, reservations and order state still need verification. (ADRs 0079, 0084–0085)

- **Q:** Does a manual extra consume one of Breakout 3's automatic positions? **A:** No. It receives the strategy's sell/protection rules while the strategy manages its own three positions. The extra still uses cash and contributes to TFSA risk; selling it creates no automatic replacement. (ADR-0085)
- **Q:** Does a successful background account read resolve an uncertain order or establish risk references? **A:** No. It refreshes observed facts and can append already-linked history only when reconciliation passes. The original order and risk workflows still own recovery and reference checks. (ADR-0085)
- **Q:** Does Stop new buys stop the strategy from selling existing holdings? **A:** No. Ordinary sell/protection rules continue for managed holdings; no automatic additions or replacements are bought. Core uses a separate saved purchase control; the full execution pause still blocks exits. TFSA controls are source/tested, while the execution host remains unfinished. (ADR-0085)
- **Q:** Does restarting TraderVI turn stopped buying back on? **A:** No. Restore the same approved strategy and saved buying mode after fresh account/order checks. Preserve risk holds and original uncertain order IDs; restart does not renew expired authority. (ADR-0085)
- **Q:** Does an ongoing assignment reset risk or spending limits each morning? **A:** No. Verified risk continuation owns the new daily reference; capital holds, protection and order history persist. The reviewed cumulative purchase ceiling is not refilled by a restart or date change. (ADR-0085)
- **Q:** Can a rebound erase a required exit missed while the app was closed? **A:** No. Replay protection and closing rules from the saved boundary. Missing required closing evidence blocks further strategy orders without moving that boundary forward. (ADR-0085)
- **Q:** Does verifying a complete portfolio fingerprint require keeping a second full JSON document in memory? **A:** No. Stream the same canonical bytes into the hash, then materialize records with shared immutable report strings. Preserve every historical item and the original fingerprint contract; bounded history loading is a separate change. (ADR-0082)

- **Q:** Why does automatic linked-account synchronization avoid a second human-review read? **A:** Market prices can change account value while completed trade facts stay identical. Save the latest complete reconciliation and revalidate binding, history and ledger transactionally; first/manual approval retains its separate second-read check. (ADR-0084)

- **Q:** Can missing AI round history be replaced with a fresh round on restart? **A:** No. Missing, corrupt or interrupted history requires reviewed recovery. Restore the saved round, peak and attempt count; fresh inputs and independent risk checks still govern trading. (ADR-0087)
- **Q:** Does retrying a saved risk command return its old buying allowance? **A:** No. An exact retry returns current state without another transition. Reusing its identity with changed input is rejected. (ADR-0087)
- **Q:** How can an unchanged cash-only paper account provide a current risk value? **A:** Read the complete locked book, confirm there are no open holdings and reconcile cash against fixed capital and closed trades. Record that verification time. Replaying the receipt still expires; held stock prices cannot gain freshness this way. (ADR-0087)
- **Q:** Why read the account again after collecting faster prices? **A:** A purchase, sale or hold may have changed during collection. Reject a changed book rather than combine prices with the wrong cash or shares. (ADR-0087)
- **Q:** Does a freshly calculated paper value immediately change AI risk allowance? **A:** No. It must retain source evidence and pass durable conflict/history checks through the existing risk journal. Price timestamps and saved holds still apply. (ADR-0087)
- **Q:** Does restarting fix a changed price for an already-used market minute? **A:** No. The conflicting evidence and buying block stay in the risk journal. Reviewed source recovery must preserve the round and any required exit. (ADR-0087)
- **Q:** Do several important events require several immediate AI calls? **A:** No. Combine their evidence into one review, keep one review active and preserve the minimum interval and shared budget. Events arriving during a call still need a later fresh review. (ADR-0087)
- **Q:** Does renewed API access or available budget clear a review failure? **A:** No. A successful fresh review is still required, and it cannot clear separate account, risk or operator holds. Scheduling history also needs durable recovery before automatic use. (ADR-0087)
- **Q:** What happens if the app closes during an AI review? **A:** Recover its original attempt as interrupted, preserve pending events and any spending reservation, then require a new fresh review under a new ID. Restarting never retries the old paid request or restores old permission. (ADR-0087)
- **Q:** Can an AI request to review again in fifteen minutes delay a protective check or keep old prices valid? **A:** No. It only sets ordinary AI attention timing. Fresh-data expiry, earlier events/checkpoints, budget checks and independent protection still apply. (ADR-0087)
- **Q:** How do we avoid false account timestamp failures without loosening freshness? **A:** Stamp the complete read with the app's clock while database locks still protect it. Keep strict read bounds, saved trade times and price expiry; never refresh a receipt after releasing the locks. (ADR-0087)
- **Q:** Does wanting AI reviews to tolerate small data gaps change today's input checks? **A:** No. Keep current checks while connecting the coordinator; revisit a concrete blocker with source ages and missing pieces explicit. AI choice of the active portfolio still preserves the agreed switching and risk history. (ADR-0087)
- **Q:** Must an AI call be repeated if saving its answer to SQL fails? **A:** No. The coordinator keeps the answer in the local learning journal and can retry only the SQL save. Saving history does not restore trading permission. (ADR-0087)
- **Q:** Does starting the first review from Breakout 3 with skipped daily gates lock the AI to that strategy? **A:** No. It supplies the initial cash and holdings; the AI still evaluates the catalogue and may decide to switch while preserving the agreed history and protection. (ADR-0087)
- **Q:** Does the five-minute starting interval fix AI reviews at five minutes forever? **A:** No. The AI can request its next review in 1–15 minutes; session checkpoints and events can bring it forward. (ADR-0087)
- **Q:** Is the 20% API cost headroom an extra provider charge? **A:** No. It is conservative local budget accounting for tax/fees; actual charges still need verification. The shared CAD 90 ceiling and recorded spending remain in force. (ADR-0087)
- **Q:** Does accepting ten-minute-old inputs for the first AI test establish a permanent freshness rule? **A:** No. It permits the review-only test under existing checks. Continuous operation still needs qualification; trading protections and actual source timestamps remain unchanged. (ADR-0087)
- **Q:** Does lossless compression prove that the AI can use the smaller request? **A:** No. Expanding it back and comparing proves the information survived. An exact token count must also fit the cap, and a separate model test must verify interpretation. (ADR-0087)
- **Q:** Does a concise AI input replace the full account and signal records? **A:** No. The shorter view declares its omissions; full evidence stays saved, and original eligibility, risk, identity and spending checks still apply. A token-count pass does not prove the model understands the view. (ADR-0087)
