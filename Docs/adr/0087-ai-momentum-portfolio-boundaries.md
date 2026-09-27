# ADR-0087: AI Momentum mandate and enforced protection

**Date:** September 26, 2026 Toronto
**Status:** Principles accepted; API budget and portfolio-proposal foundation implemented offline; host integration and protection parameters pending.
**Domains:** architecture, llm, risk-management
**Related:** ADR-0055, ADR-0062, ADR-0081, ADR-0083, ADR-0085, ADR-0086; `Docs/oracle-rules.md`

## Summary and consequence

Create an AI Momentum strategy for paper evaluation and intended real-account adoption, with
short-term momentum as its mandate. The AI may use or reject discretionary trading rules with a
recorded explanation, but TraderVI enforces the approved hard boundaries. AI Explorer, a separate
paper strategy free to choose its investing style, is deferred to a future session.

The client/proposal foundation is implemented; an operating AI portfolio and Monday launch are not verified.
The requested September 28 launch still depends on the existing native risk, broker and host checks.

## Problem

The immediate question is how an AI can exercise trading judgment while respecting protection.
The parent problem is introducing an auditable complete strategy that can manage paper and actual
holdings independently. The root goal remains deliberate adoption of a strategy for the permanent
TFSA with preserved cash, execution and risk history.

## Accepted principles

- Separate mandate, trading discretion and non-overridable boundaries. Cash, exposure, emergency
  protection and account/data/order validity belong to the enforced boundary; precise parameters
  and the full classification of existing rules remain open.
- Log meaningful decisions, evidence, reasons for overrides and conditions for reconsideration.
  A persuasive explanation is not proof of predictive value or permission to breach a boundary.
- Reduce permitted risk as portfolio losses deepen. The scale, loss reference, recovery conditions
  and protective responses are not yet specified.
- Keep hard stop limits. Do not loosen a stop to prolong a losing trade or repeatedly grant more
  room as its price falls. Exact initial-stop selection and subsequent tightening remain to be defined.
- Protect profit as an objective. This requires a measurable activation threshold and allowed
  giveback (decline from an observed peak), not a promise that every trade avoids losses. Those
  details remain open. A trigger cannot guarantee a fill price or realized loss bound.
- Retain independent paper/live histories and approved strategy identity. Paper outcomes or changes
  cannot silently overwrite real holdings, protection or the live assignment.

## Client direction and current evidence

The operator prefers GPT-6 Astra, is open to API billing and sets an initial incremental AI budget
below CAD 100 per month. With that ceiling, the recommended first implementation adapts Oracle for
API access; the earlier subscription-first exploration is an alternative if measured costs justify it.
A subscription-backed local Codex integration remains a candidate, not a verified TraderVI connection.
Official documentation checked September 26 describes ChatGPT subscription sign-in, programmatic
Codex integrations and reuse of saved CLI authentication; API-key use is a separate usage-based path.
Sources: [authentication](https://learn.chatgpt.com/docs/auth),
[SDK](https://learn.chatgpt.com/docs/codex-sdk),
[automation](https://learn.chatgpt.com/docs/non-interactive-mode),
[requested model](https://developers.openai.com/api/docs/models/gpt-6-astra).
Account/model access, quotas, latency, unattended recovery and data handling remain unqualified.

Source inspection found `Core/Oracle/Llm/ILlmClient.cs` and `OpenAiLlmClient.cs`. The latter uses
Chat Completions and older model-family parameter handling; it has not been validated for Astra.
The operator explicitly authorizes reusing and adapting Oracle's client for AI Momentum. Reuse its
useful client/prompt/usage components while defining the new structured decision and transport contracts.
Oracle's existing rules forbid LLM influence on decisions and constrain historical prompt inputs.
The new proposal-only contract below is separately scoped in `Docs/oracle-rules.md`.
This ADR does not change Oracle's narration behavior
or authorize unrestricted raw history, tools, credentials or broker access for a model.

`Docs/oracle-phases.md` records a successful manual Phase 2 narration run in May; this is historical
documentation, not fresh database verification. September 26 at 21:07 Toronto, read-only process
inspection found no Oracle executable or matching dotnet-hosted Oracle process. Scheduled-task
inspection found no task name/action directly mentioning Oracle; the matching TraderVI Nightly task
does not mention it, and `Operations/nightly-runner.json` contains Hermes, Delphi and Athena only.
This bounded check does not establish all past runs or rule out arbitrary indirect launchers.

Architecture: persist a dated decision packet, request a structured proposal and validate it in
TraderVI. The implemented reviewer has no execution interface. Eventual submission must use the
established execution path. Protective monitoring must operate independently of model availability;
host integration and outage behavior remain to be designed.

## First source increment — September 26

`AiPortfolioReviewer` accepts a version-1 packet of dated evidence, eligible portfolio versions,
classified rules and evaluated lessons. It freezes the packet before requesting strict JSON and
checks freshness again after the response. Allowed proposals are wait, keep, select and switch;
unknown references, changed versions, unsupported actions and hard-rule overrides are rejected.
Each discretionary exception needs its own reason. New learning observations are hypotheses only.
The host still must build authoritative packets, classify rules, evaluate lessons and recheck current
assignment, evidence and risk before acting. A cited evidence ID does not prove the model's rationale.

`OpenAiResponsesLlmClient` reuses `ILlmClient` and Oracle's API-key configuration through an explicit,
disabled-by-default factory entry point. Existing Oracle composition is unchanged. The new path uses
GPT-6 Astra, standard reasoning/billing, strict structured output, no tools, no server conversation,
`store=false` and disabled truncation. These request settings are not a zero-retention guarantee.
It counts the matching input before reserving the bounded response cost, including reasoning tokens.
No automatic retries, model fallback or response-generated tools are allowed. Authentication, model
access, provider billing and live latency are still unverified. Injected HTTP transports must preserve
single-transmission behavior and must not redirect credentials or add retries.

`AiCallJournal` persists the exact request and hash before generation, then the raw response and
conservative usage estimate. A stable call ID cannot be reused. One process owns the journal and
concurrent reservations share one UTC calendar-month budget. Unknown outcomes retain their full
reservation across restart/month boundaries; settlements spanning months count conservatively in
both months. A lower ceiling cannot be raised within its starting month. Missing/corrupt storage or
an interrupted write blocks further calls; usage overruns or unexpected billing identity block them
durably. Recovery/retention tooling and domain acceptance/rejection dispositions remain host work.
The private JSON is not encrypted: production needs a protected directory outside the checkout,
one stable shared journal for every AI Momentum role, backups and reviewed recovery. Do not create
another journal to reset spending. The SHA-256 is an integrity check, not tamper-proof attestation.

The default working ceiling is CAD 90, with any policy ceiling required to remain below CAD 100.
Currency/tax/fee upper bounds and their expiration are mandatory explicit inputs; none are activated.
Default per-call limits are 32,000 input and 4,096 total output tokens. Input is conservatively charged
at USD 12.50/million and output at USD 50/million, with no anticipated cache savings. Input above
272,000 tokens is forbidden to avoid the higher price bracket. Rates, conversion and other charges
must be requalified before activation; this local estimate is not a provider invoice or an account-wide
cap on unrelated clients. Sources checked September 26:
[model pricing](https://developers.openai.com/api/docs/models/gpt-6-astra),
[exact token counting](https://developers.openai.com/api/docs/guides/token-counting),
[structured output](https://developers.openai.com/api/docs/guides/structured-outputs).

Synthetic transport/packet tests cover budget exhaustion, concurrency, restarts, month rollover,
cancellation, interrupted saves, response failures and unsafe proposals. See project status for the
validation result. No real API requests, production journal, database changes or broker operations
are part of this increment. It does not run a portfolio or implement continuous learning.

## Open work and validation

### Spending and expanded discretionary role

The budget covers all AI Momentum model roles, reviews, learning calls and retries together, separate
from the operator's existing Pro subscription. The library now reserves bounded input/output cost
and reconciles returned usage; production conversion, tax treatment and cadence still need review.
A dashboard alert alone is insufficient.
At the limit, stop discretionary model calls while independent deterministic protection continues.
The operator accepts independent protection while AI reviews run less often. No cost control is
operationally installed yet. Profits may justify a later reviewed increase, not an automatic spending escalation.

The operator now requests an AI supervisor that selects an existing portfolio as its active strategy
context, exercises discretion inside that context, monitors alternatives and may switch in real time.
This changes the proposed role from only picking stocks to managing strategy selection. A portfolio
instance, its rule set and its execution engine are related but distinct existing contracts; do not
treat switching as merely changing a prompt. Eligibility, authorized strategy set, handover, retained
protection, costs and history still require a defined contract under ADR-0085/0086.

Self-improvement is a requested integral mandate. Define persistent lessons, outcome evaluation and
versioned strategy changes; no model-weight training, self-editing code, risk-boundary changes or
unlimited learning calls are implied. Separate agents/chat histories are a user hypothesis to assess,
not a selected implementation or an instruction to launch subagents. All would share the same budget.
Discuss portfolio selection/switching and then learning separately; the operator requests short,
single-topic responses. AI Explorer remains deferred.

Define numerical protection, profit giveback, decision cadence, source adapters and production
composition. Complete audit dispositions, outcome evaluation and execution failure/restart behavior.
See [open questions](../reviews/open-questions.md#ai-managed-portfolio-and-proposed-live-adoption--september-26-2026)
and the [implementation checklist](../strategy-alignment-implementation-checklist.md#left-to-do--development-order).
Runtime library code exists; no model calls, database changes or broker operations have been performed.

## Review questions

1. Which trading decisions may the AI override, and who enforces hard boundaries?
2. Why must a losing position not receive a progressively looser protective stop?
3. Does ChatGPT subscription access establish a working unattended TraderVI client or API credit?
