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

*(none yet — first concept entries will land alongside the Weighting work:*
*`price-weighted-vs-cap-weighted-indices.md` and `narrow-leadership.md`.)*

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
