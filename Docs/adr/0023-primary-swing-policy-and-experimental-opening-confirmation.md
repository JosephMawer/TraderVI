# ADR-0023: Primary swing policy and experimental opening confirmation

- **Status:** Accepted
- **Date:** 2026-08-23
- **Domains:** architecture, decision-engine, market-microstructure, risk-management
- **Related:** ADR-0013, ADR-0015, ADR-0020, ADR-0021, ADR-0022
- **Refined by:** ADR-0025 for the initial three-session mark-to-market measure

## Context

The immediate problem is deciding how an opening move should affect a Delphi recommendation and whether same-day and multi-day trades belong to one policy. The parent problem is aligning paper outcomes with the intended short-term use of Delphi. The root goal is to tune Delphi from reproducible evidence while the user retains strategic control.

High opening activity does not by itself prove that price direction confirms or contradicts the completed-session thesis. A morning move may reflect new information, price discovery, a temporary liquidity imbalance, or execution friction. Combining same-day exits and multi-day trend holding in one result would hide those different risks and costs.

## Decision

Use a **multi-day swing policy** as Delphi's primary trade-management direction. A position is expected to last roughly three to five completed sessions, with a future versioned exit rule allowed to keep a healthy trend longer. The exact trailing, profit-protection, time-limit, and stop rules remain unresolved until separately reviewed.

Keep the current completed-daily-session Delphi evaluation as the baseline selector. Do not keep Delphi running overnight. A future opening-confirmation process, if implemented, runs separately after the market opens and persists its own timestamped inputs and decision.

Treat opening confirmation as an experimental challenger, not an automatic veto. Initially compare at least these policies against the same baseline candidate evidence:

1. ignore the opening move;
2. use opening evidence as a soft rank or confidence adjustment;
3. require confirmation before a paper entry.

Only input-integrity or execution-safety conditions—such as unavailable data, an untradeable spread, a halt, or unusable liquidity—may be designed as immediate hard exclusions. Directional contradiction, gap thresholds, relative volume, VWAP relationships, and the observation delay require paper evidence and the ADR-0022 human-promotion process before they can alter the champion.

Keep **intraday wave trading** as a separate experimental execution policy with its own entries, exits, costs, risks, and scoreboard. Do not combine its performance with the primary swing policy. It may reuse Delphi's candidate evidence, but it is not the current implementation priority.

Retain the accepted 10- and 20-session prediction outcomes for model-label compatibility and longer-path diagnostics. New 1-, 2-, and 3-session economic measures and the eventual swing exit policy require new immutable outcome-definition versions; they do not rewrite the existing definitions.

The initial 9:45 a.m. checkpoint and comparisons at 5, 15, and 30 minutes are **proposed research defaults**, not accepted production thresholds. The intraday source, quote/spread contract, checkpoint, and exact swing exit rule remain open decisions.

## Alternatives considered

- **Let any opening contradiction veto Delphi.** Rejected because the opening move has not yet demonstrated incremental value and may discard profitable swing candidates.
- **Ignore the opening permanently.** Rejected because timestamped opening price, volume, gap, and liquidity evidence may improve entry quality and can be tested without changing the champion.
- **Combine intraday and swing results.** Rejected because their holding periods, transaction costs, opportunity sets, and exit risks are materially different.
- **Run one Delphi process overnight and pause until the open.** Rejected because it creates unnecessary operational fragility and obscures the boundary between completed-session analysis and new intraday evidence.

## Consequences

- The next tradeable-outcome work prioritizes a separately versioned multi-day swing policy.
- Intraday collection and opening confirmation need an explicit as-of timestamp, source/entitlement review, data-quality contract, and separate evidence identity.
- Any future opening confirmer should persist a baseline-versus-challenger comparison, including avoided losses, missed winners, and costs.
- Existing 10/20-session outcomes remain valid for their original purpose but no longer define the primary economic holding target.
- No opening rule or intraday policy may activate automatically; the user approves any promotion.

## Review questions

1. Why is an opening directional contradiction a challenger signal rather than an immediate veto?
2. Why must intraday wave and multi-day swing results remain separate?
3. Which opening conditions may be hard exclusions before directional confirmation proves itself?
4. Which timing and exit details remain proposed rather than accepted?
