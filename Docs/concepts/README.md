# Concepts

This folder explains the *ideas* behind the system — the "what" and "why
it matters" — separately from the decisions captured in ADRs.

ADRs answer *"why did we choose X over Y?"*
Concepts answer *"what is X, and why does it matter to a trading system?"*

A single ADR may reference several concepts; a single concept may inform
many ADRs.

## Conventions

- **Filename:** `kebab-title.md` (no numeric prefix — concepts evolve, ADRs are immutable).
- **Domains:** same tag taxonomy as ADRs (see `../adr/README.md`).
- **Length:** prefer 1–3 pages. If a concept gets longer, split it.
- Each concept ends with a **Review questions** section, just like ADRs.

## Index

- `price-weighted-contribution.md` — proxy used by the Granville Weighting group.
- `oracle-prompt-tightening.md` — lessons from the Oracle Phase 2 prompt pass
  (shared-context suppression, curated JSON view, GPT-5 API quirks).
- `ranking-lenses.md` — multi-lens evaluation (lens = thesis × gate stack ×
  ranking key); Continuations executed, Breakouts journaled.
- `paper-calibration-and-outcome-feedback.md` — draft architecture and measurement
  contract for immutable prediction outcomes, tradeable outcomes, shadow
  portfolios, and controlled self-calibration.

## Concept template

```
# <Concept name>

- **Domains:** tag1, tag2
- **Related ADRs:** ADR-NNNN, ADR-MMMM

## Summary
One paragraph. The crux of the idea.

## Why it matters here
How does this concept show up in TraderVI specifically?

## Details
Longer explanation, formulas, diagrams, examples.

## Common pitfalls / misconceptions
What is easy to get wrong?

## Review questions
1. ...
2. ...
```
